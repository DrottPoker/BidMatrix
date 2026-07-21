# BidMatrix

BidMatrix Foundation Release F0 is a controlled, local-first foundation for sourced and reviewable RFP intake plus an internal AI operating system. It uses Next.js and Tailwind CSS, ASP.NET Core, PostgreSQL, MinIO, Temporal, and a Python agent worker.

F0 is intentionally draft-only. It does not perform OCR, requirement extraction, compliance matching, bid scoring, outbound communication, remote Git actions, billing, or production deployment.

## Quick start

Prerequisite: Docker Desktop with Compose v2.

```powershell
Copy-Item .env.example .env
docker compose up --detach --build --wait
.\scripts\verify-f0.ps1
```

Open:

- customer app: <http://localhost:3000/app>
- owner sign-in: <http://localhost:3000/login>
- Owner Console: <http://localhost:3000/owner>
- API readiness: <http://localhost:8080/health/ready>
- worker readiness: <http://localhost:8081/health/ready>
- MinIO console: <http://localhost:9001>
- Temporal UI: <http://localhost:8233>

Development owner credentials come from `.env`. The checked-in placeholders are `owner@example.invalid` and `change-me-local-owner-password`. Never reuse these values outside local Development.

Stop while preserving data:

```powershell
docker compose down
```

`docker compose down --volumes` permanently deletes the local BidMatrix database, object-storage, workflow, data-protection, and engineering-worktree volumes.

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
npm run lint
npm run typecheck
npm run test
npm run build
dotnet build BidMatrix.slnx --no-restore
$env:BIDMATRIX_TEST_POSTGRES_PORT='55432'
dotnet test BidMatrix.slnx --no-build
uv run --directory src/agents ruff check .
uv run --directory src/agents mypy
uv run --directory src/agents pytest
docker compose --env-file .env.example config --quiet
```

The PostgreSQL integration tests require a local initialized PostgreSQL instance with the roles from `infra/postgres/init`. Starting the Compose `postgres` service provides that baseline.

## Demonstrable F0 flows

- Sign in as the owner and inspect live tasks, approvals, agents, runs, audit, goals, analyses, and system controls.
- Create an analysis, upload the synthetic PDF fixture into tenant-scoped quarantine, and submit it for manual review.
- Run deterministic offline demonstrations for Executive, Support, Product Analyst, and Engineering agents.
- Review every agent tool call at the mandatory Tool Gateway boundary.
- Create, reject, approve, expire, cancel, or revise payload-bound approvals while external adapters remain disabled.
- Run the Engineering Agent fixture in a server-generated Git worktree, execute only `git diff --check`, and store a reviewable diff artifact without any remote action.

## Repository map

- `apps/web`: Next.js App Router, TypeScript, Tailwind CSS, customer app, and Owner Console.
- `src/backend`: ASP.NET Core API, domain services, policy, Tool Gateway, audit, and SQL migration host.
- `src/agents`: Python structured agents and Temporal worker.
- `infra`: pinned application containers and PostgreSQL initialization.
- `tests/fixtures`: deterministic PDF, agent, and engineering repositories.
- `scripts/verify-f0.ps1`: essential running-stack end-to-end verification.
- `docs/architecture`: system topology and trust boundaries.
- `docs/operations`: setup, migration, worker, incident, and release runbooks.
- `docs/security`: F0 threat model and engineering-sandbox controls.
- `docs/evaluations`: deterministic agent evaluation contract.
- `BIDMATRIX_AI_COMPANY_MASTER_PLAN.md`: authoritative specification.

Start with `docs/implementation-status.md` for the verified release state and `docs/product/f0-capability-boundaries.md` before changing customer-visible behavior.
