# helix-runner

Public Windows execution host for the Helix ecosystem.

This repository is **not** a source mirror, project workspace, validation authority, or alternate publication line. Canonical project bytes remain in their project repositories. This repo exists only to execute exact candidate bundles staged by the Helix workspace on the Windows host appropriate to each route.

## Flow

```text
Helix hosted workspace
→ exact candidate worktree
→ private Supabase source bundle
→ route-specific request file carries only an opaque build id
→ GitHub Actions chooses the required Windows host
→ GitHub OIDC authenticates to Supabase
→ short-lived signed source download
→ allowlisted build / verification route
→ GitHub Actions artifact
→ source bundle deleted
```

The public repository stores no GitLab credentials and receives no persistent worker token. GitHub OIDC is accepted only for this repository's allowlisted `main` workflows.

## Routes

```text
omniphony / windows-product
→ GitHub-hosted windows-latest
→ ci/windows-product.ps1
→ dist/OmniphonySetup.exe

eft2 / verification-reference
→ GitHub-hosted windows-latest
→ tools/run-verification.ps1 reference
→ dist/EFT2Verification.zip
```

Both active routes use disposable standard GitHub-hosted runners. EFT2's hosted route is intentionally limited to the strongest engine-independent reference verification available on a clean runner. Exact s&box editor/runtime verification remains a distinct capability and is not assumed to exist on the user's machine or on a permanent runner.

A build is requested by replacing the route's request file with the staged build UUID:

- `request.json` for `omniphony/windows-product`
- `eft2-request.json` for `eft2/verification-training`

Both workflows can also be started manually with the same staged UUID.

This repository should remain tiny. New routes must be explicit, allowlisted end to end, tied to a fixed artifact contract, and runnable on standard GitHub-hosted infrastructure unless a future capability is explicitly provisioned. Prefer bounded jobs, cancellation of superseded requests, short artifact retention, and no persistent runner state.
