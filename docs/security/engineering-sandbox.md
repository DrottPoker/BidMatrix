# Engineering sandbox

## F0 contract

The Engineering Agent runs only for an owner-created task whose type is `engineering`. Repository tools are present only on the Engineering Agent version and are evaluated by deterministic policy. `allAgentsEnabled` and `engineeringWritesEnabled` act as kill switches.

The configured base is a small, committed fixture repository. The service creates a bare mirror and detached Git worktree under a server-generated organization and task path. The agent cannot select an absolute workspace path.

## Path controls

- Reject absolute paths, empty paths, `.` and `..` segments.
- Canonicalize every path and require it to remain under the generated worktree.
- Reject symlinks, junctions, and other reparse points in the resolved path.
- Block Git metadata, `.env*`, SSH and cloud credential directories, credential or secret names, and PEM, PFX, or key files.
- Bound individual text files, total searched files, match count, and returned line length.
- Keep the configured base repository unchanged. Only the generated worktree is writable.

## Command controls

Commands are represented as an executable and argument array. `UseShellExecute` is false and arguments are passed through `ProcessStartInfo.ArgumentList`. Exact signatures are allowlisted. F0 demonstrates only:

```text
git diff --check
```

The broader initial allowlist contains pinned build, test, lint, status, and diff signatures from the master plan. Non-allowlisted arguments, including `git push`, fail before process start. Commands have a timeout, bounded combined output, disabled terminal prompts, offline npm mode, and fail-closed proxy variables.

No allowlisted F0 command contains a remote URL or other network target. Child processes receive fail-closed HTTP, HTTPS, and all-proxy values, disabled Git prompting, and offline npm mode. The local Compose network remains host-accessible for the required web and API ports. A dedicated network-disabled runner is required before production.

## Artifact and retention behavior

`repo.createDiffArtifact` obtains `git diff --no-ext-diff`, hashes the UTF-8 diff with SHA-256, and stores the exact content as a tenant-scoped draft artifact. Sandbox metadata records base and head revisions plus a seven-day review retention timestamp. Automatic deletion is not active in F0.

Remote Git tools remain registered as disabled adapters. Approval never turns a disabled adapter into a successful push, pull request, merge, or deployment.

## Required follow-up before production

Move command execution into a dedicated ephemeral runner with its own read-only base mount, writable scratch volume, seccomp or equivalent policy, non-root identity, per-command CPU and memory limits, no container socket, no cloud metadata route, and verifiable teardown.
