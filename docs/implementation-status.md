# Implementation status

Last updated: 2026-07-20

## Current release target

Foundation Release F0 is the active and bounded target. The authoritative scope is defined in `BIDMATRIX_AI_COMPANY_MASTER_PLAN.md`. Later roadmap capabilities are out of scope until every F0 phase gate has been satisfied in order.

Phases 0 and 1 are complete. Phase 2 is the next permitted implementation boundary.

## Repository assessment

The repository started with only a short README in Git. An uncommitted Vite, React, and TypeScript dashboard prototype was subsequently added. It provided useful visual direction, a reusable stage-card concept, foundation data, and a bid scoring demonstration.

The prototype conflicted with the authoritative F0 specification because it used Vite instead of Next.js, presented scoring that is outside F0, had floating dependencies, and lacked the required API, worker, data, storage, workflow, policy, and audit boundaries.

The useful visual direction and stage-card concept have been adapted into the Next.js product shell. The Vite runtime and scoring demonstration were removed. The F0 interface now labels unavailable capabilities honestly.

## Phase status

| Phase | Status | Evidence and remaining gate |
| --- | --- | --- |
| 0. Repository assessment and decisions | Complete | Repository conflict assessment, seven accepted ADRs, runtime files, formatting defaults, implementation status, and a verified build baseline exist. |
| 1. Monorepo and local infrastructure | Complete | .NET solution, Python package, Next.js and Tailwind app, pinned Compose stack, health checks, environment template, lockfiles, and local setup documentation are present. The full stack reached healthy state without an OpenAI key. |
| 2. Database and core domain | Ready, not started | Next boundary. Requires authoritative migrations, tenant model, core OS records, development seed, RLS, and tenant tests. |
| 3. Authentication and API foundations | Not started | Blocked by Phase 2 acceptance. |
| 4. Upload and minimal analysis pipeline | Not started | Blocked by Phase 3 acceptance. |
| 5. Tool Gateway, Policy Engine, and approvals | Not started | Blocked by Phase 4 acceptance. |
| 6. Four agents and workflows | Not started | Blocked by Phase 5 acceptance. |
| 7. Owner Console and customer shell | Not started | Blocked by Phase 6 acceptance. |
| 8. Engineering sandbox foundation | Not started | Blocked by Phase 7 acceptance. |
| 9. Quality, documentation, and release gate | Not started | Blocked by Phase 8 acceptance. |

## Capability truth table

| Capability | Current state | Customer claim allowed |
| --- | --- | --- |
| Foundation product shell | Implemented in Next.js and Tailwind CSS | Yes, as a foundation shell only |
| Bid scoring | Removed and outside F0 | No |
| Authentication | Not implemented | No |
| PDF quarantine upload | Not implemented | No |
| Analysis workflow | Not implemented | No |
| AI agents | Runtime dependencies installed, definitions not implemented | No |
| Tool Gateway and approvals | Not implemented | No |

## Phase 1 service gate

The Compose stack was built and started from the checked-in definitions. These endpoints returned HTTP 200 and every long-running service reported healthy:

| Service | Verified endpoint |
| --- | --- |
| Next.js web | `http://localhost:3000` |
| ASP.NET Core API | `http://localhost:8080/api` |
| API readiness | `http://localhost:8080/health/ready` |
| Python worker readiness | `http://localhost:8081/health/ready` |
| Temporal UI | `http://localhost:8233` |
| PostgreSQL 18.4 | Compose health check on host port `55432` |
| MinIO | Compose health check; quarantine and private buckets created |

The locally available host versions are .NET SDK 10.0.302, Python 3.14.4, Docker 29.6.1, Docker Compose v5.3.0, and Node.js 22.14.0. The pinned container targets are .NET SDK 10.0.302, ASP.NET 10.0.10, Python 3.14.6, and Node.js 24.18.0. Local npm installation also completed successfully, but the pinned Node 24 image is the verified cloud-portable build baseline.

## Verification record

- `npm ci`: 545 packages installed from lockfile, 0 vulnerabilities.
- `npm run lint`: passed with zero warnings.
- `npm run typecheck`: passed.
- `npm run test`: 1 test passed.
- `npm run build`: production build passed.
- `dotnet build BidMatrix.slnx --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test BidMatrix.slnx --no-build`: 1 integration test passed.
- `uv run --offline --directory src/agents ruff check .`: passed.
- `uv run --offline --directory src/agents mypy`: passed for 4 source files.
- `uv run --offline --directory src/agents pytest`: 2 tests passed.
- `docker compose --env-file .env.example config --quiet`: passed.
- `docker compose --env-file .env.example build`: all three application images built.
- `docker compose --env-file .env.example up --detach --wait`: all long-running services became healthy.
- Secret-shaped value scan found no committed credential material. Development values remain explicit placeholders.

No commit, push, remote branch, pull request, or deployment was performed.
