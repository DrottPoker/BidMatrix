# Neon development database

This runbook operates the live managed development database in the owner's Neon account. It does not authorize production traffic or changes to the default production branch.

## Canonical resources

| Resource | Value |
| --- | --- |
| Organization | `Auxron` |
| Project | `BidMatrix` |
| Project ID | `gentle-glitter-10708743` |
| Region | `aws-eu-central-1`, Frankfurt |
| PostgreSQL | 18 |
| Production branch | `production`, `br-snowy-fog-b25dd9bi` |
| Development branch | `development`, `br-muddy-hat-b2zy3lrf` |
| Application database | `bidmatrix` |

The production branch was empty when the development branch was created and has not received BidMatrix migrations. Run development checks only against `br-muddy-hat-b2zy3lrf`.

## Local configuration

The API project has a development-only .NET User Secrets identifier. The configured keys are:

- `POSTGRES_HOST` for the pooled runtime endpoint;
- `POSTGRES_MIGRATION_HOST` for the direct migration endpoint;
- `POSTGRES_PORT` and `POSTGRES_DATABASE`;
- `POSTGRES_SSL_MODE=VerifyFull`;
- `POSTGRES_CHANNEL_BINDING=Require`;
- separate migration, application, audit, and Better Auth role names and passwords;
- `BETTER_AUTH_DATABASE_URL`, `BETTER_AUTH_SECRET`, `BETTER_AUTH_URL`, and trusted origins for the self-hosted identity service; and
- local Better Auth owner bootstrap and OIDC client values required by the isolated development exercise; and
- local Mailpit SMTP values used for verification and password-recovery evidence.

Do not run `dotnet user-secrets list` in recorded terminals because it prints values. Do not copy a Neon connection string into `.env`, `.env.example`, documentation, tickets, or chat.

## Apply and verify migrations

PowerShell:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/backend/BidMatrix.Api/BidMatrix.Api.csproj --configuration Release -- --migrate-database
dotnet run --project src/backend/BidMatrix.Api/BidMatrix.Api.csproj --configuration Release -- --verify-database-access
```

The migration command uses the direct endpoint and the Neon owner role. The verification command opens independent pooled connections as `bidmatrix_app`, `bidmatrix_audit`, and `bidmatrix_auth`, then fails if a runtime role is missing, elevated, inheriting, unable to connect, or able to bypass row-level security. It also verifies that the auth role can use only the intended Better Auth boundary and cannot read BidMatrix user authority.

Verify that Better Auth's generated schema matches migrations `0014`, `0015`, and `0017`:

```powershell
. .\scripts\web\Import-BetterAuthUserSecrets.ps1
Push-Location apps\web
try { npm run auth:schema:check } finally { Pop-Location }
```

Current development evidence:

- 17 migration records from `0001_core_domain` through `0017_s1_managed_identity_recovery`;
- every migration has a checksum;
- the invitation table is removed and the narrow self-service registration function is present;
- 13 tables in the separate `better_auth` schema, including the database-backed rate-limit table;
- the application, audit, and auth runtime roles are login roles with no superuser, database creation, role creation, inheritance, or RLS bypass capability; and
- `bidmatrix_auth` can use Better Auth tables but cannot select from `public.users`, inspect invitations, or execute the managed-password trigger function directly;
- a real registration sends verification email to Mailpit and an unverified identity cannot sign in;
- the managed reset flow consumes its emailed token, rejects the old password, accepts the new password, and revokes the previous Better Auth session; and
- integration evidence proves that the same credential update transactionally revokes mapped BidMatrix sessions without granting the auth role direct access to application authority.

## Branch discipline

1. Make and verify schema changes locally first.
2. Apply the checked-in migration chain to `development` only.
3. Run `--verify-database-access` and the relevant application gates.
4. Compare the development and production schemas before any promotion.
5. Require an explicit production release decision before applying changes to `production`.
6. Use forward-fix migrations. Never rewrite an applied migration.

## Better Auth boundary

Neon Managed Auth is not provisioned. ADR 0016 instead self-hosts Better Auth in the Next.js application and stores its records in `better_auth` through `bidmatrix_auth`. BidMatrix remains authoritative for users, private workspaces, memberships, platform roles, application sessions, and audit records.

The local development OIDC client, owner identity, ES256 signing key, and explicit BidMatrix owner mapping have been created only on the development branch. Ordinary Better Auth email/password owner linking and BidMatrix-owned role authorization are verified. ADR 0018 adds self-service accounts with private workspaces. ADR 0019 adds verified email, managed recovery, database rate limits, and mapped application-session revocation. Google and GitHub are deferred and remain hidden.

Do not enable Neon Managed Auth, copy Better Auth records into `public`, grant the auth role broad public-schema access, or create provider mappings through direct SQL.

## Remaining production controls

Before production use, configure hosted secret management, production authenticated TLS SMTP, branch protection, network policy, capacity, monitoring, encrypted backups, a restore drill, incident response, credential rotation, production HTTPS, and a stable security-qualified Better Auth release. Exercise registration, verification, recovery, session revocation, logout, and emergency access through the hosted system. Record evidence without connection strings or credential values.
