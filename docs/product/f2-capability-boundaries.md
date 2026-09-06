# Concierge Pilot F2 capability boundaries

## Product contract

F2 is a focused concierge product for smaller IT and cybersecurity companies. BidMatrix organizes procurement information and preserves the source trail. The customer decides whether the company can and should respond.

The customer flow is intentionally small:

1. Sign in to the organization workspace.
2. Create an analysis and upload digital English PDF files.
3. Wait while deterministic extraction and internal quality review run.
4. Open the owner-published analysis.
5. Review requirements, key dates, requested documents, and evaluation criteria with exact source citations.

## Implemented in F2

- Professional responsive customer workspace with dashboard, analysis list, new-analysis flow, report, and account page.
- Deterministic extraction of requirement candidates, key dates, requested documents, and weighted evaluation criteria.
- File, page, section, exact quote, confidence, and review status for every extracted item.
- Owner review API and UI for accepting, correcting, or rejecting requirements and other findings.
- Optimistic concurrency for review edits.
- Original extracted text retained alongside corrected text.
- Explicit owner publication gate before customer results become visible.
- Searchable and filterable customer compliance matrix for published requirements.
- Formula-safe CSV export with empty customer-status and response columns for continued spreadsheet work.
- Review note, correction count, processing duration, reviewer identity, and publication time retained in the authoritative database and audit trail.
- Tenant isolation and the F0 security boundaries remain active.

## S0 verification boundary

S0 proves this complete F2 flow on a clean local stack and makes it repeatable in CI. The controlled test report includes all four result types and verifies pre-publication hiding, correction history, stale-write rejection, rejected-item filtering, audit events, the audit chain, and disabled external actions.

S0 does not convert F2 into a production SaaS. Later S1 slices now provide local self-service private workspaces, server sessions, password rotation, Development recovery, one-shot owner bootstrap, explicit provider mappings, and Better Auth email/password. Those controls remain outside the F2 analysis contract and do not prove configured Google or GitHub providers, hosted recovery and email delivery, production hosting, billing, usage enforcement, or operational recovery.

## Deliberately excluded

- Managed production identity, Better Auth recovery and verified-email delivery, configured social-provider callbacks, and tenant self-service. Local invitation and recovery foundations are tracked under S1 rather than F2.
- Supplier capability profiles and service matching.
- Certificate, reference, personnel, or evidence-library matching.
- Compliance scoring and bid or no-bid recommendations.
- Team assignment, requirement ownership, and collaboration queues.
- Customer-editable findings or automated customer approval.
- OCR, handwriting recognition, and complex table reconstruction.
- Styled PDF or native XLSX exports, billing automation, notifications, and retention settings. MVP v1 includes only the requirements CSV defined in `docs/product/mvp-v1.md`.
- Customer-facing agents, prompts, workflows, tools, or approval infrastructure.
- Agent schedules, live model operation, company steering, and agent-created priorities.
- Live external actions, cloud deployment, and production operations.

## Human responsibility

The owner reviews every analysis before publication. Publication accepts all remaining pending items as reviewed, preserves explicit corrections and rejections, and makes only non-rejected items visible to the customer. A published result is an information product, not legal advice, a compliance conclusion, or a recommendation to bid.

## Agent boundary

The customer analysis path is deterministic and human-published. The Python worker coordinates Temporal activities but does not decide publication or business priorities. Existing F0 agent demonstrations are internal regression fixtures only. Under ADR 0010, no agent may steer company goals, product priorities, schedules, customer decisions, or external actions before the working SaaS gate is complete and the owner approves a new ADR.

## Commercial release boundary

The local product can support controlled development and pilot preparation. F2 is not commercially validated until at least one pilot customer pays and repeat-analysis demand is measured. Those are business outcomes and cannot be satisfied by repository implementation alone.

The current local product gate is the intentionally small MVP v1 defined in `docs/product/mvp-v1.md`. Hosted S1.1c remains incomplete but was deferred by the owner on 2026-08-19 while the customer result and quality evidence are improved locally.
