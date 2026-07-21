# F0 incident runbook

F0 does not send external security notices. The owner remains responsible for investigation, legal advice, customer communication, and regulatory decisions.

## Immediate containment

1. Record UTC time, affected service, request or trace IDs, and the observed behavior.
2. In Owner Console controls, disable `allAgentsEnabled`. Disable `engineeringWritesEnabled` when repository activity is involved.
3. Preserve PostgreSQL, object-storage, Temporal, and audit data. Do not delete volumes.
4. Stop only the affected application service if continued activity increases harm.
5. Rotate the internal service credential, owner credential, storage keys, and database passwords when compromise is plausible.

## Evidence

- Export relevant append-only audit entries and verify chain-link status.
- Record tool calls, normalized input hash, policy version, approval state, execution state, task, workflow, and agent run.
- Record object key and SHA-256 without exposing a signed URL or secret.
- Retain container logs, but redact credentials, cookies, authorization headers, and customer document content.

## Recovery gate

Before re-enabling a control, identify root cause, add or update a regression test, run the full release gate, and document residual risk. External notice, permanent deletion, deployment, and spending remain unavailable F0 actions even during an incident.
