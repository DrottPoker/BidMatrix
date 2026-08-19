# ADR 0018: Self-service account registration

- Status: Accepted
- Date: 2026-08-12
- Supersedes: ADR 0011 and ADR 0014

## Context

BidMatrix needs to become an owner-usable SaaS before any agent receives product or company authority. Invitation-only onboarding adds an operator dependency and prevents ordinary customers from creating accounts. The selected identity experience is email and password with Google and GitHub as optional alternatives.

## Decision

BidMatrix removes the tenant-invitation product, API, web, and database surfaces.

`/register` creates a Better Auth identity without an invitation. On the first successful managed OIDC login, BidMatrix atomically creates a private organization, the application user, a fixed `owner` membership, a federated identity mapping, a disabled native credential, and an audit event.

Better Auth establishes identity only. BidMatrix remains authoritative for organizations, memberships, platform roles, application sessions, and audit. Self-service registration cannot select a role, join an existing organization, grant `platform_owner`, or invoke an agent.

ADR 0019 supersedes the temporary Development auto-verification allowance. Every email/password account now requires the emailed verification link. Development captures messages in Mailpit. Every hosted environment fails closed without HTTPS and authenticated TLS SMTP. Google and GitHub remain deferred.

Migration `0016_s1_self_service_registration` refuses to drop invitation storage when any invitation rows remain. This prevents silent loss of pending onboarding state. When the table is empty, the migration removes the invitation functions and table and grants only the application role access to the narrow registration function.

## Consequences

- Every new account starts in a separate private workspace.
- Multi-user organizations and organization switching require a later explicit membership design.
- Existing emails cannot be claimed by a different provider subject.
- Production SMTP delivery, hosted HTTPS and secrets, logout, emergency access, and a real customer exercise remain production gates. Social-provider activation is deferred.
- Historical invitation migrations remain in the append-only migration chain, but active invitation surfaces no longer exist.
- No agent authority changes.
