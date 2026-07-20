# ADR 0005: Keep F0 external autonomy draft-only

- Status: Accepted
- Date: 2026-07-20

## Context

F0 must demonstrate useful agent workflows without creating uncontrolled legal, financial, reputational, or production effects.

## Decision

Agents may analyze internal data, create internal work, draft artifacts, propose actions, and request owner approval. External effects including email, publication, spending, deployment, remote Git operations, refunds, and contractual actions remain disabled or represented by non-executing adapters.

## Consequences

- The interface must label drafts and disabled capabilities honestly.
- Approval does not make an unavailable F0 adapter operational.
- External-action demonstrations end with an auditable proposal or draft.
- Later autonomy changes require a reviewed policy and architecture decision.
