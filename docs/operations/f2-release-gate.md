# Concierge Pilot F2 and S0 release gate

This is a historical regression contract. The current executable boundary is `docs/operations/s1-release-gate.md`, which runs this F2 gate before the S1.0 onboarding gate.

## Scope gate

- Customer results remain hidden until owner publication.
- Every visible item has an exact file, page, and quote citation.
- Owner corrections preserve the original extraction and increment a version.
- Stale review writes fail with HTTP 409.
- Rejected items do not appear in the customer report.
- Published requirements are searchable and filterable as a compliance matrix.
- The requirements CSV retains exact source evidence, includes blank customer response columns, and neutralizes spreadsheet-formula prefixes.
- No company match, score, bid or no-bid recommendation, billing, external action, cloud deployment, or production claim is present.
- No agent schedule, agent steering, live model dependency, or customer-facing agent is introduced.

## Static and automated verification

Run from the repository root:

```powershell
npm --prefix apps/web ci
npm --prefix apps/web audit --audit-level=high
npm run lint
npm run typecheck
npm run test
npm run build
dotnet restore BidMatrix.slnx
dotnet format BidMatrix.slnx --verify-no-changes --no-restore
dotnet build BidMatrix.slnx --configuration Release --no-restore
dotnet list BidMatrix.slnx package --vulnerable --include-transitive
uv sync --project src/agents --locked
uv run --directory src/agents ruff check .
uv run --directory src/agents mypy
uv run --directory src/agents pytest
docker compose --env-file .env.example config --quiet
```

Start only PostgreSQL before the backend integration suite:

```powershell
docker compose --env-file .env.example up --detach --wait postgres
$env:BIDMATRIX_TEST_POSTGRES_PORT='55432'
dotnet test BidMatrix.slnx --configuration Release --no-build
```

## Clean running-stack verification

Use a disposable Compose project so the gate cannot rely on or delete normal development data:

```powershell
docker compose --project-name bidmatrix-s0 --env-file .env.example config --quiet
docker compose --project-name bidmatrix-s0 --env-file .env.example up --detach --build --wait
.\scripts\verify-f2.ps1 -EnvFile .env.example -ComposeProjectName bidmatrix-s0
docker compose --project-name bidmatrix-s0 --env-file .env.example down --volumes
```

Before the final command, verify that `bidmatrix-s0` is the intended disposable project. The command permanently deletes only that project's volumes.

The running gate proves:

- migration `0009_f2_reviewable_results` applies with a recorded checksum;
- requirements, key dates, requested documents, and evaluation criteria are persisted with exact citations;
- customer results remain empty before publication;
- owner review writes are version checked and audited;
- corrected and original requirement text are both retained;
- publication changes the analysis to `completed` and exposes only non-rejected results;
- F0 tenancy, authentication, audit, sandbox, and disabled-external controls still pass;
- the current defaults do not grant agents external effects;
- API, worker, and web logs contain no error-level entry during the gate.

## CI gate

`.github/workflows/s1-quality.yml` retains the complete F2 regression inside the current two-job S1 gate:

1. `quality` installs pinned dependencies, runs the full npm audit, all web checks, .NET formatting and build, NuGet vulnerability scan, PostgreSQL tests, and all Python checks.
2. `compose` builds a clean stack, runs `verify-f2.ps1` and then `verify-s1.ps1`, always captures logs, and always deletes its disposable volumes.

Local verification does not prove that GitHub Actions passed. The workflow must run successfully on the branch before a remote release claim.

## Visual inspection

- Verify desktop and mobile navigation.
- Verify dashboard, empty states, loading states, errors, analysis list filters, upload progress, processing state, and published report.
- Verify requirements, dates, requested documents, evaluation criteria, and citations at narrow and wide viewports.
- Verify compliance-matrix search, filters, horizontal table navigation, source expansion, and CSV download.
- Verify keyboard focus, readable contrast, and reduced-motion behavior.

S0 changes no UI source, so the previous F2 visual result remains applicable. Any later UI change must repeat this inspection.

## Commercial gate

- Record actual processing time and corrections for every pilot analysis.
- Secure at least one paid pilot.
- Measure how many pilot customers request another analysis.
- Do not call the market validated from implementation or test results alone.

## Production stop boundary

Passing this gate proves the F2 local product baseline. It does not authorize production identity, public hosting, billing, legal claims, or agent steering. Continue by validating the intentionally small product loop in `docs/product/mvp-v1.md`; hosted deployment in `docs/product/s1-hosted-concierge.md` remains deferred until the owner resumes it.
