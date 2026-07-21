# Implementation status

Last updated: 2026-07-21

## Release state

Extraction Prototype F1 is implemented and verified locally on top of the Foundation F0 control plane. F1 provides real, deterministic extraction from digital English PDFs with exact page citations and mandatory human review. No cloud or production deployment was performed.

The active application uses Next.js, TypeScript, and Tailwind CSS. Vite, a JavaScript-only conversion, and fake bid scoring are not part of the application.

## Foundation status

| Release | Status | Verified result |
| --- | --- | --- |
| F0 phases 0 through 9 | Complete | Local infrastructure, tenancy, authentication, approvals, four deterministic agents, audit, engineering sandbox, release controls, and end-to-end verification remain intact. |
| F1.1 schema and architecture | Complete | Migration `0008_f1_document_extraction` adds extraction state and tenant-isolated page storage; ADR 0009 records the local deterministic design. |
| F1.2 document extraction | Complete | Digital PDF text is read from MinIO, integrity checked, stored by page, hashed, classified, and reported with explicit `requires_ocr` and failure states. |
| F1.3 requirements and citations | Complete | Strict mandatory and optional review candidates include category, confidence, requested evidence, file identity, page number, and exact quote. |
| F1.4 workflow, API, and UI | Complete | Temporal runs extraction before manual review; typed customer and internal APIs expose F1 results; the analysis page presents metrics, document types, and citations. |
| F1.5 evaluation and quality | Complete | A checked-in synthetic evaluation set measures mandatory recall, classification, citation pages, and quote equality. |

## Capability truth table

| Capability | F1 state | Honest outward claim |
| --- | --- | --- |
| Customer and owner authentication | Implemented | Organization workspace and owner console are available locally. |
| PDF intake | Implemented | English PDF files are validated, hashed, quarantined, and integrity checked before extraction. |
| Digital PDF text extraction | Implemented prototype | Page-preserving local extraction works for digital text PDFs. |
| OCR and complex table reconstruction | Not implemented | Scanned or blank-text pages are marked `requires_ocr`; no content is fabricated. |
| Requirement detection | Implemented prototype | Deterministic mandatory and optional candidates are produced for human review. |
| Source citations | Implemented | Every generated requirement includes an exact file, page, and quote citation. |
| Company and evidence matching | Not implemented | No supplier capability or evidence conclusion is generated. |
| Compliance and bid/no-bid scoring | Not implemented | No score, recommendation, or legal conclusion is generated. |
| Four internal agents | Implemented in deterministic mode | Offline, structured, draft-only demonstrations remain available. |
| Tool Gateway and owner approvals | Implemented | Policy and payload-bound approval controls remain active. |
| External communication and spending | Disabled | Approval does not execute an external effect in F1. |
| Remote Git and deployment | Disabled | No push, pull request, merge, cloud, or production deployment occurs. |

## Current verification record

- Next.js and Tailwind CSS: lint, typecheck, four Vitest tests, and production build pass.
- .NET: Release build passes with zero warnings; 27 integration, security, policy, extraction, and evaluation tests pass.
- Python: Ruff and strict mypy pass; 12 pytest cases pass.
- F1 evaluation: five of five annotated mandatory requirements are recalled; all expected classifications, pages, and exact quotes match.
- Dependencies: npm production audit and the NuGet direct plus transitive vulnerability scan report no known vulnerabilities.
- Compose: configuration validates and all six long-running local services reach healthy state.
- Essential end to end: extraction reaches `succeeded`; analysis reaches `requires_review`; two mandatory and cited requirements are produced; no fixture page requires OCR or fails.
- F0 regression controls: four agent runs complete; the engineering sandbox is recorded; the audit chain is valid; draft-only and disabled-external controls remain active.

## Explicit post-F1 boundary

F1 is a controlled extraction prototype, not a production accuracy claim. OCR, handwriting recognition, robust table reconstruction, semantic or model-assisted extraction, company matching, evidence matching, correction capture, compliance scoring, bid/no-bid recommendations, legal conclusions, billing, live external actions, cloud deployment, and production operations require a separately approved phase.
