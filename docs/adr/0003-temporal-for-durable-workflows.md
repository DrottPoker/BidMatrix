# ADR 0003: Use Temporal for durable workflows

- Status: Accepted
- Date: 2026-07-20

## Context

Document intake, manual review, approvals, and agent tasks can wait, retry, and continue across process restarts. Ad hoc background jobs would make durable state, idempotency, and operational visibility harder to guarantee.

## Decision

Use Temporal for long-running F0 workflows. Workflow code must remain deterministic, activities must define retry and timeout behavior, and application side effects must be idempotent.

## Consequences

- Workflow state survives worker restarts.
- Temporal owns orchestration state, not authoritative product data.
- Activities call authenticated internal APIs and never bypass the Tool Gateway for controlled effects.
- Retry behavior and duplicate-delivery handling require tests.
