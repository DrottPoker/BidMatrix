# F1 capability boundaries

## Purpose

Release F1 adds a real, local extraction prototype to the verified F0 control plane. It produces sourced review candidates, not final procurement advice.

## Customer-visible scope

F1 provides:

- text extraction from digital English PDF files;
- preserved file and page identity;
- deterministic procurement document classification;
- structured mandatory and optional requirement candidates;
- categories, confidence values, requested-evidence hints, and review status;
- exact quote citations linked to the source file and page;
- explicit OCR and extraction-failure indicators;
- a manual-review task for every submitted analysis.

## Reliability boundary

- Every result is a draft with `reviewStatus=pending`.
- The UI says that manual review is required.
- A requirement without an exact source quote is not produced by the F1 pipeline.
- Blank page text is reported as `requires_ocr`.
- Failed files remain visible and make the overall extraction `partial`.
- Critical misses are measured against the checked-in evaluation set and are never hidden by a synthetic score.

## Explicit non-capabilities

F1 does not provide OCR, handwriting recognition, reliable complex-table reconstruction, company profile matching, evidence matching, compliance scoring, bid/no-bid recommendations, legal conclusions, correction capture, billing, outbound communication, remote Git actions, or production deployment.

F2 is the earliest phase that may introduce concierge-pilot reporting and manual correction capture.
