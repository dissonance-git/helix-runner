using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

if ( !OperatingSystem.IsWindows() )
{
    Console.Error.WriteLine( "Windows is required." );
    return 2;
}

return args.Length > 0 && args[0] == "child"
    ? await Probe.ChildAsync( args[1..] )
    : await Probe.ParentAsync();

static class Probe
{
    const int AlreadyExistsHresult = unchecked((int)0x800700B7);
    const int ErrorInsufficientBuffer = 122;
    const uint ExtendedStartupInfoPresent = 0x00080000;
    const uint CreateNoWindow = 0x08000000;
    const uint WaitObject0 = 0;
    const uint WaitTimeout = 258;
    const uint TokenQuery = 0x0008;
    const int TokenIsAppContainer = 29;
    static readonly IntPtr SecurityCapabilitiesAttribute = new( 0x00020009 );

    [SupportedOSPlatform( "windows" )]
    public static async Task<int> ParentAsync()
    {
        var profile = "HelixProbe_" + Guid.NewGuid().ToString( "N" );
        IntPtr sid = IntPtr.Zero;
        TcpListener? listener = null;
        string? root = null;

        try
        {
            var hr = CreateAppContainerProfile(
                profile,
                "Helix AppContainer Probe",
                "Disposable Helix boundary qualification",
                IntPtr.Zero,
                0,
                out sid );

            if ( hr == AlreadyExistsHresult )
                hr = DeriveAppContainerSidFromAppContainerName( profile, out sid );

            if ( hr < 0 || sid == IntPtr.Zero )
                throw new InvalidOperationException(
                    $"CreateAppContainerProfile failed: 0x{hr:X8}" );

            var securityIdentifier = new SecurityIdentifier( sid );

            root = Path.Combine(
                Path.GetTempPath(),
                "helix-appcontainer-probe-" + Guid.NewGuid().ToString( "N" ) );

            var allowed = Path.Combine( root, "allowed" );
            var denied = Path.Combine( root, "denied" );
            Directory.CreateDirectory( allowed );
            Directory.CreateDirectory( denied );

            File.WriteAllText(
                Path.Combine( denied, "secret.txt" ),
                "host-secret" );

            GrantDirectory(
                AppContext.BaseDirectory,
                securityIdentifier,
                FileSystemRights.ReadAndExecute );

            GrantDirectory(
                allowed,
                securityIdentifier,
                FileSystemRights.Modify |
                FileSystemRights.ReadAndExecute );

            listener = new TcpListener( IPAddress.Loopback, 0 );
            listener.Start();
            var endpoint = (IPEndPoint)listener.LocalEndpoint;

            using ( var control = new TcpClient() )
            {
                var accept = listener.AcceptTcpClientAsync();
                await control.ConnectAsync(
                    IPAddress.Loopback,
                    endpoint.Port ).WaitAsync(
                        TimeSpan.FromSeconds( 5 ) );
                using var accepted = await accept.WaitAsync(
                    TimeSpan.FromSeconds( 5 ) );
            }

            var resultPath = Path.Combine( allowed, "result.json" );
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "Probe executable path is unavailable." );

            var exitCode = StartContainerChild(
                executable,
                allowed,
                denied,
                endpoint.Port,
                resultPath,
                sid );

            if ( !File.Exists( resultPath ) )
                throw new InvalidOperationException(
                    $"Sandbox child exited {exitCode} without writing a result." );

            var result = JsonSerializer.Deserialize<ChildResult>(
                File.ReadAllText( resultPath ) )
                ?? throw new InvalidOperationException(
                    "Sandbox child result was empty." );

            var passed =
                exitCode == 0 &&
                result.IsAppContainer &&
                result.AllowedWrite &&
                result.DeniedReadBlocked &&
                result.NetworkBlocked;

            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        profile,
                        sid = securityIdentifier.Value,
                        childExitCode = exitCode,
                        result
                    } ) );

            if ( !passed )
            {
                Console.Error.WriteLine(
                    "HELIX_APPCONTAINER_PROBE_FAILED" );
                return 1;
            }

            Console.WriteLine(
                "HELIX_APPCONTAINER_PROBE_OK " +
                "filesystem=confined network=confined " +
                "capabilities=none" );
            return 0;
        }
        finally
        {
            listener?.Stop();

            if ( sid != IntPtr.Zero )
                _ = FreeSid( sid );

            _ = DeleteAppContainerProfile( profile );

            if ( root is not null )
            {
                try
                {
                    Directory.Delete(
                        root,
                        recursive: true );
                }
                catch
                {
                }
            }
        }
    }

    [SupportedOSPlatform( "windows" )]
    public static async Task<int> ChildAsync(
        string[] args )
    {
        if ( args.Length != 4 ||
             !int.TryParse( args[2], out var port ) )
            return 20;

        var allowed = args[0];
        var denied = args[1];
        var resultPath = args[3];

        var isAppContainer = IsCurrentProcessAppContainer();

        var allowedWrite = false;
        string? allowedError = null;
        try
        {
            File.WriteAllText(
                Path.Combine( allowed, "child-write.txt" ),
                "allowed" );
            allowedWrite = true;
        }
        catch ( Exception exception )
        {
            allowedError =
                exception.GetType().Name + ": " + exception.Message;
        }

        var deniedReadBlocked = false;
        string? deniedError = null;
        try
        {
            _ = File.ReadAllText(
                Path.Combine( denied, "secret.txt" ) );
        }
        catch ( Exception exception )
        {
            deniedReadBlocked = true;
            deniedError =
                exception.GetType().Name + ": " + exception.Message;
        }

        var networkBlocked = false;
        string? networkError = null;
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(
                IPAddress.Loopback,
                port ).WaitAsync(
                    TimeSpan.FromSeconds( 3 ) );
        }
        catch ( Exception exception )
        {
            networkBlocked = true;
            networkError =
                exception.GetType().Name + ": " + exception.Message;
        }

        var result = new ChildResult(
            isAppContainer,
            allowedWrite,
            deniedReadBlocked,
            networkBlocked,
            allowedError,
            deniedError,
            networkError );

        File.WriteAllText(
            resultPath,
            JsonSerializer.Serialize( result ) );

        return isAppContainer &&
               allowedWrite &&
               deniedReadBlocked &&
               networkBlocked
            ? 0
            : 21;
    }

    [SupportedOSPlatform( "windows" )]
    static void GrantDirectory(
        string path,
        SecurityIdentifier sid,
        FileSystemRights rights )
    {
        var info = new DirectoryInfo( path );
        var security = info.GetAccessControl();

        security.AddAccessRule(
            new FileSystemAccessRule(
                sid,
                rights,
                InheritanceFlags.ContainerInherit |
                InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow ) );

        info.SetAccessControl( security );
    }

    [SupportedOSPlatform( "windows" )]
    static int StartContainerChild(
        string executable,
        string allowed,
        string denied,
        int port,
        string resultPath,
        IntPtr sid )
    {
        nuint attributeBytes = 0;

        if ( InitializeProcThreadAttributeList(
                 IntPtr.Zero,
                 1,
                 0,
                 ref attributeBytes ) ||
             Marshal.GetLastWin32Error() != ErrorInsufficientBuffer )
            throw new InvalidOperationException(
                "Unable to size process attribute list." );

        var attributeList = Marshal.AllocHGlobal(
            checked((nint)attributeBytes) );
        var capabilitiesPointer = IntPtr.Zero;
        PROCESS_INFORMATION processInformation = default;

        try
        {
            if ( !InitializeProcThreadAttributeList(
                     attributeList,
                     1,
                     0,
                     ref attributeBytes ) )
                ThrowLastWin32(
                    "InitializeProcThreadAttributeList" );

            var capabilities = new SECURITY_CAPABILITIES
            {
                AppContainerSid = sid,
                Capabilities = IntPtr.Zero,
                CapabilityCount = 0,
                Reserved = 0
            };

            capabilitiesPointer = Marshal.AllocHGlobal(
                Marshal.SizeOf<SECURITY_CAPABILITIES>() );
            Marshal.StructureToPtr(
                capabilities,
                capabilitiesPointer,
                fDeleteOld: false );

            if ( !UpdateProcThreadAttribute(
                     attributeList,
                     0,
                     SecurityCapabilitiesAttribute,
                     capabilitiesPointer,
                     (nuint)Marshal.SizeOf<SECURITY_CAPABILITIES>(),
                     IntPtr.Zero,
                     IntPtr.Zero ) )
                ThrowLastWin32(
                    "UpdateProcThreadAttribute" );

            var startup = new STARTUPINFOEX
            {
                StartupInfo = new STARTUPINFO
                {
                    cb = (uint)Marshal.SizeOf<STARTUPINFOEX>()
                },
                lpAttributeList = attributeList
            };

            var commandLine = new StringBuilder(
                string.Join(
                    " ",
                    Quote( executable ),
                    "child",
                    Quote( allowed ),
                    Quote( denied ),
                    port.ToString(
                        System.Globalization.CultureInfo.InvariantCulture ),
                    Quote( resultPath ) ) );

            if ( !CreateProcessW(
                     executable,
                     commandLine,
                     IntPtr.Zero,
                     IntPtr.Zero,
                     false,
                     ExtendedStartupInfoPresent |
                     CreateNoWindow,
                     IntPtr.Zero,
                     allowed,
                     ref startup,
                     out processInformation ) )
                ThrowLastWin32(
                    "CreateProcessW(AppContainer)" );

            var wait = WaitForSingleObject(
                processInformation.hProcess,
                20_000 );

            if ( wait == WaitTimeout )
            {
                _ = TerminateProcess(
                    processInformation.hProcess,
                    125 );
                _ = WaitForSingleObject(
                    processInformation.hProcess,
                    5_000 );

                throw new TimeoutException(
                    "AppContainer child exceeded probe timeout." );
            }

            if ( wait != WaitObject0 )
                ThrowLastWin32(
                    "WaitForSingleObject" );

            if ( !GetExitCodeProcess(
                     processInformation.hProcess,
                     out var exitCode ) )
                ThrowLastWin32(
                    "GetExitCodeProcess" );

            return unchecked((int)exitCode);
        }
        finally
        {
            if ( processInformation.hThread != IntPtr.Zero )
                _ = CloseHandle(
                    processInformation.hThread );

            if ( processInformation.hProcess != IntPtr.Zero )
                _ = CloseHandle(
                    processInformation.hProcess );

            if ( attributeList != IntPtr.Zero )
            {
                DeleteProcThreadAttributeList(
                    attributeList );
                Marshal.FreeHGlobal(
                    attributeList );
            }

            if ( capabilitiesPointer != IntPtr.Zero )
                Marshal.FreeHGlobal(
                    capabilitiesPointer );
        }
    }

    [SupportedOSPlatform( "windows" )]
    static bool IsCurrentProcessAppContainer()
    {
        if ( !OpenProcessToken(
                 GetCurrentProcess(),
                 TokenQuery,
                 out var token ) )
            ThrowLastWin32(
                "OpenProcessToken" );

        try
        {
            var value = 0;

            if ( !GetTokenInformation(
                     token,
                     TokenIsAppContainer,
                     out value,
                     sizeof(int),
                     out _ ) )
                ThrowLastWin32(
                    "GetTokenInformation(TokenIsAppContainer)" );

            return value != 0;
        }
        finally
        {
            _ = CloseHandle( token );
        }
    }

    static string Quote(
        string value )
        => "\"" +
           value.Replace(
               "\\"",
               "\\\\"",
               StringComparison.Ordinal ) +
           "\"";

    [SupportedOSPlatform( "windows" )]
    static void ThrowLastWin32(
        string operation )
        => throw new System.ComponentModel.Win32Exception(
            Marshal.GetLastWin32Error(),
            operation );

    sealed record ChildResult(
        bool IsAppContainer,
        bool AllowedWrite,
        bool DeniedReadBlocked,
        bool NetworkBlocked,
        string? AllowedError,
        string? DeniedError,
        string? NetworkError );

    [StructLayout( LayoutKind.Sequential )]
    struct SECURITY_CAPABILITIES
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }

    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
    struct STARTUPINFO
    {
        public uint cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout( LayoutKind.Sequential, CharSet = CharSet.Unicode )]
    struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout( LayoutKind.Sequential )]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport(
        "userenv.dll",
        CharSet = CharSet.Unicode )]
    static extern int CreateAppContainerProfile(
        string pszAppContainerName,
        string pszDisplayName,
        string pszDescription,
        IntPtr pCapabilities,
        uint dwCapabilityCount,
        out IntPtr ppSidAppContainerSid );

    [DllImport(
        "userenv.dll",
        CharSet = CharSet.Unicode )]
    static extern int DeriveAppContainerSidFromAppContainerName(
        string pszAppContainerName,
        out IntPtr ppsidAppContainerSid );

    [DllImport(
        "userenv.dll",
        CharSet = CharSet.Unicode )]
    static extern int DeleteAppContainerProfile(
        string pszAppContainerName );

    [DllImport( "advapi32.dll" )]
    static extern IntPtr FreeSid(
        IntPtr pSid );

    [DllImport(
        "kernel32.dll",
        SetLastError = true )]
    static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        uint dwFlags,
        ref nuint lpSize );

    [DllImport(
        "kernel32.dll",
        SetLastError = true )]
    static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        nuint cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize );

    [DllImport( "kernel32.dll" )]
    static extern void DeleteProcThreadAttributeList(
        IntPtr lpAttributeList );

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true )]
    static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation );

    [DllImport( "kernel32.dll" )]
    static extern IntPtr GetCurrentProcess();

    [DllImport(
        "advapi32.dll",
        SetLastError = true )]
    static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle );

    [DllImport(
        "advapi32.dll",
        SetLastError = true )]
    static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        out int tokenInformation,
        int tokenInformationLength,
        out int returnLength );

    [DllImport(
        "kernel32.dll",
        SetLastError = true )]
    static extern uint WaitForSingleObject(
        IntPtr hHandle,
        uint dwMilliseconds );

    [DllImport(
        "kernel32.dll",
        SetLastError = true )]
    static extern bool GetExitCodeProcess(
        IntPtr hProcess,
        out uint lpExitCode );

    [DllImport(
        "kernel32.dll",
        SetLastError = true )]
    static extern bool TerminateProcess(
        IntPtr hProcess,
        uint uExitCode );

    [DllImport(
        "kernel32.dll",
        SetLastError = true )]
    static extern bool CloseHandle(
        IntPtr hObject );
}
