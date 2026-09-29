# helix-runner

Public ephemeral execution host for the Helix ecosystem.

This repository is **not** a source mirror, project workspace, validation authority, or alternate publication line. Canonical project bytes remain in GitLab. The repository carries only small control code and opaque request identifiers that wake standard GitHub-hosted runners.

## Hosted runner status

This repository remains the public home for disposable GitHub-hosted Helix execution. The former `helix-workspace` and `helix-build` Supabase source-broker functions are retired and return HTTP 410. Workflows that still call those brokers are retained as migration evidence/control code, not as currently available verification routes.

The next hosted source handoff must bind GitHub-hosted runners to the current admitted private transport without making this public repository a source mirror or introducing GitLab runner state.

```text
canonical private project bytes
→ current admitted private handoff
→ opaque job identity
→ GitHub-hosted runner
→ bounded project verifier
→ retained result/evidence
```

## Windows runner target

The intended Windows execution shape remains:

```text
exact admitted private source
→ GitHub-hosted windows-2025
→ project-owned Windows verifier
→ bounded artifact/evidence
```

For Omniphony, the verifier remains `ci/windows-product.ps1`. For EFT2, the engine-independent reference verifier remains `tools/run-verification.ps1 reference`. The checked-in `windows-build.yml`, `workspace*.yml`, and request files still document the previous broker protocol, but they are not current authority while that broker is retired.

The public repository stores no GitLab credentials, project source, Supabase service-role key, or persistent worker token. Any replacement handoff must preserve those properties.

This repository should remain tiny. New routes must be explicit, allowlisted end to end, tied to a fixed artifact contract, and runnable on standard GitHub-hosted infrastructure unless a future capability is explicitly provisioned. Prefer bounded jobs, cancellation of superseded requests, short artifact retention, and no persistent runner state.

## Resource policy

Standard GitHub-hosted runners remain the intended hosted execution substrate. A workflow is usable only when its private-source handoff is also live and admitted. Retired broker-dependent workflows must not be cited as successful verification. The runner repository intentionally uses no persistent cache because the staged candidate is authoritative and the current products are small enough that cache invalidation would add more risk than value.

The retired `api` repository identity is not a source root. Compatibility environment variable names may still contain `API` while migration finishes, but all central source bytes and worker code come from the `helix` repository.
