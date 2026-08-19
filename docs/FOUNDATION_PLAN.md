# Foundation plan notice

The earlier dashboard-only plan was superseded by `BIDMATRIX_AI_COMPANY_MASTER_PLAN.md`.

Foundation Release F0 remains the control-plane baseline. Extraction Prototype F1 and Concierge Pilot F2 are implemented on top of it. SaaS Stabilization S0 is the verified local baseline. Hosted Concierge S1 is active and is defined in `docs/product/s1-hosted-concierge.md`.

S1.0 self-service registration and the local S1.1 email identity, session, and recovery controls are implemented and verified. ADR 0018 removes invitation-only onboarding: a new Better Auth identity creates a private workspace and fixed `owner` membership without platform or agent authority. ADR 0019 adds verified email, password reset, rate limits, and transactional cross-session revocation. ADR 0015 selects Neon for managed PostgreSQL, and the isolated development branch has the complete checksummed schema plus least-privilege runtime roles. A production SMTP provider, hosted HTTPS and secret management, deployment, emergency access, and real-customer evidence remain open. Google and GitHub are deferred. Existing agent demonstrations remain deterministic, manual, internal regressions. Do not add agent schedules, agent-created priorities, live model operation, customer-facing agents, or expanded tool authority before the working SaaS gate in ADR 0010 is satisfied and the owner approves a new architecture decision.

Continue from `docs/implementation-status.md` and follow `docs/product/f2-capability-boundaries.md`. OCR, semantic or model-assisted extraction, live external effects, company matching, billing, and production deployment each require explicit phase scope and verification.
