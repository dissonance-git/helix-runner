using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

internal static class Program
{
    const uint ExtendedStartupInfoPresent = 0x00080000;
    const uint CreateNoWindow = 0x08000000;
    const uint Infinite = 0xFFFFFFFF;
    static readonly IntPtr ProcThreadAttributeSecurityCapabilities = new( 0x00020009 );

    static async Task<int> Main( string[] args )
    {
        if ( args.Length > 0 && args[0] == "--child" )
            return await ChildAsync( args );

        return await LauncherAsync();
    }

    static async Task<int> LauncherAsync()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "helix-appcontainer-" + Guid.NewGuid().ToString( "N" ) );
        var allowed = Path.Combine( root, "allowed" );
        var denied = Path.Combine( root, "denied" );
        Directory.CreateDirectory( allowed );
        Directory.CreateDirectory( denied );

        var allowedFile = Path.Combine( allowed, "allowed.txt" );
        var deniedFile = Path.Combine( denied, "denied.txt" );
        var resultFile = Path.Combine( allowed, "result.json" );
        File.WriteAllText( allowedFile, "allowed" );
        File.WriteAllText( deniedFile, "denied" );

        var controlNetwork = await CanReachPublicTcpAsync();

        var moniker = "HelixProbe." + Guid.NewGuid().ToString( "N" );
        IntPtr sid = IntPtr.Zero;
        var created = false;

        try
        {
            var hr = CreateAppContainerProfile(
                moniker,
                "Helix AppContainer Probe",
                "Disposable Helix confinement witness",
                IntPtr.Zero,
                0,
                out sid );

            if ( hr < 0 )
                Marshal.ThrowExceptionForHR( hr );

            created = true;
            var sidText = SidToString( sid );

            Grant( AppContext.BaseDirectory, sidText, "(OI)(CI)RX" );
            Grant( allowed, sidText, "(OI)(CI)M" );
            Deny( denied, sidText, "(OI)(CI)R" );

            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException( "Process path unavailable." );
            var commandLine = string.Join(
                " ",
                Quote( executable ),
                "--child",
                Quote( allowedFile ),
                Quote( deniedFile ),
                Quote( resultFile ) );

            var exitCode = LaunchAppContainer(
                executable,
                commandLine,
                allowed,
                sid );

            if ( exitCode != 0 )
                throw new InvalidOperationException(
                    $"AppContainer child exited with code {exitCode}." );

            if ( !File.Exists( resultFile ) )
                throw new InvalidOperationException(
                    "AppContainer child did not write its result." );

            var result = JsonSerializer.Deserialize<ProbeResult>(
                File.ReadAllText( resultFile ) )
                ?? throw new InvalidOperationException( "Probe result was empty." );

            var passed =
                controlNetwork &&
                result.AllowedRead &&
                result.DeniedReadBlocked &&
                result.NetworkBlocked;

            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        passed,
                        controlNetwork,
                        appContainerSid = sidText,
                        result.AllowedRead,
                        result.DeniedReadBlocked,
                        result.NetworkBlocked,
                        result.DeniedError,
                        result.NetworkError
                    } ) );

            return passed ? 0 : 1;
        }
        finally
        {
            if ( sid != IntPtr.Zero )
                _ = FreeSid( sid );

            if ( created )
                _ = DeleteAppContainerProfile( moniker );

            try
            {
                if ( Directory.Exists( root ) )
                    Directory.Delete( root, recursive: true );
            }
            catch
            {
            }
        }
    }

    static async Task<int> ChildAsync( string[] args )
    {
        if ( args.Length != 4 )
            return 64;

        var allowedRead = false;
        var deniedReadBlocked = false;
        var deniedError = "";
        var networkBlocked = false;
        var networkError = "";

        try
        {
            allowedRead =
                string.Equals(
                    File.ReadAllText( args[1] ),
                    "allowed",
                    StringComparison.Ordinal );
        }
        catch ( Exception exception )
        {
            deniedError = "allowed:" + exception.GetType().Name;
        }

        try
        {
            _ = File.ReadAllText( args[2] );
        }
        catch ( Exception exception )
        {
            deniedReadBlocked = true;
            deniedError = exception.GetType().Name;
        }

        try
        {
            networkBlocked = !await CanReachPublicTcpAsync();
            if ( networkBlocked )
                networkError = "connection-denied-or-unreachable";
        }
        catch ( Exception exception )
        {
            networkBlocked = true;
            networkError = exception.GetType().Name;
        }

        var result = new ProbeResult(
            allowedRead,
            deniedReadBlocked,
            networkBlocked,
            deniedError,
            networkError );

        File.WriteAllText(
            args[3],
            JsonSerializer.Serialize( result ) );

        return 0;
    }

    static async Task<bool> CanReachPublicTcpAsync()
    {
        using var client = new TcpClient( AddressFamily.InterNetwork );
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds( 5 ) );

        try
        {
            await client.ConnectAsync(
                IPAddress.Parse( "1.1.1.1" ),
                443,
                cancellation.Token );
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }

    static int LaunchAppContainer(
        string executable,
        string commandLine,
        string workingDirectory,
        IntPtr appContainerSid )
    {
        var attributeBytes = IntPtr.Zero;
        _ = InitializeProcThreadAttributeList(
            IntPtr.Zero,
            1,
            0,
            ref attributeBytes );

        if ( attributeBytes == IntPtr.Zero )
            throw new InvalidOperationException(
                "Unable to size process attribute list.",
                new System.ComponentModel.Win32Exception(
                    Marshal.GetLastWin32Error() ) );

        var attributeList = Marshal.AllocHGlobal( attributeBytes );
        var securityPointer = IntPtr.Zero;

        try
        {
            if ( !InitializeProcThreadAttributeList(
                    attributeList,
                    1,
                    0,
                    ref attributeBytes ) )
                ThrowLastWin32( "InitializeProcThreadAttributeList" );

            var security = new SECURITY_CAPABILITIES
            {
                AppContainerSid = appContainerSid,
                Capabilities = IntPtr.Zero,
                CapabilityCount = 0,
                Reserved = 0
            };

            securityPointer = Marshal.AllocHGlobal(
                Marshal.SizeOf<SECURITY_CAPABILITIES>() );
            Marshal.StructureToPtr(
                security,
                securityPointer,
                fDeleteOld: false );

            if ( !UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    ProcThreadAttributeSecurityCapabilities,
                    securityPointer,
                    new IntPtr( Marshal.SizeOf<SECURITY_CAPABILITIES>() ),
                    IntPtr.Zero,
                    IntPtr.Zero ) )
                ThrowLastWin32( "UpdateProcThreadAttribute" );

            var startup = new STARTUPINFOEX();
            startup.StartupInfo.cb =
                Marshal.SizeOf<STARTUPINFOEX>();
            startup.lpAttributeList =
                attributeList;

            var mutableCommandLine = new StringBuilder(
                commandLine );

            if ( !CreateProcessW(
                    executable,
                    mutableCommandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    ExtendedStartupInfoPresent | CreateNoWindow,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startup,
                    out var process ) )
                ThrowLastWin32( "CreateProcessW" );

            try
            {
                var wait = WaitForSingleObject(
                    process.hProcess,
                    60_000 );

                if ( wait == Infinite )
                    ThrowLastWin32( "WaitForSingleObject" );

                if ( !GetExitCodeProcess(
                        process.hProcess,
                        out var exitCode ) )
                    ThrowLastWin32( "GetExitCodeProcess" );

                return unchecked( (int)exitCode );
            }
            finally
            {
                _ = CloseHandle( process.hThread );
                _ = CloseHandle( process.hProcess );
            }
        }
        finally
        {
            if ( securityPointer != IntPtr.Zero )
                Marshal.FreeHGlobal( securityPointer );

            if ( attributeList != IntPtr.Zero )
            {
                DeleteProcThreadAttributeList(
                    attributeList );
                Marshal.FreeHGlobal(
                    attributeList );
            }
        }
    }

    static string SidToString( IntPtr sid )
    {
        if ( !ConvertSidToStringSidW(
                sid,
                out var text ) )
            ThrowLastWin32( "ConvertSidToStringSidW" );

        try
        {
            return Marshal.PtrToStringUni( text )
                ?? throw new InvalidOperationException( "SID string unavailable." );
        }
        finally
        {
            _ = LocalFree( text );
        }
    }

    static void Grant(
        string path,
        string sid,
        string rights )
        => RunIcacls(
            path,
            "/grant",
            "*" + sid + ":" + rights );

    static void Deny(
        string path,
        string sid,
        string rights )
        => RunIcacls(
            path,
            "/deny",
            "*" + sid + ":" + rights );

    static void RunIcacls(
        string path,
        string operation,
        string rule )
    {
        var start = new ProcessStartInfo( "icacls.exe" )
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        start.ArgumentList.Add( path );
        start.ArgumentList.Add( operation );
        start.ArgumentList.Add( rule );
        start.ArgumentList.Add( "/T" );
        start.ArgumentList.Add( "/C" );

        using var process = Process.Start( start )
            ?? throw new InvalidOperationException( "icacls did not start." );
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if ( process.ExitCode != 0 )
            throw new InvalidOperationException(
                $"icacls failed ({process.ExitCode}): {stderr} {stdout}" );
    }

    static string Quote( string value )
        => "\"" + value.Replace( "\"", "\\\"" ) + "\"";

    static void ThrowLastWin32( string operation )
        => throw new InvalidOperationException(
            operation + " failed.",
            new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error() ) );

    sealed record ProbeResult(
        bool AllowedRead,
        bool DeniedReadBlocked,
        bool NetworkBlocked,
        string DeniedError,
        string NetworkError );

    [StructLayout( LayoutKind.Sequential )]
    struct SECURITY_CAPABILITIES
    {
        public IntPtr AppContainerSid;
        public IntPtr Capabilities;
        public uint CapabilityCount;
        public uint Reserved;
    }

    [StructLayout( LayoutKind.Sequential )]
    struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout( LayoutKind.Sequential )]
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

    [DllImport( "userenv.dll", CharSet = CharSet.Unicode )]
    static extern int CreateAppContainerProfile(
        string pszAppContainerName,
        string pszDisplayName,
        string pszDescription,
        IntPtr pCapabilities,
        uint dwCapabilityCount,
        out IntPtr ppSidAppContainerSid );

    [DllImport( "userenv.dll", CharSet = CharSet.Unicode )]
    static extern int DeleteAppContainerProfile(
        string pszAppContainerName );

    [DllImport( "advapi32.dll" )]
    static extern IntPtr FreeSid(
        IntPtr pSid );

    [DllImport( "advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
    [return: MarshalAs( UnmanagedType.Bool )]
    static extern bool ConvertSidToStringSidW(
        IntPtr sid,
        out IntPtr stringSid );

    [DllImport( "kernel32.dll", SetLastError = true )]
    [return: MarshalAs( UnmanagedType.Bool )]
    static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        uint dwFlags,
        ref IntPtr lpSize );

    [DllImport( "kernel32.dll", SetLastError = true )]
    [return: MarshalAs( UnmanagedType.Bool )]
    static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        uint dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize );

    [DllImport( "kernel32.dll" )]
    static extern void DeleteProcThreadAttributeList(
        IntPtr lpAttributeList );

    [DllImport( "kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true )]
    [return: MarshalAs( UnmanagedType.Bool )]
    static extern bool CreateProcessW(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs( UnmanagedType.Bool )] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation );

    [DllImport( "kernel32.dll", SetLastError = true )]
    static extern uint WaitForSingleObject(
        IntPtr hHandle,
        uint dwMilliseconds );

    [DllImport( "kernel32.dll", SetLastError = true )]
    [return: MarshalAs( UnmanagedType.Bool )]
    static extern bool GetExitCodeProcess(
        IntPtr hProcess,
        out uint lpExitCode );

    [DllImport( "kernel32.dll" )]
    [return: MarshalAs( UnmanagedType.Bool )]
    static extern bool CloseHandle(
        IntPtr hObject );

    [DllImport( "kernel32.dll" )]
    static extern IntPtr LocalFree(
        IntPtr hMem );
}
