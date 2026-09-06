# Implementation status

Last updated: 2026-08-12

## Release state

Hosted Concierge S1 is active on top of the verified S0 baseline. The active onboarding model is self-service under ADR 0018 and email identity operations follow ADR 0019. `/register` supports ordinary verified email/password registration. The first successful managed login creates a private BidMatrix workspace and an `owner` membership. Invitation pages, APIs, services, and active database objects are removed. Google and GitHub are deferred.

On 2026-08-19 the owner deferred hosted deployment and selected the intentionally small `docs/product/mvp-v1.md` product loop. The active local work is the source-linked report, requirements compliance matrix, CSV handoff, clear failure states, and representative quality evidence. Broader product features remain outside the first version.

Better Auth runs inside Next.js and uses the isolated Neon auth schema. BidMatrix remains authoritative for application users, organizations, memberships, platform roles, server sessions, and audit. Registration cannot grant `platform_owner` and creates no agent work.

This is not yet a production SaaS. Development captures verification and reset email in Mailpit while Better Auth stores identity data and rate limits on the isolated Neon development branch. Hosted configuration requires HTTPS plus authenticated TLS SMTP, but a production email provider and hosted application are not configured. Application hosting, secret management, emergency access, production database promotion, private storage operations, malware scanning, backups, restore evidence, observability, billing, and commercial validation remain open.

The Neon production branch is untouched. Managed database work remains isolated to the owner's BidMatrix development branch.

## Current SaaS outcome

| Capability | Status | Verified behavior |
| --- | --- | --- |
| Ordinary registration UI | Passed locally | `/register` exposes name, email, password with an 8-character minimum, and confirmation without invitation or deferred social-provider noise. |
| Verified email | Passed locally | Registration sends a one-hour verification link through SMTP and unverified password sign-in is rejected with HTTP 403. |
| Managed password recovery | Passed locally | `/forgot-password` returns a neutral result, a 30-minute single-use link reaches `/reset-password`, and the bearer is moved to and cleared from a browser fragment. |
| Identity rate limits | Implemented | Better Auth stores global and endpoint-specific sign-up, sign-in, verification, and recovery limits in the isolated Neon schema. |
| Reset session invalidation | Passed locally | A real Better Auth reset revoked the prior identity session and rejected the old password. Migration `0017` transactionally rotates mapped BidMatrix security stamps and revokes application sessions. |
| Private workspace provisioning | Passed locally | A new managed identity atomically creates one active organization, one active user, one `owner` membership, and one federated mapping. |
| Authority boundary | Passed locally | A self-service account receives no platform role, cannot choose a role or existing tenant, and does not receive a native BidMatrix password. |
| Agent freeze | Passed locally | Registration creates no agent task, run, schedule, model call, Tool Gateway call, or external effect. |
| Duplicate ownership protection | Passed locally | A different provider subject cannot claim an email already registered to another BidMatrix identity. |
| Invitation removal | Implemented | `/join`, `/owner/tenants`, invitation APIs, invitation services, and the invitation table are removed by migration `0016_s1_self_service_registration`. |
| Better Auth OIDC | Implemented locally | Discovery, ES256 JWKS, authorization code plus PKCE, confidential token exchange, claim validation, explicit existing-owner linking, and BidMatrix-owned authorization were previously verified against Neon development. |
| Session logout | Passed locally | The button revokes the BidMatrix server session, deletes the same-origin Better Auth session, and returns to the ordinary sign-in form without exposing an OIDC end-session error. |
| Hosted identity evidence | Incomplete | Production SMTP, hosted HTTPS and secrets, hosted callback/logout evidence, emergency access, and a real customer exercise remain open. Google/GitHub evidence is deferred. |

## Managed PostgreSQL outcome

| Capability | Status | Verified behavior |
| --- | --- | --- |
| Provider isolation | Provisioned for development | The Neon `BidMatrix` project is separate from the owner's other projects and runs PostgreSQL 18 in AWS Frankfurt. |
| Branch isolation | Passed | Managed database work targets development branch `br-muddy-hat-b2zy3lrf`; production remains untouched. |
| Runtime connections | Passed | Independent application, audit, and Better Auth roles connect with TLS verification and required channel binding. |
| Least privilege | Passed locally | Registration uses a narrow security-definer function. The password trigger has a fixed search path and cannot be called by the auth role. The application and auth roles cannot directly read each other's authority tables. |
| Secret handling | Implemented locally | Development credentials are stored outside Git in .NET User Secrets. |
| Production readiness | Not complete | Branch protection, hosted secrets, application deployment, network policy, backups, restore, monitoring, and production promotion remain open. |

## Product capability truth

| Capability | Current state | Honest outward claim |
| --- | --- | --- |
| PDF intake | Implemented locally | Digital English PDFs are validated, hashed, tenant-isolated, and processed locally. |
| Requirements and findings | Implemented prototype | Requirements, key dates, requested documents, and weighted criteria are presented after owner quality review with exact citations. |
| Compliance Matrix v1 | Implemented locally | Published requirements have search, obligation and category filters plus stable matrix numbering. |
| Customer CSV export | Implemented locally | The published requirements matrix exports source evidence, blank response columns, confidence, and review state with formula-prefix neutralization. |
| Owner review and publication | Implemented | Corrections and rejections are versioned, attributed, audited, and hidden until publication. |
| OCR and complex tables | Not implemented | Missing digital text remains explicit; no content is fabricated. |
| Company matching or bid decision | Not implemented | No supplier-fit score, recommendation, legal conclusion, or compliance decision is generated. |
| Self-service identity | Implemented locally | Verified email/password can create and recover a private workspace identity without an invitation. Production delivery evidence remains open and social providers are deferred. |
| Billing and usage enforcement | Not implemented | Pilot payment remains manual. |
| Cloud and production | Partially provisioned | Neon development is proven; the application and production operations are not deployed. |
| Agent steering | Frozen | No agent may set goals, priorities, schedules, product decisions, or external actions during the SaaS-first sequence. |

## Verification record

- Web: route generation, strict TypeScript, lint, 25 Vitest tests, the optimized Next.js production build, and browser inspection of registration and recovery pass locally.
- Live identity: SMTP verification and recovery delivery, unverified-account rejection, fragment-only reset continuation, single-use password reset, identity-session revocation, old-password rejection, and new-password login pass against Neon development plus local Mailpit.
- .NET: all 54 integration and security tests pass locally, including transactional mapped-session revocation from a Better Auth credential update.
- Database: a fresh PostgreSQL 18 test database applies all 17 migrations; registration ownership, role isolation, audit, rate-limit storage, password-reset revocation, duplicate-email denial, and zero agent runs are verified.
- Neon: migration `0017` is applied on the isolated development branch. It records 17 checksummed migrations, passes runtime-role access verification, and matches the configured Better Auth schema. The production branch remains untouched.
- CI: the workflow definition contains the language, dependency, database, and Compose gates. A remote GitHub Actions run is not claimed.

## Next SaaS gate

1. Select the production application host, SMTP provider, and secret manager.
2. Deploy under production HTTPS against a protected Neon environment and prove real verification and recovery delivery.
3. Verify callback origins, cookies, logout, throttling, emergency owner access, and one real customer registration.
4. Continue private document operations, backup and restore, observability, billing, and a real customer pilot.
5. Reconsider Google and GitHub only when the owner chooses to activate them.

No agent-steering phase begins until the working SaaS gate in ADR 0010 is satisfied and the owner records a new decision.
