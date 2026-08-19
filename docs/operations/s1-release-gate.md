# Hosted Concierge S1 release gate

## Current scope

The executable boundary covers self-service registration, verified email, managed password recovery, rate limits, cross-session revocation, private workspace provisioning, Better Auth OIDC, and the complete F2 customer-flow regression. Passing locally does not prove a production SMTP provider, hosted HTTPS and secrets, production callbacks, storage operations, disaster recovery, observability, billing, or commercial validation. Google and GitHub are deferred.

The gate requires:

- `/register` accepts ordinary email/password registration without an invitation;
- unverified accounts are blocked and verified email/password accounts can recover securely;
- the first managed login creates exactly one private workspace and fixed `owner` membership;
- registration cannot grant platform authority, join an existing workspace, or create agent work;
- duplicate-email and provider-subject conflicts fail closed;
- provider subjects and tokens remain outside BidMatrix browser storage, API responses, logs, and audit;
- every application cookie is backed by a hash-only bounded server session;
- Better Auth uses an isolated schema and role that cannot read BidMatrix user authority;
- password reset revokes Better Auth and mapped BidMatrix sessions without exposing recovery tokens;
- customer results remain hidden until owner publication and retain exact source citations afterward; and
- production configuration fails closed without managed identity and verified-email delivery.

## Static and automated verification

```powershell
npm --prefix apps/web ci
npm --prefix apps/web audit --audit-level=moderate
npm run lint
npm run typecheck
npm run test
npm run build
dotnet restore BidMatrix.slnx
dotnet format BidMatrix.slnx --verify-no-changes --no-restore
dotnet build BidMatrix.slnx --configuration Release --no-restore
dotnet list BidMatrix.slnx package --vulnerable --include-transitive
uv sync --project src/agents --locked
uv run --directory src/agents ruff check . --no-cache
uv run --directory src/agents mypy
uv run --directory src/agents pytest
docker compose --env-file .env.example config --quiet
```

Start PostgreSQL before the backend integration suite:

```powershell
docker compose --env-file .env.example up --detach --wait postgres
$env:BIDMATRIX_TEST_POSTGRES_PORT='55432'
dotnet test BidMatrix.slnx --configuration Release --no-build
```

The database suite applies all migrations to a fresh database and verifies self-service ownership, role isolation, duplicate-email denial, audit evidence, session security, and zero agent runs.

## Running-stack verification

```powershell
docker compose --project-name bidmatrix-s1 --env-file .env.example up --detach --build --wait
.\scripts\verify-f2.ps1 -EnvFile .env.example -ComposeProjectName bidmatrix-s1
docker compose --project-name bidmatrix-s1 --env-file .env.example down --volumes
```

The final command permanently deletes only the disposable `bidmatrix-s1` volumes. Confirm the project name first.

## Better Auth and Neon development

```powershell
. .\scripts\web\Import-BetterAuthUserSecrets.ps1
Push-Location apps/web
try { npm run auth:schema:check } finally { Pop-Location }
.\scripts\verify-better-auth-oidc.ps1
```

Additionally register a disposable Development account through `/register`, verify it through Mailpit, and prove that sign-in fails before verification. Confirm the browser reaches `/app`, request a reset, prove the prior session and password are rejected, then inspect Neon for an active private workspace, `owner` membership, disabled native credential, zero platform roles, one registration audit event, and zero agent runs.

## CI gate

`.github/workflows/s1-quality.yml` runs pinned dependency installation, vulnerability checks, all web checks, .NET format/build/tests, Python checks, and a clean Compose F2 regression. Local verification does not prove that GitHub Actions passed.

## Visual inspection

Inspect `/register`, `/login`, `/forgot-password`, `/reset-password`, `/app`, `/app/account`, `/owner/access`, and `/recover` at desktop and narrow mobile sizes. Verify labels, focus order, validation, loading and error states, absence of deferred social-provider noise, session actions, recovery-token cleanup, and the transition into the private workspace.

## Production stop boundary

Do not admit a real production customer until production email delivery, HTTPS, hosted secrets, logout, emergency access, private document handling, restore, monitoring, and incident response are proven. Google and GitHub do not block the email/password launch and remain deferred. Do not begin agent steering until the owner records a new decision after ADR 0010's working SaaS gate is satisfied.
