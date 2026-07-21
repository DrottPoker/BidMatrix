# Foundation F0 release gate

## Clean setup

```powershell
Copy-Item .env.example .env
npm --prefix apps/web ci
dotnet restore BidMatrix.slnx
uv sync --project src/agents --locked
docker compose up --detach --build --wait
```

## Static and automated verification

Run every command from repository root:

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
.\scripts\verify-f0.ps1
```

## Manual inspection

- Customer navigation contains no owner or internal OS pages.
- Owner Console prominently shows draft-only and disabled-external banners.
- Approval view renders the exact normalized payload, destination, hash, risk, expiry, policy, and adapter state.
- Analysis details explicitly state that requirement extraction is not implemented.
- Engineering diff is readable and the base fixture repository remains unchanged.
- OpenAPI is available only in Development.
- Logs contain no credentials, cookies, authorization headers, signed object URLs, document bodies, or model input/output.

## Dependency and secret review

```powershell
npm --prefix apps/web audit --omit=dev
dotnet list BidMatrix.slnx package --vulnerable --include-transitive
git grep -n -I -E "(BEGIN (RSA|OPENSSH|EC) PRIVATE KEY|AKIA[0-9A-Z]{16})"
```

Dependabot covers npm, NuGet, Python, Docker, and GitHub Actions. A clean result means the scanner reported no known issue at the time of execution, not that the system is vulnerability-free.

## Stop boundary

F0 completion does not authorize cloud production deployment, a live OpenAI key, real malware scanning, external communication, billing, remote Git, or real RFP extraction. Those require a new bounded phase and owner decisions.
