# Better Auth OIDC operations

## Scope

Better Auth is BidMatrix's managed identity provider. Next.js serves it at `/api/auth` and stores its records in the isolated Neon `better_auth` schema. The ASP.NET Core API is the OIDC relying party. BidMatrix remains authoritative for application users, private workspaces, memberships, platform roles, server sessions, and audit events.

The local authorization-code flow has been exercised against the owner's Neon development branch, including discovery, ES256 JWKS, PKCE, confidential code exchange, signed-claim validation, and existing-owner linking. ADR 0018 adds ordinary self-service registration. Do not claim hosted completion until every production evidence item below passes.

## Customer methods

- Verified email and password is the current customer method.
- Google and GitHub are deferred. Their credentials and callbacks stay empty until the owner chooses to activate them.
- On first managed login, BidMatrix creates a new private workspace and fixed `owner` membership.
- Registration never grants `platform_owner`, joins an existing workspace, or invokes an agent.

Development sends verification and reset messages to Mailpit. Hosted startup fails closed unless Better Auth uses HTTPS and SMTP uses authenticated TLS.

## Callback registration

For an API origin such as `https://api.example.com`, register the Better Auth OIDC client callbacks:

```text
https://api.example.com/signin-oidc
```

For social providers, register Better Auth callbacks on the public web origin:

```text
https://app.example.com/api/auth/callback/google
https://app.example.com/api/auth/callback/github
```

Local social callbacks are:

```text
http://localhost:3000/api/auth/callback/google
http://localhost:3000/api/auth/callback/github
```

GitHub must be allowed to read the user's email address. Never register the ASP.NET callback as a Google or GitHub callback.

## Runtime configuration

Inject secrets from a secret manager and never put them in Git, shell history, tickets, or screenshots.

| Variable | Hosted value |
| --- | --- |
| `BIDMATRIX_ENVIRONMENT` | Non-Development environment name |
| `BIDMATRIX_OIDC_ENABLED` | `true` |
| `BIDMATRIX_API_PUBLIC_BASE_URL` | Public HTTPS API origin |
| `BIDMATRIX_PUBLIC_BASE_URL` | Public HTTPS web origin |
| `BIDMATRIX_OIDC_AUTHORITY` | Better Auth HTTPS authority |
| `BIDMATRIX_OIDC_CLIENT_ID` | Confidential OIDC client identifier |
| `BIDMATRIX_OIDC_CLIENT_SECRET` | Injected OIDC client secret |
| `BIDMATRIX_OIDC_REQUIRE_HTTPS_METADATA` | `true` |
| `BIDMATRIX_IDENTITY_TRANSITION_MODE` | `true` only during a bounded existing-account migration |
| `BIDMATRIX_NATIVE_LOGIN_ENABLED` | `false` after existing accounts are linked |
| `BIDMATRIX_NATIVE_RECOVERY_ENABLED` | `false` after managed recovery is proven |
| `BETTER_AUTH_URL` | Public HTTPS web origin |
| `BETTER_AUTH_SECRET` | Independent high-entropy secret |
| `BETTER_AUTH_TRUSTED_ORIGINS` | Exact public web origins |
| `BETTER_AUTH_DATABASE_URL` | TLS connection for `bidmatrix_auth` |
| `BETTER_AUTH_SMTP_HOST` | Production SMTP hostname |
| `BETTER_AUTH_SMTP_PORT` | Production SMTP port |
| `BETTER_AUTH_SMTP_SECURE` | `true` for implicit TLS, otherwise use required STARTTLS |
| `BETTER_AUTH_SMTP_REQUIRE_TLS` | `true` when implicit TLS is not used |
| `BETTER_AUTH_SMTP_USER` | Injected SMTP username |
| `BETTER_AUTH_SMTP_PASSWORD` | Injected SMTP password |
| `BETTER_AUTH_EMAIL_FROM_ADDRESS` | Verified sender address |
| `BETTER_AUTH_EMAIL_FROM_NAME` | Customer-visible sender name |
| `BETTER_AUTH_GOOGLE_CLIENT_ID` | Optional Google web client identifier |
| `BETTER_AUTH_GOOGLE_CLIENT_SECRET` | Optional Google client secret |
| `BETTER_AUTH_GITHUB_CLIENT_ID` | Optional GitHub application identifier |
| `BETTER_AUTH_GITHUB_CLIENT_SECRET` | Optional GitHub client secret |

The public web and API origins must be same-site, normally HTTPS subdomains of the same registrable domain. HTTPS is mandatory outside Development. Reverse proxies and telemetry must redact token endpoint bodies.

## Development bootstrap

Load the Neon development configuration from .NET User Secrets without printing it:

```powershell
. .\scripts\web\Import-BetterAuthUserSecrets.ps1
Push-Location apps\web
try {
    npm run auth:schema:check
    npm run auth:bootstrap-owner
    npm run auth:bootstrap-client
} finally {
    Pop-Location
}
```

Owner and client bootstrap commands are one-shot. Rotations require an API restart and a fresh OIDC exercise. Never delete identity rows to force a bootstrap rerun.

Start the services with the loaded development secrets:

```powershell
dotnet run --project src/backend/BidMatrix.Api
.\scripts\web\Start-BetterAuthWeb.ps1
```

## Existing-account transition

1. Apply migrations and verify Better Auth schema parity.
2. Bootstrap the confidential OIDC client and exact callback origins.
3. Bootstrap the existing Better Auth owner once.
4. Enable bounded transition mode with OIDC and native login.
5. Link the existing platform owner from a recent BidMatrix session using the exact matching email.
6. Prove a fresh Better Auth login retains the BidMatrix `platform_owner` role.
7. Disable native login, native recovery, and transition mode after managed recovery is ready.

Never relink an account with direct SQL. Never copy a raw provider subject into logs or operator notes.

## Logout

The application revokes the current BidMatrix server session first. The browser then calls Better Auth's same-origin `signOut` operation to delete the managed identity session before returning to `/login`. Do not route logout through the OIDC `/oauth2/end-session` endpoint because BidMatrix deliberately does not persist the ID token required by RP-initiated logout.

Google and GitHub accounts are not globally signed out. BidMatrix logout ends the application's sessions without ending the user's unrelated provider sessions.

## New-account registration

1. Open `/register`.
2. Register with name, email, and a password of at least 8 characters.
3. Open the one-hour verification link. Unverified sign-in must return HTTP 403.
4. Sign in again so Better Auth continues to the BidMatrix OIDC login.
5. If the subject is new, BidMatrix atomically creates a private workspace and fixed `owner` membership.
6. Confirm `/v1/me` contains exactly the new workspace membership and no platform role.
7. Confirm native BidMatrix password login is unavailable for the self-service account.
8. Confirm the registration audit exists and no agent run was created.

A different provider subject cannot claim an existing BidMatrix email. Multi-provider account linking and multi-user workspace membership require explicit authenticated product flows and are not inferred during registration.

## Recovery and revocation

Managed recovery is implemented locally. `/forgot-password` always returns a neutral result. The email link expires after 30 minutes, is consumed once, and reaches `/reset-password` through a continuation that moves the bearer into a fragment. A successful reset revokes Better Auth sessions. Migration `0017` also rotates the mapped BidMatrix security stamp and revokes application sessions transactionally. Do not disable the final emergency owner path or admit a production customer until the same flow is exercised through a production SMTP provider under hosted HTTPS.

Revoking a mapping from `/app/account` revokes its BidMatrix sessions, retains token-free audit history, and refuses to remove the final authentication method when native login is disabled.

## Required hosted evidence

Record evidence without secrets or token material:

1. discovery, ES256 JWKS, authorization, code exchange, cookie issuance, and exact callback origins;
2. PKCE and server-side flow configuration with no provider token in browser storage;
3. ordinary email/password registration creating a private workspace with only `owner` authority;
4. Google and GitHub registration and fresh login only if the owner activates either provider;
5. denial of duplicate-email claims, stale authentication, invalid issuer, nonce, and callback state;
6. BidMatrix and Better Auth session logout with rejection of both old application sessions;
7. production verified-email delivery, password recovery, rate limiting, and emergency owner recovery;
8. native login and recovery disabled after transition;
9. database and audit inspection showing only subject hashes and no provider token;
10. zero agent tasks, runs, schedules, model calls, or external effects from registration.

## Rollback boundary

A provider outage does not authorize direct credential SQL or repeated bootstrap. Use the documented emergency-access process. Any temporary transition-mode change requires an operator record, a time limit, and proof that native fallback was disabled again.
