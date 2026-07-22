# Concierge Pilot F2 capability boundaries

## Product contract

F2 is a focused, sellable concierge product for smaller IT and cybersecurity companies. BidMatrix organizes procurement information and preserves the source trail. The customer decides whether the company can and should respond.

The customer flow is intentionally small:

1. Sign in to the organization workspace.
2. Create an analysis and upload digital English PDF files.
3. Wait while extraction and internal quality review run.
4. Open the published analysis.
5. Review requirements, key dates, requested documents, and evaluation criteria with exact source citations.

## Implemented in F2

- Professional responsive customer workspace with dashboard, analysis list, new-analysis flow, report, and account page.
- Deterministic extraction of requirement candidates, key dates, requested documents, and weighted evaluation criteria.
- File, page, section, exact quote, confidence, and review status for every extracted item.
- Owner review API and UI for accepting, correcting, or rejecting requirements and other findings.
- Optimistic concurrency for review edits.
- Original extracted text retained alongside corrected text.
- Explicit owner publication gate before customer results become visible.
- Review note, correction count, processing duration, reviewer identity, and publication time retained in the authoritative database and audit trail.
- Tenant isolation and the F0 security boundaries remain active.

## Deliberately excluded

- Supplier capability profiles and service matching.
- Certificate, reference, personnel, or evidence-library matching.
- Compliance scoring and bid or no-bid recommendations.
- Team assignment, requirement ownership, and collaboration queues.
- Customer-editable findings or automated customer approval.
- OCR, handwriting recognition, and complex table reconstruction.
- Customer exports, billing automation, notifications, and self-service administration.
- Customer-facing agents, prompts, workflows, tools, or approval infrastructure.
- Live external actions, cloud deployment, and production operations.

## Human responsibility

The owner reviews every analysis before publication. Publication accepts all remaining pending items as reviewed, preserves explicit corrections and rejections, and makes only non-rejected items visible to the customer. A published result is an information product, not legal advice, a compliance conclusion, or a recommendation to bid.

## Commercial release boundary

The local product can be used for controlled pilot delivery. F2 is not commercially validated until at least one pilot customer pays and repeat-analysis demand is measured. Those are business outcomes and cannot be satisfied by repository implementation alone.
