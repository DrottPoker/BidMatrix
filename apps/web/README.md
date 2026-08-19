# BidMatrix web

This package contains the Next.js App Router and Tailwind CSS interface for BidMatrix Concierge Pilot F2 and Hosted Concierge S1.

Run commands from the repository root:

```powershell
npm run dev
npm run lint
npm run typecheck
npm run test
npm run build
```

The customer interface must follow `docs/product/f2-capability-boundaries.md`. It may present owner-published, source-linked extraction results, but must not imply that OCR, production-grade accuracy, company matching, compliance scoring, bid or no-bid recommendations, legal conclusions, self-service billing, or production hosting exist.

`/register` supports ordinary name, email, and password registration. A customer verifies the address before signing in. A successful Better Auth identity continues through managed OIDC, where BidMatrix atomically creates a private workspace and its `owner` membership. Registration never grants platform roles or starts an agent. Google and GitHub remain configuration-supported but are deferred and not rendered in the current experience.

Better Auth is mounted at `/api/auth/[...all]` and stores identity-provider records in the isolated Neon `better_auth` schema. OAuth token material is encrypted. Session tokens, provider tokens, raw provider subjects, recovery values, and recovery hashes must never appear in rendered lists, browser storage, analytics, logs, or server-rendered props.

Development captures verification and password-reset email in Mailpit at `http://localhost:8025`. Better Auth blocks unverified sign-in, stores database-backed rate limits, returns neutral recovery responses, consumes reset links once, and revokes identity sessions after reset. Hosted startup fails closed without HTTPS plus authenticated TLS SMTP. The native BidMatrix password and recovery UI remains a Development and transition boundary, not the customer-facing identity path.

S1.1 also provides `/forgot-password` and `/reset-password` for managed recovery, `/app/account` for server-session, password, and managed-identity control, `/owner/access` for recently authenticated owner-mediated native recovery, and `/recover` for one-time native credential reset. Recovery tokens are fragment-only and must be removed from the address bar before inspection.

Better Auth operator commands run from `apps/web` after `scripts/web/Import-BetterAuthUserSecrets.ps1` loads development configuration without printing it:

```powershell
npm run auth:schema:check
npm run auth:bootstrap-owner
npm run auth:bootstrap-client
npm run auth:rotate-signing-key
```

Bootstrap commands are one-shot. Signing-key and client rotation are explicit operational actions, not normal application startup steps.

Agent prompts, runs, tools, and approvals are internal control-plane surfaces. No agent may steer the customer product or company operations during the SaaS-first sequence defined in `docs/product/s1-hosted-concierge.md` and ADR 0010.
