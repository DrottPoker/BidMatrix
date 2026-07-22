# Implementation status

Last updated: 2026-07-22

## Release state

The local Concierge Pilot F2 product is implemented on the F0 control plane and F1 extraction foundation. It provides a focused customer workflow, four source-linked result types, internal correction capture, and an explicit owner publication gate.

F2 is technically pilot-ready, not commercially validated. No cloud or production deployment was performed, and the paid-pilot release gate still requires real customer evidence.

The active application uses Next.js 16, TypeScript, and Tailwind CSS 4. Vite, JavaScript-only conversion, customer-facing agents, supplier matching, and bid scoring are not part of the customer product.

## F2 implementation

| Capability | Status | Verified behavior |
| --- | --- | --- |
| Customer experience | Implemented | Responsive landing page, sign-in, sidebar and mobile navigation, dashboard, analysis list, guided upload, report, and account view. |
| F2 extraction | Implemented | Requirements, key dates, requested documents, and weighted evaluation criteria retain exact file, page, section, quote, confidence, and review state. |
| Owner quality review | Implemented | Owner can accept, correct, or reject requirements and findings with optimistic concurrency and correction notes. |
| Publication gate | Implemented | Customer result collections remain hidden until explicit owner publication; rejected findings remain hidden after publication. |
| Traceability | Implemented | Original extraction is retained, corrections are counted, review identity and time are stored, and mutations append audit events. |
| Pilot operations | Partially complete | Processing duration and correction count are available; actual paid-pilot evidence must be collected outside automated tests. |

## Capability truth table

| Capability | F2 state | Honest outward claim |
| --- | --- | --- |
| PDF intake | Implemented | Digital English PDFs are validated, hashed, tenant-isolated, and processed locally. |
| Requirements | Implemented prototype | Mandatory and optional candidates are presented after quality review. |
| Key dates | Implemented prototype | Recognized English procurement dates are shown with exact source citations. |
| Requested documents | Implemented prototype | Recognized submission materials are shown with exact source citations. |
| Evaluation criteria | Implemented prototype | Explicit percentage-weighted criteria are shown with exact source citations. |
| Owner review and correction | Implemented | Corrections and rejections are versioned, attributed, and audited. |
| Customer publication | Implemented | Only a published, non-rejected result is shown as ready. |
| OCR and complex tables | Not implemented | Missing digital text remains explicit; no content is fabricated. |
| Company and evidence matching | Not implemented | No supplier capability conclusion is generated. |
| Compliance or bid decision | Not implemented | No score, recommendation, or legal conclusion is generated. |
| Billing and customer export | Not implemented | Pilot payment and any deliverables outside the web report remain manual. |
| Cloud and production | Not performed | Current verification is local only. |

## Verification record

- Next.js and Tailwind CSS: lint, type generation, TypeScript checking, four Vitest tests, and the production build pass.
- .NET: build passes with zero warnings and all 28 integration, security, policy, extraction, publication, and evaluation tests pass.
- Python foundation: Ruff, strict mypy, and all 12 pytest cases pass unchanged.
- Compose: configuration validation passes.
- Visual QA: the new landing page and customer dashboard were inspected at desktop and 390 by 844 mobile viewports.
- Running F2 stack: not verified. The rebuild command reached its five-minute timeout before replacing the existing healthy F1-era containers, so migration `0009` and the new runtime images still require the running-stack gate.

## Post-F2 boundary

Supplier profiles, evidence libraries, matching, team collaboration, customer exports, billing, notifications, self-service administration, OCR, model-assisted extraction, cloud deployment, and production operations require a separately approved release. Commercial validation requires at least one paid pilot and measured repeat demand.
