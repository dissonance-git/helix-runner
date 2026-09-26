# Agent law

This repository is a disposable build-control surface, not a project source repository.

- `main` is the only writable branch.
- Do not copy Helix project source into this repository.
- Do not add persistent worker state, GitLab credentials, personal tokens, deploy keys, or machine-local secrets.
- Build requests carry only opaque staged-build identifiers.
- Windows workflows may execute only explicit allowlisted project/route pairs.
- Source bytes must come from the authenticated Supabase build broker and must be checksum-verified before execution.
- GitHub OIDC must be scoped to this repository, `main`, and the exact workflow file.
- Do not accept arbitrary commands, arbitrary artifact paths, or arbitrary repository URLs as workflow inputs.
- Build artifacts are outputs, not repository state.
- Keep the repository surface minimal; if a file does not support the build boundary directly, it does not belong here.

- Ordinary execution must use standard GitHub-hosted runners. Do not require provider-installed or user-managed CI workers.
- Prefer the cheapest sufficient hosted image: use Windows only for Windows-specific work and host-agnostic images when they satisfy the route.
- Every workflow must have bounded timeouts and concurrency. Superseded request-driven jobs should cancel instead of consuming runner time.
- Keep artifact retention short and upload only the declared result. Do not use Actions artifacts as a long-term data store.
- A route that needs software unavailable on standard hosted runners must degrade to an honest hosted verifier or remain explicitly unavailable; never silently turn machine-local state into a prerequisite.
- All third-party actions must be pinned to an immutable full commit SHA.
