# helix-runner

Public ephemeral execution host for the Helix ecosystem.

This repository is **not** a source mirror, project workspace, validation authority, or alternate publication line. Canonical project bytes remain in GitLab. The repository carries only small control code and opaque request identifiers that wake standard GitHub-hosted runners.

## Hosted runner status

This repository is the public control surface for disposable **GitHub-hosted** execution. It does not use a local runner, self-hosted runner, or GitLab runner.

For Omniphony Windows builds, the `windows-build.yml` workflow runs on GitHub-hosted `windows-2025`, reads only an exact canonical GitLab commit SHA from the public request, authenticates to the private compile-source relay with GitHub OIDC, reconstructs only the bounded source snapshot required for compilation, verifies every file hash, and deletes the staged snapshot after the job. No GitLab credential is stored in this public repository or required as a GitHub Actions secret.

```text
canonical private GitLab commit SHA
→ bounded private compile snapshot
→ GitHub OIDC
→ GitHub-hosted windows-2025
→ managed + native Windows compile
→ short-lived compile receipt
```

## Windows runner target

The intended Windows execution shape remains:

```text
exact admitted private source
→ GitHub-hosted windows-2025
→ project-owned Windows verifier
→ bounded artifact/evidence
```

For Omniphony, `windows-build.yml` is the default hosted compile gate. It compiles the managed Setup project and the native Windows APO/helper CMake tree from the exact bounded snapshot. Full product packaging remains project-owned; the old `helix-build` / `helix-workspace` broker protocol is retired and is not used.

The public repository stores no GitLab credentials, project source, Supabase service-role key, or persistent worker token. Any replacement handoff must preserve those properties.

This repository should remain tiny. New routes must be explicit, allowlisted end to end, tied to a fixed artifact contract, and runnable on standard GitHub-hosted infrastructure unless a future capability is explicitly provisioned. Prefer bounded jobs, cancellation of superseded requests, short artifact retention, and no persistent runner state.

## Resource policy

Standard GitHub-hosted runners are the execution substrate. Omniphony's Windows route uses the GitHub-OIDC private compile-source relay; no local or self-hosted machine is part of the compile path. The runner repository intentionally uses no persistent cache because the staged candidate is authoritative and the current products are small enough that cache invalidation would add more risk than value.

The retired `api` repository identity is not a source root. Compatibility environment variable names may still contain `API` while migration finishes, but all central source bytes and worker code come from the `helix` repository.
