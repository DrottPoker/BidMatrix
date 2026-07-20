# ADR 0001: Use the hybrid application stack

- Status: Accepted
- Date: 2026-07-20

## Context

BidMatrix combines a customer SaaS product, authoritative business and policy logic, durable AI workflows, and an internal owner control plane. These workloads have different runtime strengths and security boundaries.

## Decision

Use ASP.NET Core 10 for the authoritative API and domain boundary, Python 3.14 for agent orchestration and Temporal workers, and Next.js App Router with TypeScript and Tailwind CSS for the web application. PostgreSQL is the relational source of truth, S3-compatible storage holds files, Temporal coordinates durable workflows, and Docker Compose provides the local environment.

## Consequences

- Cross-runtime contracts must be explicit and validated.
- The API remains authoritative for product and operating-system data.
- The Python worker does not connect directly to the application database.
- The web application uses server-side boundaries for sensitive session operations.
- Runtime and dependency versions must be pinned and tested together.
