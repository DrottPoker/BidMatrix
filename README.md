# BidMatrix

BidMatrix is a controlled foundation for sourced, reviewable RFP decision support. The active target is Foundation Release F0, not the full analysis product.

## Quick start

Prerequisites: Docker Desktop with Compose v2.

```powershell
Copy-Item .env.example .env
docker compose up --build
```

Open:

- web application: <http://localhost:3000>
- API health: <http://localhost:8080/health/ready>
- worker health: <http://localhost:8081/health/ready>
- MinIO console: <http://localhost:9001>
- Temporal UI: <http://localhost:8233>

Stop the stack with `docker compose down`. Use `docker compose down -v` only when local database, object-storage, and workflow data should be deleted.

## Install dependencies locally

Use Node.js 24.18.0, .NET SDK 10.0.302, Python 3.14, and uv 0.11.12 or later.

```powershell
npm --prefix apps/web ci
dotnet restore BidMatrix.slnx
uv sync --project src/agents
```

All direct application dependencies are pinned. npm and uv lockfiles capture transitive versions.

## Verification

```powershell
npm run lint
npm run typecheck
npm run test
npm run build
npm run backend:build
npm run backend:test
npm run agents:lint
npm run agents:typecheck
npm run agents:test
docker compose config --quiet
```

## Repository map

- `apps/web`: Next.js App Router and Tailwind CSS product shell.
- `src/backend`: ASP.NET Core modular-monolith boundary.
- `src/agents`: Python agent and Temporal worker host.
- `infra/docker`: pinned application container definitions.
- `docs/adr`: accepted architecture decisions.
- `docs/implementation-status.md`: current phase gates and honest capability state.
- `BIDMATRIX_AI_COMPANY_MASTER_PLAN.md`: authoritative F0 specification.

## Capability boundary

F0 establishes accounts, quarantine upload, analysis state, durable workflows, manual review, controlled tools, approvals, audit, and four deterministic agent demonstrations. Real OCR, requirement extraction, compliance matching, bid/no-bid scoring, outbound actions, billing, and production deployment are not F0 capabilities.

See `docs/operations/local-development.md` for detailed setup and `docs/product/f0-capability-boundaries.md` before adding customer-visible behavior.
