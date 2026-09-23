# helix-runner

Public ephemeral Windows build host for the Helix ecosystem.

This repository is **not** a source mirror, project workspace, validation authority, or alternate publication line. Canonical project bytes remain in their GitLab repositories. This repo exists only to let GitHub-hosted Windows VMs compile exact candidate bundles staged by the Helix workspace.

## Flow

```text
Helix hosted workspace
→ exact candidate worktree
→ private Supabase source bundle
→ request.json carries only an opaque build id
→ GitHub-hosted Windows VM
→ GitHub OIDC authenticates to Supabase
→ short-lived signed source download
→ allowlisted build route
→ GitHub Actions artifact
→ source bundle deleted
→ VM disappears
```

The public repository stores no GitLab credentials and receives no persistent worker token. GitHub OIDC is accepted only for this repository's `main` workflow.

## Current route

```text
omniphony / windows-product
→ ci/windows-product.ps1
→ dist/OmniphonySetup.exe
```

A build is requested by replacing `request.json` with the staged build UUID. The workflow can also be started manually with the same UUID.

This repository should remain tiny. New build routes must be explicit, allowlisted, and tied to a fixed artifact contract.
