# Implementation status

Last updated: 2026-07-20

## Release state

Foundation Release F0 is implemented and verified locally. Phases 0 through 9 satisfy their bounded acceptance gates. This is a cloud-portable development foundation, not authorization for production deployment or post-F0 product capabilities.

The initial uncommitted Vite and bid-scoring prototype was assessed and adapted into the required Next.js and Tailwind stack. Vite, JavaScript-only conversion, and fake bid scoring are not part of the active application.

## Phase status

| Phase | Status | Verified result |
| --- | --- | --- |
| 0. Repository assessment and decisions | Complete | Repository assessment, runtime pins, formatting, status record, and eight accepted ADRs. |
| 1. Monorepo and local infrastructure | Complete | Next.js, ASP.NET Core, Python, PostgreSQL, MinIO, Temporal, and Docker Compose all reach healthy state without an OpenAI key. |
| 2. Database and core domain | Complete | Seven forward-only migrations apply to empty PostgreSQL; application and audit roles are separate; 17 tenant-sensitive tables use RLS; audit mutation is rejected. |
| 3. Authentication and API foundations | Complete | Owner bootstrap, login/logout/me, organization context, CSRF, owner/customer/internal authorization, OpenAPI, problem details, and correlation IDs are tested. |
| 4. Upload and minimal analysis pipeline | Complete | Valid PDF upload is hashed and quarantined; Development scan bypass is forbidden outside Development; Temporal creates one manual-review task; requirements remain explicitly unimplemented. |
| 5. Tool Gateway, policy, and approvals | Complete | Role permission, deterministic policy, idempotency, canonical payload hashes, revision, race, expiry, disabled adapters, and audit tests pass. |
| 6. Four agents and workflows | Complete | Four versioned prompts and strict outputs have deterministic demos; support injection does not gain tools; invalid output fails rather than completes. |
| 7. Owner Console and customer shell | Complete | Required owner and customer pages use live APIs; exact approval payload is visible; customer navigation contains no OS pages; draft-only state is prominent. |
| 8. Engineering sandbox foundation | Complete | Generated Git worktree, containment, traversal, junction, secret, command, timeout, output, base-integrity, diff, and remote-action gates pass. |
| 9. Quality, documentation, and release gate | Complete | Architecture, threat model, runbooks, evaluations, CI, dependency scanning, essential end-to-end script, and current verification record are present. |

## Capability truth table

| Capability | F0 state | Honest outward claim |
| --- | --- | --- |
| Customer and owner authentication | Implemented | Organization workspace and owner console are available. |
| PDF intake | Implemented | English PDF files are validated, hashed, and stored in quarantine. |
| RFP requirement extraction | Not implemented | Manual review is required; no requirements are generated. |
| Four internal agents | Implemented in deterministic mode | Offline, structured, draft-only demonstrations are available. |
| Live model mode | Opt-in adapter only | Not part of the verified default gate. |
| Tool Gateway and policy | Implemented | All agent tools cross the deterministic boundary. |
| Owner approvals | Implemented | Decisions are payload-bound, versioned, expiring, and audited. |
| External communication and spending | Disabled | Approval does not execute an external effect in F0. |
| Engineering code preparation | Fixture-only foundation | A documentation diff can be prepared in an isolated worktree. |
| Remote Git and deployment | Disabled | No push, PR, merge, or deployment occurs. |

## Current verification record

- Next.js/Tailwind: lint, TypeScript generation and check, three Vitest tests, and production build pass.
- .NET: build passes with zero warnings; 25 integration and policy tests pass against disposable PostgreSQL.
- Python: Ruff and strict mypy pass; 12 pytest cases pass.
- Contract surface: Development OpenAPI includes required customer, owner, demo, and internal routes.
- Dependencies: npm production audit, NuGet transitive vulnerability scan, and pip-audit report no known vulnerabilities. The local Python package itself is skipped because it is not published to PyPI.
- Compose: configuration validates; all six long-running services are healthy from pinned images.
- Essential end to end: analysis reaches `requires_review`; four agent runs complete; engineering sandbox is recorded; audit chain is valid; draft-only and disabled-external controls remain active.
- Engineering runtime: artifact, worktree, write, allowlisted command, and diff calls complete; remote Git call count is zero; base fixture remains clean.

## Explicit post-F0 boundary

No cloud deployment was performed. Real malware scanning, OCR, extraction, scoring, live-model qualification, external actions, billing, dedicated production sandbox infrastructure, and production operations require a new owner-approved phase.
