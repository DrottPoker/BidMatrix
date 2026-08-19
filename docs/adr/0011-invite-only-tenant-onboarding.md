# ADR 0011: Invite-only tenant onboarding foundation

- Status: Superseded by ADR 0018 on 2026-08-12
- Date: 2026-08-11

## Context

The local product already authenticates provisioned users with secure cookies, password hashing, lockout, security-stamp validation, CSRF protection, organization claims, and PostgreSQL row-level security. Provisioning is the missing entry path. The only current account is synchronized by a Development-only hosted service, so it cannot support an invited hosted pilot.

Selecting a hosted identity provider, recovery channel, or MFA product is a separate production decision. BidMatrix still needs a provider-neutral tenant and invitation lifecycle that can survive that later choice.

## Decision

S1 begins with an invite-only concierge onboarding boundary.

An authenticated platform owner creates a new organization and its initial `owner` invitation in one operation. BidMatrix generates a 256-bit random bearer token, stores only a SHA-256 hash, returns the raw token once, and defaults to a seven-day expiry. The invitation can be listed without token material and revoked while pending.

The invitation link places the token in the browser fragment. Anonymous inspection and acceptance use CSRF-protected, rate-limited POST bodies. Acceptance atomically creates a previously unregistered user, a password credential, and the organization-owner membership, consumes the invitation, appends an audit event, and establishes the existing BidMatrix cookie session.

ADR 0014 later extends the same authoritative invitation lifecycle with managed-provider acceptance. It replaces the enabled password credential with an explicit provider mapping for that path while preserving the fixed owner role, single-use state, atomicity, and BidMatrix session boundary.

S1.0 accepts only unregistered email addresses. Reusing one account across organizations remains unavailable until BidMatrix has an explicit organization-selection and session-context design. The initial invitation role is fixed to `owner`; arbitrary role assignment is not part of tenant creation.

Invitation creation, revocation, and acceptance run without models, agents, schedules, Tool Gateway calls, email connectors, or external side effects. Manual delivery of the returned link is an explicit concierge operation.

## Consequences

- A real tenant has a controlled entry path without Development bootstrap credentials.
- Token disclosure is bounded to the create response and the manually delivered browser link.
- Database and audit records remain useful if the credential implementation later moves to an external identity provider.
- A leaked unused link remains a bearer-secret risk, so expiry, revocation, careful delivery, and later recovery and MFA work are mandatory.
- No hosted production claim is allowed until S1.1 through S1.5 are verified.
- ADR 0010 remains unchanged and agent steering stays frozen.
