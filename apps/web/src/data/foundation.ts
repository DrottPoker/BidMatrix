export type PhaseStatus = "Complete" | "Active" | "Locked";

export type FoundationPhase = {
  id: string;
  number: string;
  title: string;
  status: PhaseStatus;
  outcome: string;
  deliverables: string[];
};

export type FoundationMetric = {
  label: string;
  value: string;
  detail: string;
};

export const foundationMetrics: FoundationMetric[] = [
  { label: "Release target", value: "F1", detail: "Extraction gate active" },
  { label: "Architecture decisions", value: "9", detail: "Accepted and recorded" },
  { label: "External actions", value: "0", detail: "Disabled by policy" },
  { label: "Initial agents", value: "4", detail: "Offline demos verified" },
];

export const foundationPhases: FoundationPhase[] = [
  { id: "phase-0", number: "00", title: "Decisions and boundaries", status: "Complete", outcome: "Scope, runtime choices, trust boundaries, and preserved prototype direction are documented.", deliverables: ["Repository assessment", "Nine ADRs", "Runtime baseline"] },
  { id: "phase-1", number: "01", title: "Local infrastructure", status: "Complete", outcome: "The pinned web, API, worker, database, object storage, and workflow stack starts healthy.", deliverables: ["Next.js and Tailwind", "Compose stack", "Health contracts"] },
  { id: "phase-2", number: "02", title: "Authoritative domain", status: "Complete", outcome: "Eight migrations establish tenant-aware domain, audit, controls, approvals, agents, sandbox, and extraction state.", deliverables: ["PostgreSQL model", "Forced RLS", "Development seed"] },
  { id: "phase-3", number: "03", title: "Identity and API", status: "Complete", outcome: "Owner bootstrap, organization authentication, CSRF, authorization, OpenAPI, and problem details protect the system.", deliverables: ["Authentication", "Four route policies", "Contract tests"] },
  { id: "phase-4", number: "04", title: "Analysis intake", status: "Complete", outcome: "PDF files are validated, hashed, quarantined, and routed through durable extraction and mandatory manual review.", deliverables: ["PDF quarantine", "Temporal intake", "Manual review"] },
  { id: "phase-5", number: "05", title: "Controlled tools", status: "Complete", outcome: "Every agent action crosses deterministic policy, payload-bound approvals, and append-only audit.", deliverables: ["Tool Gateway", "Policy engine", "Approval workflow"] },
  { id: "phase-6", number: "06", title: "Four agents", status: "Complete", outcome: "Versioned strict outputs and deterministic workflows exist for all four bounded roles.", deliverables: ["Role prompts", "Pydantic contracts", "Offline demos"] },
  { id: "phase-7", number: "07", title: "Product surfaces", status: "Complete", outcome: "The customer shell stays honest while Owner Console exposes live operational state and controls.", deliverables: ["Customer app", "Owner Console", "Exact payload UI"] },
  { id: "phase-8", number: "08", title: "Engineering sandbox", status: "Complete", outcome: "A generated isolated worktree permits bounded fixture edits, exact commands, and reviewable diffs only.", deliverables: ["Path controls", "Command allowlist", "Diff artifact"] },
  { id: "phase-9", number: "09", title: "Release gate", status: "Complete", outcome: "Automated, security, dependency, documentation, Compose, and essential end-to-end gates are recorded.", deliverables: ["Threat model", "Runbooks", "F1 verification"] },
  { id: "f1-extraction", number: "F1", title: "Sourced extraction", status: "Active", outcome: "Digital PDF pages become classified requirement candidates with exact source citations and mandatory human review.", deliverables: ["Page-preserving extraction", "Strict requirements", "Measured recall"] },
];
