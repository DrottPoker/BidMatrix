# ADR 0004: Make the Tool Gateway the only agent side-effect boundary

- Status: Accepted
- Date: 2026-07-20

## Context

Model output is untrusted and cannot itself authorize a write or external action. Direct access from agents to databases, credentials, shells, or external adapters would bypass deterministic policy and audit controls.

## Decision

All agent tool requests pass through the ASP.NET Core Tool Gateway. The gateway validates the schema, identity, agent permissions, system controls, policy result, approval state, idempotency key, and exact normalized payload before execution.

## Consequences

- No agent receives direct infrastructure or external-service credentials.
- Every tool decision and execution is auditable.
- Disabled tools return an explicit disabled result and never report success.
- Payload normalization and hashing become security-critical contracts.
