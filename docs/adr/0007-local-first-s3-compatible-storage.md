# ADR 0007: Use local-first S3-compatible object storage

- Status: Accepted
- Date: 2026-07-20

## Context

Uploaded RFP documents require private, tenant-scoped storage and a quarantine boundary. The implementation must work locally while remaining portable to a managed cloud service.

## Decision

Use an S3-compatible storage interface with MinIO in local development. Uploads first enter a dedicated quarantine bucket under server-generated, tenant-scoped object keys. Application code depends on a storage abstraction rather than MinIO-specific behavior.

## Consequences

- Local development does not require a cloud account.
- Quarantine and private storage use distinct buckets and access policies.
- Object keys, content hashes, validation results, and scan state are stored as metadata.
- Production storage can change without redesigning domain boundaries.
