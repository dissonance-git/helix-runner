# helix-runner

Public ephemeral execution host for the Helix ecosystem.

This repository is **not** a source mirror, project workspace, validation authority, or alternate publication line. Canonical project bytes remain in GitLab. The repository carries only small control code and opaque request identifiers that wake standard GitHub-hosted runners.

## Hosted runner status

This repository is the public control surface for disposable **GitHub-hosted** execution. It does not use a local runner, self-hosted runner, or GitLab runner.

For Omniphony Windows builds, the `windows-build.yml` workflow runs on GitHub-hosted `windows-2025`, reads only an exact canonical GitLab commit SHA from the public request, and fetches that private commit at runtime with a repository read-only GitLab credential stored in GitHub Actions secrets. Private source is never committed to this repository and is deleted from the hosted runner after the job.

```text
canonical private GitLab commit SHA
→ GitHub Actions read-only GitLab credential
→ GitHub-hosted windows-2025
→ ci/windows-product.ps1
→ attested short-lived build artifact
```

## Windows runner target

The intended Windows execution shape remains:

```text
exact admitted private source
→ GitHub-hosted windows-2025
→ project-owned Windows verifier
→ bounded artifact/evidence
```

For Omniphony, the verifier is `ci/windows-product.ps1` running on GitHub-hosted Windows. The old Supabase source-broker protocol is retired and is no longer used by `windows-build.yml`.

The public repository stores no GitLab credentials, project source, Supabase service-role key, or persistent worker token. Any replacement handoff must preserve those properties.

This repository should remain tiny. New routes must be explicit, allowlisted end to end, tied to a fixed artifact contract, and runnable on standard GitHub-hosted infrastructure unless a future capability is explicitly provisioned. Prefer bounded jobs, cancellation of superseded requests, short artifact retention, and no persistent runner state.

## Resource policy

Standard GitHub-hosted runners are the execution substrate. Omniphony's Windows route now uses direct private GitLab fetch on the hosted runner; no local or self-hosted machine is part of the compile path. The runner repository intentionally uses no persistent cache because the staged candidate is authoritative and the current products are small enough that cache invalidation would add more risk than value.

The retired `api` repository identity is not a source root. Compatibility environment variable names may still contain `API` while migration finishes, but all central source bytes and worker code come from the `helix` repository.
