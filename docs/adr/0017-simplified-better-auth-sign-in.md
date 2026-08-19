# ADR 0017: Simplify Better Auth sign-in

> Amendment: ADR 0018 removes the invitation gate and gives each new managed identity a separate private workspace with no platform role. ADR 0019 makes verified email/password the current customer experience and defers Google and GitHub activation.

- Status: Accepted
- Date: 2026-08-12

## Context

The first Better Auth implementation required a WebAuthn passkey before a
`platform_owner` identity could be linked or used. That added a second
interactive ceremony and made the first owner-usable SaaS harder to operate.
The owner has chosen a simpler first release: email and password as the primary
method, with Google and GitHub as optional alternatives.

## Decision

Better Auth is the only customer-visible identity sign-in when managed OIDC is
enabled. It supports:

- self-service email and password;
- Google OAuth when its client credentials are configured; and
- GitHub OAuth when its client credentials are configured.

Passkeys are not part of the current product. Migration
`0015_s1_simplified_authentication` refuses to proceed if registered passkeys
exist, then removes the unused passkey table, removes the passkey-only session
field, and replaces the provider-mapping functions without an authentication-
method parameter.

Google and GitHub remain implementation-supported but are hidden and inactive
in the current customer experience. A later activation must prove provider
callbacks and verified provider email. BidMatrix still requires an explicit
mapping or narrow self-service registration before issuing an application
session. Matching email alone never creates a BidMatrix mapping.

Better Auth account linking accepts only the same email and encrypts stored
OAuth token material. BidMatrix receives the stable Better Auth OIDC subject,
verified email, and recent authentication time. It stores only the issuer and
subject hash and remains authoritative for user status, organizations,
memberships, platform roles, sessions, invitations, recovery controls, and
audit events.

Platform-owner authorization now depends on the authenticated BidMatrix
session plus the `platform_owner` role in BidMatrix. Recent-authentication
requirements remain in force for sensitive operations. Native BidMatrix login
and recovery remain explicit Development or migration controls and stay hidden
when managed identity is the active product path.

## Consequences

- The owner can link and use Better Auth with email and password without
  Windows Hello or another passkey ceremony.
- Google and GitHub buttons remain hidden until a later owner-approved
  activation, even if server-side credentials are present.
- Provider client secrets and OAuth tokens do not enter browser configuration,
  BidMatrix API responses, documentation, or source control.
- This decision supersedes the passkey-specific requirements in ADR 0012 and
  ADR 0016. Their OIDC, explicit-linking, invitation, session, and authority
  boundaries remain accepted.
- A future MFA or passkey requirement needs a new owner decision and a recovery
  design that does not block ordinary SaaS operation.
- ADR 0019 supplies local verified-email and managed-recovery operation. Hosted
  SMTP, HTTPS and secrets, logout, emergency access, and a real customer
  exercise remain release gates.
- No authentication event authorizes agent steering or creates an agent run.
