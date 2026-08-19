# ADR 0014: Consume tenant invitations through managed identity

- Status: Superseded by ADR 0018 on 2026-08-12
- Date: 2026-08-12

## Context

ADR 0011 makes BidMatrix invitations authoritative for initial tenant ownership. ADR 0012 selects managed OpenID Connect for hosted authentication, and ADR 0013 prohibits automatic email-based linking for existing accounts.

Password invitation acceptance cannot remain the only new-account path after native login is disabled. At the same time, a provider subject must not create a tenant, choose a role, or discover an invitation from email alone. The invitation bearer token also must not be placed in an OIDC query string, provider state visible to the browser, logs, analytics, or persistent browser storage.

## Decision

BidMatrix adds a distinct managed invitation flow:

1. The platform owner creates the same one-time tenant-owner invitation. BidMatrix remains authoritative for organization, invited email, fixed owner role, expiry, revocation, and single-use state.
2. The web client reads the bearer token from the URL fragment, removes the fragment immediately, and sends the token only in CSRF-protected request bodies.
3. `POST /v1/invitations/managed/prepare` is rate limited, validates that the invitation is pending, and writes a five-minute protected HttpOnly, SameSite Strict cookie containing only the invitation identifier. Its response contains only the fixed OIDC start path.
4. `GET /v1/auth/oidc/onboard` consumes that cookie before starting the normal server-side authorization-code flow with PKCE. The protected OIDC flow state carries the invitation identifier, never the invitation token.
5. The provider must supply one verified email and recent authentication. The verified email must equal the invitation email using case-insensitive comparison.
6. One PostgreSQL security-definer operation locks and consumes the invitation, then atomically creates the user, disabled native credential, fixed owner membership, federated mapping, active organization state, and token-free audit events.
7. The federated mapping stores the issuer and `SHA-256(issuer + newline + subject)`. The raw provider subject and provider tokens are not stored, returned, logged, or audited.
8. Successful completion creates the ordinary BidMatrix server session and application cookie. The provider does not become authoritative for tenant membership, application roles, session validity, or invitation state.
9. A managed-only user has no enabled native password even when global transition mode exposes native login to other users. The final federated mapping therefore cannot be revoked until another authentication method exists for that account.
10. This onboarding exception does not weaken ADR 0013. Login for an existing account still resolves only a previously linked issuer and subject hash and never creates or links an account from an email match.

## Consequences

- A hosted managed-only pilot can use the existing concierge invitation lifecycle without temporarily issuing a password to each new tenant owner.
- Losing or misdirecting an unused invitation link remains a bearer-secret risk. Expiry, revocation, careful manual delivery, CSRF protection, rate limiting, and single-use consumption remain mandatory.
- A callback failure does not restore the removed bearer token. The user must reopen the original invitation link and retry with the invited provider account.
- Native credential recovery can enable a password only when the explicitly configured native recovery boundary is active. Hosted managed-only operation keeps native login and recovery disabled.
- Local tests prove the BidMatrix-owned state, database, authorization, audit, and web boundaries. ADR 0016 later adds a locally exercised Better Auth challenge and callback, and ADR 0017 simplifies owner sign-in. Hosted recovery, verified-email delivery, logout, configured social-provider callbacks, and a newly invited-person exercise remain unproven.
- No onboarding event invokes an agent, model, schedule, Tool Gateway call, or agent-controlled external effect.
