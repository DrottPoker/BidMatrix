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
uv sync --project src/agents --locked
```

Bash:

```bash
cp .env.example .env
npm --prefix apps/web ci
dotnet restore BidMatrix.slnx
uv sync --project src/agents --locked
```

Values in `.env.example` are development-only placeholders. Do not reuse them in a deployed environment.

`BIDMATRIX_PUBLIC_BASE_URL` is used for authentication and recovery redirects. Outside Development it must be an explicit HTTPS origin.

Local identity controls use these defaults:

| Setting | Default | Purpose |
| --- | --- | --- |
| `BIDMATRIX_SESSION_IDLE_MINUTES` | `30` | Revoke a session after bounded inactivity. |
| `BIDMATRIX_SESSION_ABSOLUTE_HOURS` | `8` | Revoke a session even when it remains active. |
| `BIDMATRIX_RECOVERY_LIFETIME_MINUTES` | `30` | Expire an unused manual recovery link. |
| `BIDMATRIX_RECENT_AUTH_MINUTES` | `15` | Require a fresh platform-owner login before recovery operations. |

`.env.example` keeps Better Auth OIDC disabled and native login plus recovery enabled for deterministic Compose Development. It includes placeholder auth-role, Better Auth, and Mailpit values so migrations through `0017` can be applied and email can be captured locally. Google and GitHub values are empty because those methods are deferred. The live Neon development configuration is stored in .NET User Secrets and documented in `docs/operations/managed-oidc.md`. Do not add real client secrets to `.env.example`.

## Run the complete local stack

```powershell
docker compose up --detach --build --wait
```

Compose waits on health conditions for PostgreSQL, MinIO, Temporal, the API, the worker, and the web application. Mailpit starts as the local SMTP sink. The MinIO initialization service creates the quarantine and private buckets idempotently.

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
| Mailpit SMTP | `localhost:1025` |
| Mailpit UI and API | `http://localhost:8025` |

## Register a local account

The default Compose stack keeps managed OIDC disabled. To test real self-service registration, load the Neon Development Better Auth configuration and start the API and web application as described in `docs/operations/managed-oidc.md`.

1. Open `http://localhost:3000/register`.
2. Enter a name, an unregistered email, and a password of at least 8 characters.
3. Open `http://localhost:8025` and follow the verification link.
4. Sign in and complete the managed login continuation.
5. Confirm that `/app` opens the new private workspace.
6. Confirm `/v1/me` contains only the private workspace `owner` membership and no platform role.

Development never auto-verifies email/password accounts. Mailpit captures verification and recovery messages. Hosted configuration fails closed without HTTPS and authenticated TLS SMTP. Google and GitHub are deferred.

## Use local account security and recovery

- Open `http://localhost:3000/app/account` to list the current account's server sessions, revoke a session, close other sessions, or change the password.
- Open `http://localhost:3000/forgot-password` for enumeration-safe managed recovery. The reset continuation moves the bearer into a fragment and clears it before rendering the form.
- Open `http://localhost:3000/owner/access` as a recently authenticated Development owner to create or revoke a one-time recovery link after verifying the account owner outside BidMatrix.
- Open the complete link in a separate browser session. `/recover` removes the token fragment immediately, validates it through a CSRF-protected request body, and requires a fresh sign-in after reset.

Password change and successful recovery revoke every existing session. Recovery links are shown once, expire after 30 minutes by default, and are stored only as hashes. The operator procedure is `docs/operations/identity-bootstrap-and-recovery.md`.

## Stop and reset

Stop containers while preserving local volumes:

```powershell
docker compose down
```

Delete containers and all volumes for the selected Compose project:

```powershell
docker compose down --volumes
```

The volume-removal command permanently removes that project's local database, object-storage, workflow, data-protection, and engineering-worktree data. Use an explicit project name for disposable verification so normal development data is not selected.

## Run services outside Compose

Web:

```powershell
npm run dev
```

To run Next.js against the Neon Better Auth configuration without printing User Secrets:

```powershell
.\scripts\web\Start-BetterAuthWeb.ps1
```

API:

```powershell
dotnet run --project src/backend/BidMatrix.Api
```

Worker:

```powershell
uv run --project src/agents python -m bidmatrix_agents.worker
```

The worker readiness endpoint remains unavailable until it can connect to Temporal. No OpenAI API key is required or authorized for the S0 product path. The configured Development agent mode is deterministic.

## Use the managed Neon development database

BidMatrix has a dedicated Neon project and an isolated `development` branch. The repository does not contain its credentials. The local API project reads the development connection settings from .NET User Secrets.

Use the controlled database-only commands when checking the managed branch:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/backend/BidMatrix.Api/BidMatrix.Api.csproj --configuration Release -- --migrate-database
dotnet run --project src/backend/BidMatrix.Api/BidMatrix.Api.csproj --configuration Release -- --verify-database-access
```

See `docs/operations/neon-development.md` before changing branches, credentials, roles, or schema. Compose continues to use its local PostgreSQL service for deterministic full-stack gates.

With the Neon-backed web and API running on ports 3000 and 8080, verify the real local Better Auth flow:

```powershell
.\scripts\verify-better-auth-oidc.ps1
```

The script proves the OIDC code flow, ordinary email/password owner linking, the hash-only mapping, and BidMatrix-owned platform-role authorization. Google and GitHub require separately configured provider credentials.

## Essential F2 end-to-end verification

With a healthy stack running:

```powershell
.\scripts\verify-f2.ps1
```

The script signs in with the Development owner and proves the complete F2 path: controlled PDF upload, extraction, hidden pre-publication results, exact citations, owner correction, stale-version rejection, owner rejection, publication, customer delivery, migration checksum, audit events, audit chain, and disabled external actions.

The script also invokes the historical F0 regression gate. Its four agent demonstrations are deterministic and manually triggered by the script. They do not schedule themselves, steer company work, reach the customer product, or perform external actions.

## S1 identity verification

The .NET integration suite applies migrations through `0017`, creates a self-service account transactionally, and proves private workspace ownership, zero platform roles, disabled native credentials, duplicate-email denial, audit evidence, role isolation, managed-password application-session revocation, and zero agent runs. `verify-better-auth-oidc.ps1` separately follows the real local provider flow against Neon development. The checked-in web tests verify registration, verification delivery configuration, neutral recovery, fragment-only continuation, reset UI, and deferred social-provider presentation.

## Isolated S1 verification

Use a dedicated Compose project when verifying from clean volumes:

```powershell
docker compose --project-name bidmatrix-s1 --env-file .env.example up --detach --build --wait
.\scripts\verify-f2.ps1 -EnvFile .env.example -ComposeProjectName bidmatrix-s1
docker compose --project-name bidmatrix-s1 --env-file .env.example down --volumes
```

The final command intentionally deletes only the disposable `bidmatrix-s1` volumes. Confirm the project name before running it.

## Cloud continuation

The Compose topology is a development environment, not a production deployment template. S1 supplies self-service private workspaces, local sessions, verified email, managed and Development recovery, one-shot bootstrap, explicit provider mappings, and self-hosted Better Auth. The hosted gate must still prove production SMTP delivery, HTTPS and secrets, logout, native-fallback disablement, emergency access, and real customer registration. Google and GitHub remain deferred. Later gates add production Neon promotion, private storage, malware scanning, backups, restore drills, observability, and billing.

Agent steering remains frozen under ADR 0010. A production deployment does not itself authorize agent schedules, live model mode, or broader tool permissions.
