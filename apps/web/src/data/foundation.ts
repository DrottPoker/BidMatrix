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
  {
    label: "Release target",
    value: "F0",
    detail: "Bounded foundation",
  },
  {
    label: "Architecture decisions",
    value: "7",
    detail: "Accepted and recorded",
  },
  {
    label: "External actions",
    value: "0",
    detail: "Draft-only by policy",
  },
  {
    label: "Initial agents",
    value: "4",
    detail: "Contracts planned",
  },
];

export const foundationPhases: FoundationPhase[] = [
  {
    id: "phase-0",
    number: "00",
    title: "Decisions and boundaries",
    status: "Complete",
    outcome:
      "The authoritative scope, runtime choices, trust boundaries, and preserved prototype work are documented.",
    deliverables: ["Repository assessment", "Seven ADRs", "Runtime baseline"],
  },
  {
    id: "phase-1",
    number: "01",
    title: "Local infrastructure",
    status: "Active",
    outcome:
      "A cloud-portable monorepo runs the web, API, worker, database, object storage, and workflow services locally.",
    deliverables: ["Next.js shell", "Compose stack", "Health contracts"],
  },
  {
    id: "phase-2",
    number: "02",
    title: "Authoritative domain",
    status: "Locked",
    outcome:
      "Tenant-aware domain models, migrations, policies, approvals, audit records, and agent definitions become authoritative.",
    deliverables: ["PostgreSQL model", "Tenant isolation", "Development seed"],
  },
  {
    id: "phase-3",
    number: "03",
    title: "Identity and API",
    status: "Locked",
    outcome:
      "Owner bootstrap, organization-aware authentication, route authorization, and stable contracts protect the system.",
    deliverables: ["Authentication", "Authorization", "OpenAPI contracts"],
  },
];
