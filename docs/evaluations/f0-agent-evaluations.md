# F0 deterministic agent evaluations

Automated tests use versioned JSON fixtures in `tests/fixtures/agents`. They never require a live model or network call.

## Shared gates

- Input and output validate against strict Pydantic contracts with unknown fields rejected.
- The same fixture produces the same output.
- Every proposed action exists in the active role permission list.
- Materialization occurs only through Tool Gateway.
- Invalid structured output fails the task and cannot produce a completed run.

## Role cases

| Role | Required evidence |
| --- | --- |
| Executive | Reports supplied task and backlog values, does not invent history, proposes bounded internal work, and requests an owner target decision. |
| Support | Uses approved fact source IDs, escalates missing evidence and restricted requests, ignores customer prompt injection, creates a draft only, and never claims human review. |
| Product Analyst | Separates observation from hypothesis, reports the fixture sample limitation, proposes a measurable experiment with guardrails and rollback, and does not change price. |
| Engineering | Creates the generated worktree, changes only fixture README, rejects traversal and secret access, runs only `git diff --check`, creates a hashed diff artifact, and performs no remote action. |

`src/agents/tests/test_agent_runtime.py` covers deterministic schemas and prompt injection. Backend integration tests cover Tool Gateway permission, policy, persistence, approval, and engineering filesystem boundaries. `scripts/verify-f0.ps1` covers all four workflows against the running Compose stack.

Live-model evaluation, quality scoring, model comparison, and production thresholds are post-F0 work.
