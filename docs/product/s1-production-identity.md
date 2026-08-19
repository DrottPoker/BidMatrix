# S1.1 production identity

## Objective

S1.1 makes self-service identities and sessions operable. Local implementation is a prerequisite for hosted identity, not a production claim.

## Bounded gates

| Gate | Outcome | State |
| --- | --- | --- |
| S1.1a Session control | Every application cookie is backed by a bounded server session that users can inspect and revoke. | Implemented and verified locally |
| S1.1b Credential recovery | Password rotation, single-use Development recovery, session invalidation, and owner bootstrap are proven. | Implemented and verified locally |
| S1.1c Hosted identity | Better Auth registration, OIDC, verified email, recovery, rate limits, session revocation, and logout are proven in the hosted environment. | Active - complete email/password flow passes locally against Neon development; hosted delivery pending and social providers deferred |

## Identity contract

1. Better Auth provides verified email/password. Google and GitHub remain optional configuration paths and are deferred from the current customer experience.
2. BidMatrix owns application users, private workspaces, memberships, platform roles, server sessions, and audit.
3. A new managed subject creates one private workspace and fixed `owner` membership. It cannot choose an existing workspace or receive `platform_owner`.
4. A different provider subject cannot claim an email already registered to another BidMatrix identity.
5. Existing-account linking starts from a recent BidMatrix session and requires verified email equality.
6. BidMatrix stores `SHA-256(issuer + newline + subject)`, not the raw provider subject or provider token.
7. A self-service account receives a disabled native BidMatrix credential.
8. OIDC uses backend authorization code, PKCE, nonce, correlation protection, bounded authentication age, and no saved provider token.
9. Every BidMatrix cookie is backed by a random server session whose token is stored only as a hash.
10. Session validation checks user state, security stamp, revocation, idle expiry, and absolute expiry on every request.
11. Logout revokes the current server session before clearing the cookie, then deletes the same-origin Better Auth session.
12. Password change and recovery rotate the security stamp and revoke all existing sessions.
13. Outside Development, native login and recovery require a bounded transition decision and remain hidden in managed-only mode.
14. Every email/password registration requires verification. Development captures mail in Mailpit; hosted configuration requires HTTPS plus authenticated TLS SMTP.
15. Registration creates no agent task, run, schedule, model call, Tool Gateway action, or external effect.
16. Reset requests are enumeration-safe, tokens expire after 30 minutes and are single-use, and successful reset revokes Better Auth plus mapped BidMatrix sessions.
17. Better Auth rate limits are database-backed, with narrower limits for sign-up, sign-in, verification resend, and reset requests.

## API surface

| Method | Path | Authority | Purpose |
| --- | --- | --- | --- |
| `GET` | `/v1/auth/configuration` | Anonymous | Report enabled managed and native sign-in surfaces without secrets. |
| `GET` | `/v1/auth/oidc/login` | Anonymous | Start login. A new valid subject receives a private workspace. |
| `GET` | `/v1/auth/oidc/link` | Recently authenticated user | Start explicit linking for an existing account. |
| `GET` | `/v1/auth/oidc/complete` | Protected OIDC state | Resolve, register, or explicitly link the validated identity and issue a session. |
| `GET` | `/v1/auth/sessions` | Authenticated user | List owned sessions. |
| `POST` | `/v1/auth/sessions/{sessionId}/revoke` | Authenticated user and CSRF | Revoke one owned session. |
| `POST` | `/v1/auth/sessions/revoke-others` | Authenticated user and CSRF | Revoke all other sessions. |
| `GET` | `/v1/auth/federated-identities` | Authenticated user | List safe provider-mapping metadata. |
| `POST` | `/v1/auth/federated-identities/{identityId}/revoke` | Recently authenticated user and CSRF | Revoke a mapping and its sessions. |

Development-only native password, account-recovery, and owner-recovery endpoints remain documented in `docs/operations/identity-bootstrap-and-recovery.md`.

## Verification gate

Automated tests prove session lifecycle, ownership isolation, password and recovery invalidation, explicit existing-account linking, raw-subject exclusion, role-based owner authorization, registration ownership, duplicate-email denial, auth-role isolation, zero platform roles for self-service users, and zero agent runs from registration.

Hosted completion still requires:

- production HTTPS and a hosted secret manager;
- production verified-email and password-recovery delivery;
- BidMatrix and Better Auth session logout evidence;
- throttling, invalid callback, issuer, nonce, and stale-authentication evidence;
- emergency owner recovery and disabled native fallback; and
- a real customer registration and F2 workflow exercise.

## Current local evidence

On 2026-08-12 the web typecheck, lint, 19 Vitest tests, and production build passed. All 54 .NET integration and security tests passed against PostgreSQL 18, including exact proof that 7-character passwords are rejected, 8-character passwords are accepted, and managed password updates transactionally revoke mapped application sessions. Migration `0017_s1_managed_identity_recovery` was applied to the isolated Neon development branch and the Better Auth schema check passed. Live identity exercises proved SMTP verification and recovery delivery, HTTP 403 for unverified sign-in, fragment-only reset continuation, identity-session revocation, old-password rejection, and new-password login. Browser inspection found the ordinary registration and recovery states present with no console errors. Earlier browser evidence also proves private-workspace registration and corrected logout.

This local evidence does not claim a production SMTP provider, hosted recovery, Google, GitHub, or production readiness.
