# F0 capability boundaries

## Purpose

Foundation Release F0 proves that BidMatrix can store, coordinate, review, and audit work without pretending that real RFP intelligence already exists.

## Customer-visible scope

F0 may expose:

- organization-aware accounts and an owner role;
- PDF upload with metadata validation and SHA-256 hashing;
- tenant-scoped quarantine storage;
- analysis status and manual-review state;
- draft artifacts clearly labeled as drafts;
- an honest message that extraction is not implemented.

## Internal scope

F0 may provide:

- four versioned agent definitions with structured outputs;
- deterministic offline demonstrations;
- durable Temporal workflows;
- the Tool Gateway and deterministic policy decisions;
- payload-bound approvals and audit records;
- disabled adapters for every external side effect;
- owner controls and kill switches.
- fixture-only engineering worktrees, allowlisted validation commands, and reviewable diff artifacts.

## Explicit non-capabilities

F0 does not provide OCR, document layout extraction, requirement extraction, compliance matching, bid/no-bid scoring, legal analysis, billing, customer API keys, outbound email, publication, production deployment, remote Git actions, or autonomous spending.

The web application and documentation must not imply that any item in this section works. A stub, fixture, schema, or disabled adapter does not make a capability complete.
