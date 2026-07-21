# F1 architecture overview

## Runtime topology

```mermaid
flowchart LR
    Browser["Customer app and Owner Console"] -->|Cookie, CSRF, HTTPS in cloud| API["ASP.NET Core API"]
    Worker["Python agent and Temporal worker"] -->|Internal bearer credential| API
    API -->|Tenant context and RLS| PG[(PostgreSQL)]
    API -->|Quarantine and private objects| S3[(MinIO or S3)]
    API -->|Digital PDF pages| Extractor["PdfPig plus deterministic rules"]
    Extractor -->|Pages, requirements, citations| PG
    Worker -->|Durable workflows| Temporal[(Temporal)]
    API -->|Outbox claim and workflow state| Temporal
    API --> Gateway["Tool Gateway and deterministic policy"]
    Gateway --> Drafts["Internal tasks and draft artifacts"]
    Gateway --> Sandbox["Generated engineering worktree"]
    Gateway -. "Approval required, adapter disabled" .-> External["External systems"]
```

## Trust boundaries

1. The browser has customer or platform-owner authority, never internal-service authority.
2. The Python worker has only an internal service credential and cannot make owner decisions or connect directly to application PostgreSQL.
3. PostgreSQL is authoritative. Tenant-owned rows use transaction-scoped organization context and row-level security.
4. Model output is untrusted structured input. It is validated by Pydantic, constrained by role-specific tool permissions, and materialized only through Tool Gateway.
5. Policy evaluation is deterministic .NET code. An LLM never decides authorization.
6. F1 extraction runs inside the API boundary. Raw PDF bytes and extracted page text are not sent to a model by default.
7. External adapters are registered for truthful policy and approval behavior but technically disabled in F1.
8. Engineering changes are restricted to a generated fixture worktree, exact command arrays without network targets, bounded output, blocked secrets, and fail-closed child-process proxy settings.

## Durable data flow

```mermaid
sequenceDiagram
    participant UI as Web UI
    participant API as ASP.NET Core
    participant DB as PostgreSQL
    participant W as Python worker
    participant T as Temporal
    UI->>API: Authenticated command plus CSRF and idempotency key
    API->>DB: Domain update plus outbox event
    W->>API: Claim event
    W->>T: Start versioned workflow
    T->>W: Run deterministic or opted-in live activity
    W->>API: Tool Gateway request
    API->>DB: Policy result, tool call, artifact or approval, audit event
    W->>API: Complete or fail agent run
    API->>DB: Persist usage and final task state
    UI->>API: Query owner-visible state
```

The customer app never exposes internal agents, approvals, prompts, traces, or unrelated internal tasks. The Owner Console shows concise rationale and exact action payloads, not chain-of-thought.

## F1 extraction flow

```mermaid
sequenceDiagram
    participant UI as Customer UI
    participant API as ASP.NET Core
    participant S3 as MinIO
    participant DB as PostgreSQL
    participant W as Python worker
    participant T as Temporal
    UI->>API: Upload and submit PDF
    API->>S3: Store quarantined object with SHA-256
    API->>DB: Commit analysis.submitted.v1
    W->>API: Claim submitted event
    W->>T: Start AnalysisIntakeWorkflow
    T->>W: Run extraction activity
    W->>API: Authenticated internal extract command
    API->>S3: Read and verify source object
    API->>API: Extract pages and detect requirements
    API->>DB: Store pages, requirements, citations, metrics, and audit
    W->>API: Create manual-review task and mark requires_review
    UI->>API: Read sourced draft requirements
```
