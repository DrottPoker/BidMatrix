# ADR 0008: Apply core SQL migrations during Development startup

- Status: Accepted
- Date: 2026-07-20

## Context

Phase 2 needs an authoritative PostgreSQL schema before authentication and API foundations are introduced. The current foundation does not include a migration tool or operational migration runner yet, and the local-first stack must still start from Docker Compose without extra manual steps.

## Decision

Store Phase 2 database changes as ordered SQL migration resources in `BidMatrix.Database`. In Development, the ASP.NET Core API applies pending migrations at startup using the configured PostgreSQL connection. Non-Development environments skip automatic migration execution until a deployment migration process is added.

## Consequences

- Empty local PostgreSQL databases are initialized automatically for Phase 2 development.
- SQL remains explicit and reviewable for RLS, constraints, indexes, and audit protections.
- Production deployment must add a controlled migration step before enabling non-Development database startup.
- Rollback is handled by forward-fix migrations, because the schema includes append-only audit and security-sensitive policy objects.
