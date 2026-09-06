# BidMatrix MVP v1

## Product promise

A customer uploads a digital English RFP PDF and receives a human-reviewed,
source-linked overview of requirements, key dates, requested documents, and
weighted evaluation criteria.

MVP v1 is deliberately a small concierge product. It is not a procurement
management suite, collaboration platform, evidence engine, or autonomous agent
product.

## Required customer flow

1. Create and verify an email/password account.
2. Create an analysis and upload one or more digital English PDF documents.
3. See whether the analysis is queued, processing, awaiting quality review,
   ready, cancelled, or needs attention.
4. Open the owner-published result.
5. Review the source-linked overview and compliance matrix.
6. Download the compliance matrix as CSV and continue response work in Excel or
   another spreadsheet application.

## Required result

The published result contains only owner-reviewed, non-rejected items:

- mandatory and optional requirements;
- key procurement dates;
- requested submission documents;
- weighted evaluation criteria; and
- exact source file, page, section, and quote evidence.

The compliance matrix is intentionally requirements-only. It supports search,
mandatory or optional filtering, category filtering, and a CSV export. The CSV
contains stable row numbers, requirement code, obligation, category,
requirement, requested evidence, empty customer-status and response columns,
source evidence, confidence, and review status. Document content is escaped and
spreadsheet-formula prefixes are neutralized before download.

## Quality and honesty boundary

- Results remain hidden until platform-owner quality review and publication.
- Failed extraction and unsupported scanned content produce an explicit state.
- BidMatrix does not fabricate content that could not be extracted.
- OCR is not part of MVP v1.
- Every published requirement must retain at least one exact citation.
- The deterministic evaluation fixture covers five representative synthetic
  procurement documents and nine mandatory requirements with exact page and
  quote checks. It is regression evidence, not market or real-document
  validation.

## Explicitly deferred

- supplier profiles and reusable evidence libraries;
- evidence matching, compliance scoring, and bid or no-bid recommendations;
- customer editing, team assignment, comments, and collaboration queues;
- OCR, handwriting, complex tables, Word, Excel, and ZIP intake;
- styled PDF or native XLSX exports;
- notifications, API access, webhooks, billing, and automated retention;
- Google and GitHub login;
- customer-facing agents, live model dependencies, schedules, or company
  steering.

## Completion state

The local MVP v1 flow and compliance-matrix presentation are implemented.
Commercial validation still requires representative customer documents, a
real customer exercise, and evidence that the result saves enough review time
to justify payment. Hosting remains a separate S1 release gate.
