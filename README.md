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

eft2 / verification-training
→ self-hosted Windows x64 machine with s&box Source Authoring Layer
→ tools/run-verification.ps1 training
→ dist/EFT2Verification.zip
```

Omniphony uses disposable GitHub-hosted Windows because its build is self-contained. EFT2 deliberately does **not** use that host: exact-runtime verification requires the real s&box editor/runtime (`game\hammer.exe`). Its workflow therefore targets `[self-hosted, Windows, X64]` and fails closed if the machine-local s&box root is unavailable.

A build is requested by replacing the route's request file with the staged build UUID:

- `request.json` for `omniphony/windows-product`
- `eft2-request.json` for `eft2/verification-training`

Both workflows can also be started manually with the same staged UUID.

This repository should remain tiny. New routes must be explicit, allowlisted end to end, tied to a fixed artifact contract, and assigned only to hosts that actually possess the required runtime.
