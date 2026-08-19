# Hosted Concierge S1

## Objective

S1 turns the verified local concierge product into an owner-usable hosted SaaS without expanding agent authority. The phase is complete only when a real customer can create an account, sign in, submit an RFP, receive an owner-reviewed result, and rely on documented security and recovery operations in production.

S1 is split into independently verifiable gates. A completed code slice is not a production claim.

## Phase gates

| Gate | Outcome | State |
| --- | --- | --- |
| S1.0 Self-service account foundation | Email/password and configured social identities can create a private workspace with an owner membership. | Implemented and verified locally |
| S1.1 Production identity | Hosted identity, verified email, account recovery, session revocation, and logout are proven. | Active - email/password operations pass locally against Neon development; hosted delivery pending and social providers deferred |
| S1.2 Secure hosted runtime | TLS, secret management, controlled migrations, private networking, and least-privilege service identities are deployed. | Active - isolated Neon development project, TLS, controlled migrations, and database roles proven; hosted runtime pending |
| S1.3 Private document pipeline | Private object storage, retention controls, real malware scanning, and quarantine failure handling are proven. | Not started |
| S1.4 Recovery and observability | Encrypted backups, a successful restore drill, alerts, logs, metrics, traces, and incident runbooks are verified. | Not started |
| S1.5 Pilot operation | A customer completes the human-reviewed F2 workflow in the hosted environment with support and quality evidence. | Not started |

## Self-service registration contract

1. `/register` accepts name, email, password, and password confirmation without an invitation.
2. Google and GitHub remain optional configuration paths but are deferred and not rendered in the current customer experience.
3. Better Auth establishes the managed identity. BidMatrix does not infer application authority from email alone.
4. The first successful managed login atomically creates one active BidMatrix user, one private organization, one `owner` membership, one federated identity mapping, and one audit event.
5. Registration creates no `platform_owner` role, agent goal, task, run, schedule, model request, Tool Gateway action, or external effect.
6. The native BidMatrix password is disabled for self-service accounts. The managed identity remains the account's authentication method.
7. A second provider subject cannot claim an email already registered to another BidMatrix identity.
8. Email/password accounts must verify through SMTP before sign-in. Development captures delivery in Mailpit; hosted configuration fails closed without HTTPS plus authenticated TLS SMTP.
9. Provider tokens and raw provider subjects never enter BidMatrix browser storage, application logs, or role records.
10. Password reset is enumeration-safe and single-use, and revokes both Better Auth sessions and mapped BidMatrix application sessions.

## Verification gate

Automated tests must prove:

- ordinary registration fields are present without invitation or deferred social-provider noise;
- the invitation and join surfaces are absent;
- a new managed identity creates exactly one private organization and owner membership;
- the user receives no platform role and registration creates no agent run;
- the application role can execute only the narrow registration function and cannot read identity authority tables directly;
- duplicate email and provider-subject races fail closed;
- all tenant-isolation, publication, audit-chain, session, recovery, and frozen-agent tests still pass.

Hosted completion additionally requires production HTTPS, a hosted secret manager, production verified-email and recovery delivery, hosted logout, rate-limit evidence, emergency owner recovery, backup and restore proof, and a real customer exercise. Google and GitHub callback evidence is required only if the owner later activates those methods.

## Identity authority

Better Auth owns identity-provider records and the OIDC ceremony. BidMatrix owns application users, organizations, memberships, platform roles, server sessions, and audit events. A provider callback can trigger the narrow registration transaction, but it cannot choose a role, join an existing organization, or grant platform authority.

ADR 0012 governs managed OIDC, ADR 0013 explicit linking, ADR 0015 Neon PostgreSQL, ADR 0016 self-hosted Better Auth, ADR 0017 simplified sign-in, ADR 0018 self-service registration, and ADR 0019 email identity operations. ADR 0018 supersedes the invitation onboarding decisions in ADR 0011 and ADR 0014.

## Agent freeze

ADR 0010 remains fully active throughout S1. Registration may not create an agent goal, task, run, schedule, model request, policy change, or external tool call. The customer owns submissions, and the platform owner remains the publication reviewer during the concierge phase.
