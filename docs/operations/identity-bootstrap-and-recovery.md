# Identity bootstrap and recovery

## Purpose and boundary

This runbook operates the provider-neutral S1.1 identity controls. It covers the one-shot initial platform-owner bootstrap, server-session response, and manual concierge recovery.

These controls are suitable for local verification and a controlled transition only. Managed OIDC and self-service registration do not replace Better Auth recovery and verified-email delivery, configured Google and GitHub callbacks, logout, hosted callback, real customer registration, and emergency-access evidence required before a hosted pilot. ADR 0017 supersedes the earlier passkey-specific owner requirement, and ADR 0018 supersedes invitation onboarding. Use `docs/operations/managed-oidc.md` for the current boundary.

No identity operation may create an agent task, run, model request, schedule, Tool Gateway call, or external action.

## Required configuration

Inject these values from the deployment secret manager or a short-lived release-job environment. Do not put production values in a repository file, command argument, shell history, ticket, chat, or log.

| Key | Requirement |
| --- | --- |
| `OWNER_BOOTSTRAP_EMAIL` | Valid, previously unregistered owner email. |
| `OWNER_BOOTSTRAP_PASSWORD` | Unique 15 to 128 character initial password without control characters. |
| `OWNER_BOOTSTRAP_DISPLAY_NAME` | Optional display name, at most 120 characters. |
| `OWNER_BOOTSTRAP_ORGANIZATION_NAME` | Initial organization name, 2 to 120 characters. |
| `OWNER_BOOTSTRAP_ORGANIZATION_SLUG` | Unique 3 to 63 character lowercase slug using letters, numbers, and single hyphens. |

The command also requires the migration database credential because it applies pending migrations before creating the owner. The normal API process must continue to use only the restricted application database role.

## One-shot platform-owner bootstrap

1. Confirm that this is an initial deployment with no existing `platform_owner` and that the target database and release artifact are exact.
2. Keep the public API stopped or unavailable while the first identity is created.
3. Inject the five bootstrap settings through the release environment and run the published API artifact once:

   ```powershell
   dotnet BidMatrix.Api.dll --bootstrap-owner
   ```

   When the verified Compose image is the release artifact and its database is already healthy, the equivalent bounded job is:

   ```powershell
   $projectName = 'bidmatrix-hosted'
   docker compose --project-name $projectName run --rm --no-deps api --bootstrap-owner
   ```

4. Require exit code 0. Record the release identifier, operator, target environment, time, and the existence of the `identity.owner.bootstrapped` audit event. Do not record credential or token material.
5. Remove the bootstrap password from the release environment, start the normal API under the restricted application role, and sign in through the application UI.
6. Change the initial password immediately through `/app/account`. This rotates the security stamp and revokes the bootstrap session, so sign in again with the new credential.
7. Verify that the new session appears in `/app/account` and that the audit chain remains valid.

The command takes a PostgreSQL advisory transaction lock and fails closed if any platform owner already exists, or if the selected email or organization slug is already registered. It never synchronizes or overwrites an existing production owner. A failed command must be investigated, not bypassed with direct SQL.

Development startup is intentionally different: it synchronizes the fixed Development owner for repeatable local stacks. Never run an API with `Development` environment behavior in a hosted deployment.

## Manual concierge recovery

1. Verify the account owner outside BidMatrix using an established organization contact and a trusted channel. A request arriving only from the locked account is insufficient evidence.
2. Sign in as a `platform_owner`. If the session is older than the configured recent-authentication window, sign in again before proceeding.
3. Open `/owner/access`, enter the verified account email, and create one recovery link.
4. Copy the link from the one-time response and deliver it only through the verified trusted channel. Treat the complete unused link as a temporary bearer secret.
5. The recipient opens `/recover`. The browser removes the fragment from the address bar before sending the token in a CSRF-protected request body.
6. The recipient sets a new password. Successful recovery consumes the token, rotates the security stamp, clears lockout, revokes every existing session, and requires a fresh sign-in.
7. Confirm the recovery audit event and valid audit chain. If the link is not used, return to `/owner/access` and revoke it.

Never ask the recipient to paste the recovery link into a ticket, email thread, support chat, analytics event, or diagnostic log. BidMatrix lists only recovery metadata and never returns the token or its hash after creation.

## Session or credential incident

- For one lost browser, use `/app/account` to revoke that session.
- For uncertain session exposure, use `Close other sessions` from a trusted current browser.
- For possible credential exposure, change the password. All sessions, including the current one, are revoked.
- If the user cannot authenticate, follow the verified manual recovery procedure above. Do not create a second account or bypass tenant membership.
- If platform-owner access itself is unavailable, stop and use the hosted identity provider's documented emergency access process once S1.1c exists. The one-shot bootstrap must not be rerun and direct credential SQL is forbidden.

## Evidence checklist

- Exact deployment and database target recorded.
- Bootstrap or recovery performed by an authorized human operator.
- No secret value captured in logs, shell history, tickets, chat, analytics, or audit metadata.
- Expected identity audit event present and audit chain valid.
- Old sessions rejected after password change or recovery.
- New sign-in succeeds and shows one server-validated current session.
- No agent run, schedule, model request, or external tool action created.
