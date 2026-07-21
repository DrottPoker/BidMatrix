# Foundation F0 threat model

## Scope and assets

Protected assets are customer PDFs, tenant metadata, sessions, owner decisions, tool permissions, system controls, agent artifacts, audit integrity, internal service credentials, and engineering repository content.

F0 assumes a single local development deployment. Cloud production hardening and production deployment remain outside F0, but the application boundaries are designed to carry forward.

## Threats and controls

| Threat | Primary F0 controls | Verification |
| --- | --- | --- |
| Cross-tenant data access | Organization claim, transaction-scoped context, forced PostgreSQL RLS | Cross-tenant integration tests |
| Owner-route escalation | Cookie policies and explicit platform-owner authorization | Customer and internal-service denial tests |
| CSRF on mutations | Strict SameSite cookies and antiforgery header | Login, logout, owner mutation tests |
| Session credential theft | HttpOnly cookie, secure policy outside Development, no secrets in responses | Authentication tests and configuration review |
| Malicious PDF | PDF signature and end-marker validation, size bound, SHA-256, quarantine, scanner adapter | Upload and production-bypass tests |
| Prompt injection | Customer content is untrusted, versioned role prompts, strict outputs, fixed tool permissions | Support injection fixture and permission tests |
| Agent side-effect bypass | Python has no database access; every action uses internal API and Tool Gateway | Tool-call persistence and internal authorization tests |
| Payload changed after approval | Canonical JSON and SHA-256 bound to optimistic approval version | Mutation and race tests |
| External action falsely reported | Disabled adapters return `disabled`, never success | Approval integration tests |
| Agent self-enables policy | No agent tool for system controls; owner-only API | Authorization and policy tests |
| Audit tampering | Separate audit role, security-definer append function, mutation trigger, chained hashes | Database mutation denial and chain verification |
| Repository traversal | Canonical relative paths, containment check, blocked segments, reparse-point rejection | Traversal and junction tests |
| Secret-file access | `.env`, credentials, secret patterns, key formats, and Git metadata blocked | Engineering sandbox tests |
| Arbitrary process execution | Exact executable plus argument-array allowlist, no shell, timeout, output bound | Non-allowlisted command test |
| Engineering network exfiltration | No allowlisted command contains a network target; child-process proxy variables fail closed | Command-policy and engineering tests |
| Remote Git action | Push, PR, merge, and deployment adapters remain approval-bound and disabled | Policy and runtime demonstration |
| Sensitive model telemetry | Model and tool data logging disabled by default; live mode explicit | Settings validation tests |

## Residual risks

- The Development scanner is not malware protection. Non-Development refuses this bypass, but a production scanner must be selected later.
- Local Compose secrets are placeholders stored in `.env`; cloud work must move them to a secret manager and rotate all credentials.
- The F0 engineering runner shares the API runtime container and its base network namespace. Its exact command signatures contain no network target and proxy variables fail closed, but a later production runner must use an ephemeral network-disabled workload with per-command CPU, memory, filesystem, and identity isolation.
- Audit-link verification detects chain continuity. Independent signed anchoring is a later hardening step.
- Live OpenAI mode is not part of the default release gate and requires an explicit key and four model selections.

Any change that activates external communication, spending, remote Git, deployment, permanent deletion, or security-incident communication is outside F0 and requires a new documented security decision.
