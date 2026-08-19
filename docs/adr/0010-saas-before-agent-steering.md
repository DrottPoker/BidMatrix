# ADR 0010: Build the working SaaS before agent steering

- Status: Accepted
- Date: 2026-08-11

## Context

BidMatrix already contains a local F2 concierge product and an F0 internal agent control-plane foundation. The product is not yet a production SaaS: identity is Development-only, organizations are bootstrapped locally, infrastructure is not deployed, billing and self-service are absent, and operational recovery has not been proven.

Advancing agent authority before these product and operating foundations would optimize the internal company concept ahead of the customer business. It would also create additional paths for priorities, schedules, tools, and external effects before there is a production product to govern.

## Decision

BidMatrix will follow a SaaS-first release sequence.

S0 establishes the verified local F2 baseline. S1 builds hosted concierge foundations. S2 adds the self-service commercial core. S3 proves real operation and paid repeat use. No agent may steer company goals, product priorities, schedules, customer decisions, publication, or external actions before S3 is satisfied and the owner approves a new ADR.

The existing four F0 agent workflows remain deterministic, manually triggered regression demonstrations. Their schedule definitions stay disabled by default. Live agent mode is not part of the product path. The Python worker may continue deterministic Temporal orchestration for document processing because it does not have decision authority over the customer result.

External adapters remain disabled. No phase may implicitly broaden agent tool permissions, model mode, or execution triggers.

## Consequences

- Product engineering is prioritized over internal autonomy work.
- Production identity, tenant provisioning, deployment security, backups, observability, billing, and customer administration become explicit release gates.
- Existing agent code remains testable without becoming an operational dependency.
- Any future agent schedule or steering capability requires evidence that the working SaaS gate is complete, plus a new owner-approved ADR with permissions, metrics, stop conditions, and rollback behavior.
- The older long-term roadmap remains useful only where it does not conflict with this sequencing decision.
