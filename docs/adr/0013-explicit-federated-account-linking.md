# ADR 0013: Link federated identities explicitly

- Status: Accepted, owner-strength portions superseded by ADR 0017
- Date: 2026-08-11

## Context

ADR 0012 selects managed OpenID Connect for hosted identity. BidMatrix already owns tenant membership, platform roles, server sessions, and the invite-only concierge lifecycle. A provider subject therefore cannot become tenant authority by itself.

Email-only account discovery is unsafe as a login-linking mechanism. Provider email addresses can change, aliases can differ, and an incorrect automatic match can attach an external identity to the wrong BidMatrix user. Storing raw provider subjects would also retain an unnecessary cross-system identifier.

The hosted platform-owner boundary is stricter than ordinary customer authentication. A valid OIDC token is not sufficient unless a signed provider claim proves the configured phishing-resistant authentication context.

## Decision

BidMatrix uses an explicit linking flow:

1. A user first authenticates to an existing BidMatrix account with a recent server-validated session.
2. The OIDC authorization-code flow runs through the server with PKCE, nonce, correlation state, and a short-lived external cookie.
3. The provider must return one unambiguous `iss`, `sub`, `email`, `email_verified=true`, and recent `auth_time` claim.
4. Linking succeeds only when the verified provider email exactly matches the existing BidMatrix email using case-insensitive comparison.
5. Login never creates or links an account from email. It resolves only an existing `(issuer, subject hash)` mapping.
6. The server hashes the subject immediately after token validation and before issuing the short-lived external cookie. PostgreSQL stores the issuer and `SHA-256(issuer + newline + subject)`. The raw subject and provider tokens are not stored, audited, logged, or returned by the API.
7. A `platform_owner` link or login succeeds only when the configured signed claim contains an exact allowlisted phishing-resistant value. Substring matches and a generic `mfa` value do not qualify automatically.
8. Revoking a federated identity revokes every active BidMatrix session for that user. The last authentication method cannot be removed unless another active provider mapping exists or both global native login and that account's native password are enabled.
9. Native login and native recovery are disabled outside Development unless explicit transition mode is enabled. A native password session never authorizes platform-owner operations outside Development, including transition mode.
10. BidMatrix remains the authority for users, tenant memberships, platform roles, sessions, invitation state, and audit events.

## Consequences

- Existing accounts require a deliberate one-time link before managed-provider login works.
- Provider compromise cannot assign a tenant or platform role without an existing BidMatrix mapping.
- A provider email change does not silently relink the identity. Support must use an explicit reviewed process.
- Provider access and refresh tokens remain outside the BidMatrix browser and database boundary.
- Transition mode can help an existing user reach the linking screen, but it cannot bypass the phishing-resistant owner policy.
- Password-based invitation acceptance is unavailable after native login is disabled. ADR 0014 supplies managed-provider invitation acceptance without weakening the existing-account linking rule.
- Local code and tests do not prove provider policy, provider recovery, callback registration, logout behavior, or emergency access.

## Supersession

ADR 0017 removes the passkey-specific authentication-context requirement in
Decision 7 and its transition-mode consequence. Platform-owner access now
requires an authenticated BidMatrix application session, the authoritative
`platform_owner` role, and recent authentication for sensitive operations. All
explicit-linking, subject-hashing, verified-email, session, invitation, and
native-fallback boundaries in this decision remain accepted.
