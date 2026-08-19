# BidMatrix

BidMatrix is a self-service concierge SaaS for RFP analysis. It uses Next.js and Tailwind CSS, ASP.NET Core, PostgreSQL, MinIO, Temporal, and a Python workflow worker.

The product extracts digital English PDFs and publishes four source-linked result types after owner quality review: requirements, key dates, requested documents, and weighted evaluation criteria. Corrections retain the original extraction, rejected items remain hidden, and every visible item has an exact file, page, and quote citation.

Hosted Concierge S1 is active on top of the verified S0 baseline. A person can create, verify, sign in to, and recover an account with email and password. The first successful managed login creates a private BidMatrix workspace and an `owner` membership. It never grants platform roles or agent authority. Google and GitHub are deferred. Existing agent workflows remain deterministic, manually triggered regression demonstrations only.

This is not yet a production SaaS. Self-hosted Better Auth, verified email, password recovery, rate limits, session revocation, and BidMatrix OIDC have been verified locally against the isolated Neon PostgreSQL development branch. A production SMTP provider, hosted HTTPS and secrets, hosted callback evidence, application hosting, production database promotion, private storage operations, malware scanning, backups, restore evidence, observability, billing, and commercial validation remain open.

## Quick start

Prerequisite: Docker Desktop with Compose v2.

```powershell
Copy-Item .env.example .env
docker compose up --detach --build --wait
.\scripts\verify-f2.ps1
```

Open:

- account registration: <http://localhost:3000/register>
- sign-in: <http://localhost:3000/login>
- customer app: <http://localhost:3000/app>
- Owner Console: <http://localhost:3000/owner>
- account security: <http://localhost:3000/app/account>
- owner recovery operations: <http://localhost:3000/owner/access>
- account recovery: <http://localhost:3000/recover>
- managed password recovery: <http://localhost:3000/forgot-password>
- Development email inbox: <http://localhost:8025>
- API readiness: <http://localhost:8080/health/ready>
- worker readiness: <http://localhost:8081/health/ready>
- MinIO console: <http://localhost:9001>
- Temporal UI: <http://localhost:8233>

The default Compose environment keeps native Development authentication enabled. End-to-end self-service registration requires the managed Better Auth OIDC configuration in `docs/operations/managed-oidc.md`. Development owner credentials come from `.env`; never reuse the checked-in placeholders outside local Development.

Users manage server sessions and password rotation in `/app/account`. A recently authenticated Development owner can create a short-lived recovery link in `/owner/access` after verifying the account owner outside BidMatrix. See `docs/operations/identity-bootstrap-and-recovery.md` before operating bootstrap or recovery outside local development.

Stop while preserving data:

```powershell
docker compose down
```

`docker compose down --volumes` permanently deletes the selected Compose project's local database, object-storage, workflow, data-protection, and engineering-worktree volumes.

## Local dependency setup

Use Node.js 24.18.0, .NET SDK 10.0.302, Python 3.14, and uv 0.11.12 or later.

```powershell
npm --prefix apps/web ci
dotnet restore BidMatrix.slnx
uv sync --project src/agents --locked
```

All direct dependencies and container images are pinned. npm and uv lockfiles capture transitive application dependencies.

## Quality gate

```powershell
npm --prefix apps/web audit --audit-level=moderate
npm run lint
npm run typecheck
npm run test
npm run build
dotnet format BidMatrix.slnx --verify-no-changes --no-restore
dotnet build BidMatrix.slnx --configuration Release --no-restore
dotnet list BidMatrix.slnx package --vulnerable --include-transitive
$env:BIDMATRIX_TEST_POSTGRES_PORT='55432'
dotnet test BidMatrix.slnx --configuration Release --no-build
uv run --directory src/agents ruff check . --no-cache
uv run --directory src/agents mypy
uv run --directory src/agents pytest
docker compose --env-file .env.example config --quiet
```

The PostgreSQL integration tests require a local initialized PostgreSQL instance with the roles from `infra/postgres/init`. Starting the Compose `postgres` service provides that baseline. The integration suite verifies that self-service registration creates one private organization, an owner membership, no platform role, no agent run, and no reusable native credential.

## Demonstrable F2 flow

- Sign in as the development owner.
- Create an analysis and upload the controlled F2 test RFP.
- Confirm that customer results remain hidden during processing and owner review.
- Review exact citations, correct one requirement, reject another, and publish.
- Confirm that the customer sees only the reviewed, non-rejected report.
- Verify optimistic concurrency, correction history, audit events, the audit chain, and disabled external actions.

The historical agent demonstrations are deterministic control-plane regressions. They are not part of the customer product and do not authorize autonomous operation.

## Repository map

- `apps/web`: Next.js App Router, TypeScript, Tailwind CSS, customer app, registration, and Owner Console.
- `src/backend`: ASP.NET Core API, domain services, policy, Tool Gateway, audit, and SQL migration host.
- `src/agents`: Temporal workflow worker plus frozen deterministic agent demonstrations.
- `infra`: pinned application containers and PostgreSQL initialization.
- `tests/fixtures`: deterministic PDF, agent, and engineering repositories.
- `scripts/verify-f2.ps1`: essential F2 running-stack end-to-end verification.
- `scripts/verify-better-auth-oidc.ps1`: local Better Auth discovery, ES256, PKCE, token, claim, owner linking, and BidMatrix-role verification against Neon development.
- `docs/operations/managed-oidc.md`: Better Auth configuration, social providers, callback registration, recovery, rollback, and hosted evidence checklist.
- `docs/operations/neon-development.md`: canonical Neon project, branch discipline, TLS, role separation, migration commands, and production boundaries.
- `docs/architecture`: system topology and trust boundaries.
- `docs/operations`: setup, migration, worker, incident, and release runbooks.
- `docs/product`: customer capability contracts and the SaaS-first sequence.
- `BIDMATRIX_AI_COMPANY_MASTER_PLAN.md`: authoritative specification and long-term roadmap.

Start with `docs/implementation-status.md`, `docs/product/s1-hosted-concierge.md`, and `docs/product/f2-capability-boundaries.md` before changing customer-visible behavior.
