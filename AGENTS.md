# Agent law

This repository is a disposable build-control surface, not a project source repository.

- `main` is the only publication branch. Automated dependency-update branches may exist only as disposable review proposals.
- Do not copy Helix project source into this repository.
- Do not add persistent worker state, GitLab credentials, personal tokens, deploy keys, or machine-local secrets.
- Hosted requests may carry only opaque admitted job/build identifiers.
- Private project source must never be copied into this public repository.
- The former `helix-build` and `helix-workspace` Supabase brokers are retired and must not be revived.
- Omniphony Windows builds use an exact canonical GitLab commit staged as a bounded private compile snapshot, fetched on a standard GitHub-hosted `windows-2025` runner through GitHub OIDC. Every file is hash-verified and the snapshot is deleted after the job. No GitLab credential or project source is stored in this public repository.
- Windows workflows may execute only explicit allowlisted project/route pairs.
- GitHub OIDC, when used, must be scoped to this repository, `main`, and the exact workflow file.
- Do not accept arbitrary commands, arbitrary artifact paths, or arbitrary repository URLs as workflow inputs.
- Build artifacts are outputs, not repository state.
- Keep the repository surface minimal; if a file does not support the build boundary directly, it does not belong here.

- Ordinary execution must use standard GitHub-hosted runners. There is no local runner, self-hosted runner, or user-managed CI worker in the Omniphony compile path.
- GitHub-hosted Ubuntu and Windows runners remain the intended execution carriers, but a checked-in workflow is not considered available unless its current private-source handoff is live.
- Prefer the cheapest sufficient hosted image: use Windows only for Windows-specific work and host-agnostic images when they satisfy the route.
- Every workflow must have bounded timeouts and concurrency. Superseded request-driven jobs should cancel instead of consuming runner time.
- Keep artifact retention short and upload only the declared result. Do not use Actions artifacts as a long-term data store.
- A route that needs software unavailable on standard hosted runners must degrade to an honest hosted verifier or remain explicitly unavailable; never silently turn machine-local state into a prerequisite.
- All third-party actions must be pinned to an immutable full commit SHA.
