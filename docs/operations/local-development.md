# Local development

## Supported baseline

- Node.js 24.18.0
- npm 11 or later
- .NET SDK 10.0.302
- Python 3.14
- uv 0.11.12 or later
- Docker Desktop with Compose v2

The container definitions pin exact application runtime and service image versions. Local patch versions may differ when they remain within the recorded runtime major.

## First setup

PowerShell:

```powershell
Copy-Item .env.example .env
npm --prefix apps/web ci
dotnet restore BidMatrix.slnx
uv sync --project src/agents
```

Bash:

```bash
cp .env.example .env
npm --prefix apps/web ci
dotnet restore BidMatrix.slnx
uv sync --project src/agents
```

Values in `.env.example` are development-only placeholders. Do not reuse them in a deployed environment.

## Run the complete local stack

```powershell
docker compose up --build
```

Compose waits on health conditions for PostgreSQL, MinIO, Temporal, the API, the worker, and the web application. The MinIO initialization service creates the quarantine and private buckets idempotently.

Service endpoints:

| Service | Endpoint |
| --- | --- |
| Web | `http://localhost:3000` |
| API | `http://localhost:8080` |
| API readiness | `http://localhost:8080/health/ready` |
| Worker readiness | `http://localhost:8081/health/ready` |
| PostgreSQL | `localhost:55432` |
| MinIO S3 | `http://localhost:9000` |
| MinIO console | `http://localhost:9001` |
| Temporal gRPC | `localhost:7233` |
| Temporal UI | `http://localhost:8233` |

## Stop and reset

Stop containers while preserving local volumes:

```powershell
docker compose down
```

Delete containers and all local BidMatrix volumes:

```powershell
docker compose down -v
```

The volume-removal command permanently removes local development database, object-storage, and workflow data.

## Run services outside Compose

Web:

```powershell
npm run dev
```

API:

```powershell
dotnet run --project src/backend/BidMatrix.Api
```

Worker:

```powershell
uv run --project src/agents python -m bidmatrix_agents.worker
```

The worker readiness endpoint remains unavailable until it can connect to Temporal. No OpenAI API key is required in Development.

## Cloud continuation

The Compose topology is a development environment, not a production deployment template. Cloud work should preserve the API authority boundary and replace infrastructure through adapters: managed PostgreSQL, private S3-compatible storage, a supported Temporal deployment or Temporal Cloud, and secret-manager supplied configuration. Production deployment remains outside F0 and requires owner approval.
