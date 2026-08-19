# ADR 0012: Use managed OIDC for hosted identity

- Status: Accepted, passkey policy superseded by ADR 0017
- Date: 2026-08-11

## Context

BidMatrix S1.0 introduced a provider-neutral invitation and tenant lifecycle while account credentials still used the local ASP.NET Core password path. Before S1.1, that path provided framework password hashing, secure cookies, CSRF protection, login throttling, lockout, and security-stamp validation, but not independently revocable sessions, account recovery, a production operator bootstrap, or phishing-resistant MFA. S1.1a and S1.1b have since supplied the provider-neutral session, recovery, and bootstrap controls.

Building a complete hosted identity provider inside BidMatrix would add a high-risk security product to the RFP product. Microsoft recommends mature authentication mechanisms and identifies passkeys or FIDO2 as the strongest phishing-resistant MFA option. The BidMatrix master plan also requires phishing-resistant MFA for future production owner sessions.

The provider cannot be selected or proven only from repository code. A real hosted tenant, credentials, callback origins, recovery policy, and MFA policy are deployment evidence.

## Decision

BidMatrix will use a managed OpenID Connect provider for hosted customer and operator authentication. The hosted integration must use the authorization-code flow with PKCE, terminate the provider flow server-side, issue the existing HttpOnly BidMatrix application cookie, and keep provider access and refresh tokens out of browser storage.

The provider policy must enforce phishing-resistant MFA for every `platform_owner`. Tenant-owner MFA is required before S2 self-service and should be offered during the invited pilot. BidMatrix must verify the provider authentication context before granting a platform-owner session rather than trusting the presence of an unverified client claim.

S1.1 starts with a provider-neutral local identity control layer:

1. every application cookie is backed by a hashed, server-side session record with idle and absolute expiration;
2. users can list and revoke sessions, and password changes revoke every existing session;
3. a platform owner can issue a short-lived, single-use recovery link for manual concierge delivery, with only the token hash stored;
4. the recovery flow changes no account state until the valid token is consumed, then rotates the security stamp and revokes all sessions;
5. production owner creation is an explicit one-shot command that fails when a platform owner already exists;
6. every session, credential, recovery, and bootstrap mutation is auditable without logging secrets.

S1.1c adds a locally verified managed OIDC integration boundary:

1. hosted startup fails closed unless OIDC, public origins, provider authority, confidential client configuration, and the owner authentication-context allowlist are explicit;
2. the backend uses authorization code plus PKCE, a short-lived external cookie, the existing server-validated BidMatrix application cookie, and no saved provider tokens;
3. existing accounts link deliberately after recent authentication, with verified email equality and no automatic email linking during login;
4. only a hash of issuer plus subject is stored, while the raw subject and provider tokens stay out of storage, APIs, logs, and audit metadata;
5. every platform-owner OIDC session requires an exact allowlisted value from the configured signed provider claim; and
6. native login and recovery are Development-only unless bounded transition mode is explicit, while native platform-owner sessions remain denied outside Development.

ADR 0013 defines the account-linking and native-fallback details. ADR 0014 defines managed-provider acceptance of a BidMatrix tenant invitation.

ADR 0017 supersedes the passkey-specific owner requirement for the first
owner-usable SaaS release. Standard OIDC, explicit mapping, recent
authentication, recovery, and BidMatrix-owned authorization remain required.

The local password and manual recovery path remains a Development and transition mechanism. It is not the hosted identity claim. Once managed OIDC is enabled in a hosted environment, provider recovery replaces native recovery for federated identities and no password fallback may silently bypass provider MFA.

## Consequences

- Session revocation and audit semantics remain owned by BidMatrix and survive a provider change.
- S1.0 invitation records and BidMatrix memberships remain canonical for tenant authority while provider subjects are explicit login mappings only.
- A support-issued recovery link is acceptable only for the controlled concierge phase and must be delivered through a trusted channel.
- The S1.1c code boundary can be verified locally, but S1.1 cannot be marked complete from local tests alone. Under ADR 0017 it still requires Better Auth recovery and verified-email delivery, configured social-provider callbacks, callback and logout validation, proof that native fallback is disabled, and a live managed-provider onboarding exercise for a newly invited user.
- No identity event invokes an agent, model, schedule, Tool Gateway call, or agent-controlled external effect.
