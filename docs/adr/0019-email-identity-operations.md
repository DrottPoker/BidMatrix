# ADR 0019: Operate email identity before social providers

## Status

Accepted on 2026-08-12.

## Context

ADR 0018 made account creation self-service, but Development still bypassed
email verification and no customer-managed password recovery existed in Better
Auth. The owner has selected ordinary email and password as the only required
identity method for the next SaaS gate. Google and GitHub remain optional and
are explicitly deferred.

Better Auth and BidMatrix own different sessions. Resetting a Better Auth
password must therefore invalidate both the identity-provider session and every
mapped BidMatrix application session without granting the Better Auth role
general access to BidMatrix authority tables.

## Decision

BidMatrix sends verification and reset messages through a server-side SMTP
transport. Email/password accounts remain unverified until the user follows the
one-hour verification link. Password-reset requests return the same response
for known and unknown addresses, use a 30-minute single-use token, and move the
token into a browser fragment before rendering the password form.

Better Auth stores rate limits in its isolated Neon schema. Sign-up, sign-in,
verification resend, and reset-request endpoints have narrower limits than the
global identity boundary. Hosted configuration requires HTTPS, authenticated
SMTP, and TLS. Development uses Mailpit so the complete flow can be exercised
without sending real email.

Migration `0017_s1_managed_identity_recovery` adds the Better Auth rate-limit
table and an after-update trigger on credential passwords. The trigger is a
fixed-search-path security-definer boundary. It matches only an existing
federated identity hash, rotates the BidMatrix security stamp, revokes active
application sessions and pending native recovery links, and appends a token-free
audit event in the same database transaction. The Better Auth role cannot call
the trigger function or read BidMatrix authority tables directly.

Google and GitHub remain supported by configuration but are not rendered or
required in the current customer experience. Activating either provider needs a
later owner decision and separate callback evidence.

## Consequences

- A customer can create, verify, recover, and reuse an email/password account.
- An unverified account cannot sign in.
- Password recovery revokes Better Auth sessions and mapped BidMatrix sessions.
- Local Mailpit evidence does not qualify a production SMTP provider.
- S1.1 still requires hosted HTTPS, secret management, production email
  delivery, emergency access, and a real customer exercise.
- No agent authority, schedule, goal, task, model call, or external action is
  added by this decision.
