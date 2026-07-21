# Agent worker runbook

## Modes

`BIDMATRIX_AGENT_MODE=deterministic` is the default and requires no model credential. It loads versioned fixtures, validates strict Pydantic outputs, and is the only mode used by automated F0 gates.

`BIDMATRIX_AGENT_MODE=live` is opt-in. It requires `OPENAI_API_KEY` and explicit model values for Executive, Support, Product Analyst, and Engineering roles. Missing configuration prevents worker startup. Live mode still has no direct database access and no model-defined tools.

## Normal event flow

1. The API commits a versioned outbox event with the domain change.
2. The worker claims the event through authenticated internal API.
3. The worker starts the role-specific Temporal workflow with a stable workflow ID.
4. Activities prepare the agent run, validate structured output, materialize tool calls, and complete or fail the task.
5. Retried tool calls reuse their effect idempotency keys.

## Diagnosis

```powershell
docker compose ps
docker compose logs --since 10m agent-worker
docker compose logs --since 10m api
```

Inspect workflow state at <http://localhost:8233> and Owner Console at <http://localhost:3000/owner/runs>. Never mark a task completed if structured validation or a mandatory Tool Gateway action failed.

## Recovery

- A retryable internal API or Temporal transport failure is retried by the workflow activity policy.
- A schema or permission failure is non-retryable and produces a failed run and task with concise failure fields.
- An outbox lease eventually becomes reclaimable. Duplicate workflow start or tool materialization is safe through stable IDs and idempotency.
- Restore a disabled kill switch only through Owner Console after the underlying problem is understood and recorded.

Do not bypass the internal API, edit task status directly, or reclassify a policy denial as permission to use another tool.
