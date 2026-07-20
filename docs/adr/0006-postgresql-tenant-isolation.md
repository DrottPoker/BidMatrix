# ADR 0006: Enforce tenant isolation in PostgreSQL and application boundaries

- Status: Accepted
- Date: 2026-07-20

## Context

BidMatrix is organization-aware from the first release. A missing filter or overprivileged database role could expose data between customer organizations.

## Decision

Store authoritative relational data in PostgreSQL. Tenant-owned rows carry `organization_id`, appropriate indexes begin with that key, application authorization checks tenant context, and PostgreSQL row-level security is used where applicable. The application role is not a database owner.

## Consequences

- Tenant context is required at application and database boundaries.
- Migrations must define explicit constraints, indexes, and policies.
- Integration and security tests must attempt cross-tenant access.
- Normal application roles cannot update immutable audit records.
