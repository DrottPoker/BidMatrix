# ADR 0009: Local deterministic PDF extraction behind the API boundary

## Status

Accepted on 2026-07-21.

## Context

F1 must extract digital PDF text, preserve pages, classify procurement documents, create a strict requirement schema, attach reliable citations, and expose measurable misses. The Python worker must not read PostgreSQL or object storage directly, and uploaded content must not be sent to a model by default.

## Decision

- ASP.NET Core owns F1 extraction because it already owns tenant context, PostgreSQL, MinIO access, audit, and analysis state.
- Temporal remains the durable orchestrator. The Python worker invokes one authenticated, idempotent internal extraction operation after the analysis enters `processing`.
- `PdfPig` 0.1.15 extracts text from digital PDFs page by page.
- Extracted page text is stored in `analysis_pages` with file identity, page number, extraction method, and SHA-256 hash.
- Deterministic English rules classify documents and produce review candidates for mandatory and optional requirements.
- Every persisted requirement keeps one or more exact file and page citations.
- Blank digital text produces `requires_ocr`. Parser or integrity failures produce an explicit failed-file record. Neither condition fabricates requirements.
- Every F1 result remains `pending` and the analysis remains `requires_review`.

## Consequences

- F1 works locally without a model key and is reproducible in tests.
- Source traceability is available before later semantic or model-assisted extraction is introduced.
- Rules are intentionally limited and measured against a checked-in evaluation set.
- Scanned PDFs, tables with complex layout, OCR, company matching, correction capture, scoring, and legal conclusions remain outside F1.
