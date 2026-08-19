# SaaS Stabilization S0

## Objective

S0 turns the implemented F2 concierge pilot into a trustworthy local product baseline before BidMatrix expands agent authority. It does not claim production readiness. It establishes a repeatable product gate, removes known dependency risk, records the SaaS-first sequence, and proves that the customer result is controlled by a human owner.

## Acceptance contract

S0 is complete only when all of the following are true:

1. The final pinned dependency graph installs in the supported Windows environment and the pinned Linux web image.
2. The full npm dependency audit has no reported vulnerability.
3. Web lint, type checking, tests, and production build pass.
4. The .NET Release build and all PostgreSQL integration tests pass.
5. Python Ruff, strict mypy, and pytest pass.
6. A clean, isolated Compose project builds and all long-running services become healthy.
7. The controlled F2 RFP completes upload, extraction, owner review, correction, rejection, publication, and customer delivery.
8. Customer results are empty before owner publication.
9. Every published result has an exact file, page, and quote citation.
10. Original extraction, correction history, optimistic concurrency, rejection filtering, audit events, and the audit chain are verified.
11. External actions remain disabled.
12. API, worker, and web logs contain no error-level entry during the running gate.
13. CI defines the same static, database, and clean-stack gates.
14. The current state and SaaS-first sequence are documented without a production or commercial claim.

## Agent freeze

During S0 and every later SaaS-first phase until a new owner-approved ADR:

- no agent schedule may be enabled;
- no agent may create or rank company goals or priorities;
- no agent may decide product scope, pricing, customer eligibility, or publication;
- no agent may appear in the customer workflow;
- no live model mode may be required for the product;
- no agent tool may gain an external side effect;
- no agent may modify policy, credentials, permissions, or its own operating boundary.

The four historical F0 agent workflows may remain as deterministic, manually triggered regression fixtures. Registering their workflow types does not authorize execution. The product workflow may continue to use the Python worker for deterministic Temporal orchestration and document intake because that worker does not make agent-governed product decisions.

## Verified S0 result

On 2026-08-11, an isolated `bidmatrix-s0` Compose project completed the automated F2 gate. It published 84 requirements, 7 key dates, 10 requested documents, and 1 weighted evaluation criterion from the controlled F2 test RFP. The gate retained one correction, hid one rejected requirement, rejected a stale update with HTTP 409, verified three mutation audit events, verified the audit chain, and confirmed disabled external actions.

All 29 .NET integration tests and all 12 Python tests passed. The .NET suite includes eight concurrent agent preparations and Tool Gateway writes. The web dependency graph installed in the supported Linux image, the full npm audit reported zero vulnerabilities, and the web quality commands passed. The final clean-stack gate recorded no error-level API, worker, or web log entry. These are local verification facts, not cloud-deployment evidence.

## SaaS-first continuation

The release sequence after S0 is:

1. S1 hosted concierge foundations: production identity, tenant onboarding, secure deployment, private storage, backups, restore drills, malware scanning, observability, and invited pilot access.
2. S2 self-service commercial core: customer administration, billing, usage enforcement, exports, notifications, retention controls, and support operations.
3. S3 operating proof: paid customers, repeat usage, incident readiness, recovery evidence, measured product quality, and sustainable unit economics.
4. A separately approved autonomy decision: only after S3 may the owner consider any agent steering, schedule, live model mode, or expanded tool authority.

Company profiles, evidence matching, OCR, and richer analysis are product expansions. They may be phased when they improve the customer SaaS, but they do not relax the agent freeze.

## Non-goals

S0 does not implement production authentication, cloud infrastructure, billing, customer self-service, legal terms, privacy operations, real malware scanning, OCR, semantic extraction, supplier matching, compliance scoring, or autonomous agents.
