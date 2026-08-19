# ADR 0016: Self-host Better Auth on Neon PostgreSQL

> Amendment: ADR 0018 removes invite-gated signup and the auth role's invitation-inspection grant. Better Auth now permits self-service identity creation, while BidMatrix atomically creates a separate private workspace with fixed `owner` authority. ADR 0019 requires verified email, managed recovery, database rate limits, and mapped application-session revocation. Google and GitHub are deferred.

- Status: Accepted, passkey policy superseded by ADR 0017
- Date: 2026-08-12

## Context

ADR 0012 requires standard OIDC, explicit account linking, server-owned application sessions, invite-only onboarding, and phishing-resistant authentication for every `platform_owner`. ADR 0015 selected Neon PostgreSQL without selecting Neon Auth. Attempts to provision Neon Managed Auth for the isolated development branch failed through both the platform API and Neon Console, and its current managed contract does not provide the complete owner-MFA and recovery evidence required by S1.1.

BidMatrix still needs a working identity implementation before hosted runtime work can continue. The implementation must use the owner's Neon account without transferring tenant, role, invitation, session, or audit authority away from BidMatrix.

## Decision

BidMatrix self-hosts Better Auth inside the Next.js application at `/api/auth/[...all]`. Better Auth uses the same Neon `bidmatrix` database through a separate `better_auth` schema and a dedicated `bidmatrix_auth` login role. The role does not inherit privileges, cannot read BidMatrix authority tables, and receives only the Better Auth table privileges. Migration `0017` adds a fixed-search-path password-update trigger that executes transactionally without granting the role direct authority-table access or permission to invoke the trigger function.

Migration `0014_s1_better_auth` owns the Better Auth schema, tables, grants, and default privileges. Generated schema output must match the checked-in migration before deployment.

Better Auth provides:

- verified self-service email and password identities for tenant users;
- a confidential OIDC provider for the ASP.NET Core API;
- authorization code flow with PKCE;
- ES256 ID-token signing keys published through JWKS;
- exact `email`, `email_verified`, `name`, and `auth_time` claims.

The ASP.NET Core API remains the relying party and issues the existing server-validated BidMatrix cookie. It stores no Better Auth access token, refresh token, raw provider subject, or signing private key. BidMatrix remains authoritative for users, status, tenant membership, platform roles, invitations, application sessions, recovery controls, and audit events.

ADR 0017 removes passkeys from the current product. Platform-owner authorization depends on an authenticated BidMatrix session plus the `platform_owner` role in BidMatrix. Recent-authentication requirements remain in force for sensitive operations.

The OIDC client uses `client_secret_post` because the ASP.NET Core OpenID Connect handler sends confidential client credentials through the token request body. Production TLS is mandatory, and request bodies containing client credentials must never enter logs or traces.

The exact Better Auth packages remain pinned. The current implementation uses `1.7.0-rc.5` because the earlier stable package line contained a known OAuth-provider advisory. A release candidate is not automatically production-qualified. S1.1 hosted completion requires either a stable fixed release or an explicit dependency-risk approval after audit and regression testing.

Neon Managed Auth is not enabled. It may be reconsidered only through a new decision that preserves the same self-service, OIDC, recovery, and authority boundaries.

ADR 0017 supersedes the passkey-specific portions of this decision. ADR 0018
supersedes the invitation-specific portions, and ADR 0019 supplies local email
verification and managed recovery. The self-hosted Better Auth, isolated schema
and role, OIDC, explicit mapping, and BidMatrix authority boundaries remain
accepted.

## Consequences

- The complete local OIDC code flow is now exercised against the owner's isolated Neon development branch rather than a non-routable provider fixture.
- Better Auth records are isolated from BidMatrix authority data by schema and role privileges.
- Local verification and recovery are exercised through Mailpit without bypassing email verification.
- Production SMTP, provider logout qualification, production HTTPS, hosted secrets, rate-limit evidence, emergency access, and a real customer exercise remain production gates. Google and GitHub callback evidence is deferred unless the owner activates those methods.
- The Neon production branch remains unmodified until an explicit release decision.
- No identity event creates an agent goal, task, run, schedule, model request, Tool Gateway action, or external agent authority.
