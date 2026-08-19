# ADR 0015: Use Neon for managed PostgreSQL

> Amendment: ADR 0018 removes invitation authority from the active schema and replaces it with self-service private workspace registration. Invitation references below describe the state when this decision was accepted.

- Status: Accepted
- Date: 2026-08-12

## Context

BidMatrix needs managed PostgreSQL before the hosted concierge can become an operable SaaS. PostgreSQL remains the authority for users, tenant membership, invitations, sessions, analysis state, audit records, approvals, and the frozen agent control plane. The hosted database must preserve the existing role separation, row-level security, controlled migrations, TLS, and branch-isolated verification.

The owner selected Neon and authorized use of the existing Neon account. Existing Neon projects belong to other products and cannot be reused without mixing ownership and data boundaries.

## Decision

BidMatrix uses a dedicated Neon project named `BidMatrix` in AWS Europe Central 1, Frankfurt, with PostgreSQL 18.

The default `production` branch remains untouched until a production release gate is approved. Managed database work runs on the child branch named `development`. That branch contains a dedicated `bidmatrix` database and four distinct database identities:

- the Neon database owner is used only for controlled migrations;
- `bidmatrix_app` is the normal application role;
- `bidmatrix_audit` is the narrow audit append role; and
- `bidmatrix_auth` is the isolated Better Auth role for the `better_auth` schema.

The application, audit, and auth roles cannot create databases or roles, do not inherit other roles, are not superusers, and cannot bypass row-level security. The auth role cannot read BidMatrix authority tables. Development credentials are stored in .NET User Secrets outside the repository. Deployment credentials must later move to the selected hosted secret manager and must never be placed in repository files, images, logs, or browser storage.

Runtime connections use Neon's pooled endpoint. Controlled migrations use the direct endpoint because Neon runs the pooler in transaction mode and recommends direct connections for schema migrations. Every Neon connection uses `SslMode=VerifyFull` and `ChannelBinding=Require`. See [Neon connection security](https://neon.com/docs/connect/connect-securely) and [Neon connection pooling](https://neon.com/docs/connect/connection-pooling).

The API exposes two bounded operator commands:

- `--migrate-database` applies only the embedded, checksummed migration chain;
- `--verify-database-access` proves that the application and audit credentials connect and retain the required least-privilege role attributes.

Neon Auth is not enabled by this decision. ADR 0016 self-hosts Better Auth against the same Neon database while preserving an isolated schema and role, which keeps the authentication runtime and its pinned dependency version under the BidMatrix release gate. ADR 0017 defines the current email/password, Google, and GitHub methods. Reconsidering Neon Auth requires a separate compatibility and migration decision.

## Consequences

- BidMatrix now has an isolated managed PostgreSQL development target in the owner's Neon account.
- All fifteen current migrations can be applied and verified without modifying the production branch.
- Local PostgreSQL remains the default Compose dependency for deterministic full-stack verification.
- A hosted application deployment, production branch promotion, IP or private-network policy, secret manager, backups, restore drill, monitoring, and capacity evidence remain separate S1.2 through S1.4 work.
- Better Auth is the selected self-hosted identity provider under ADR 0016, with simplified methods under ADR 0017. Hosted identity still requires recovery, verified-email delivery, configured social-provider callbacks, deployment, logout, and invited-user evidence. Neon Auth may be reconsidered only through a new compatibility and migration decision.
- This infrastructure decision gives no agent new schedule, model mode, tool permission, goal-setting authority, or external effect.
