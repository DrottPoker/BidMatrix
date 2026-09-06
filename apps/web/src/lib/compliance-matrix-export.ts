import type { AnalysisRequirement } from "@/lib/bidmatrix-api";

export type ComplianceMatrixRow = {
  number: number;
  requirementCode: string;
  mandatory: string;
  category: string;
  requirement: string;
  requestedEvidence: string;
  customerStatus: string;
  customerResponse: string;
  source: string;
  sourceQuote: string;
  confidence: string;
  reviewStatus: string;
};

const columns: { heading: string; value: keyof ComplianceMatrixRow }[] = [
  { heading: "Number", value: "number" },
  { heading: "Requirement code", value: "requirementCode" },
  { heading: "Mandatory", value: "mandatory" },
  { heading: "Category", value: "category" },
  { heading: "Requirement", value: "requirement" },
  { heading: "Requested evidence", value: "requestedEvidence" },
  { heading: "Customer status", value: "customerStatus" },
  { heading: "Customer response", value: "customerResponse" },
  { heading: "Source", value: "source" },
  { heading: "Source quote", value: "sourceQuote" },
  { heading: "Confidence", value: "confidence" },
  { heading: "Review status", value: "reviewStatus" },
];

export function buildComplianceMatrixRows(
  requirements: AnalysisRequirement[],
): ComplianceMatrixRow[] {
  return requirements.map((requirement, index) => ({
    number: index + 1,
    requirementCode: requirement.requirementCode ?? "",
    mandatory: requirement.mandatory ? "Yes" : "No",
    category: humanize(requirement.category),
    requirement: requirement.requirementText,
    requestedEvidence: requirement.requestedEvidence ?? "",
    customerStatus: "",
    customerResponse: "",
    source: requirement.citations
      .map(
        (citation) =>
          `${citation.originalFileName}, page ${citation.pageNumber}${citation.sectionText ? `, ${citation.sectionText}` : ""}`,
      )
      .join(" | "),
    sourceQuote: requirement.citations
      .map((citation) => citation.quoteText)
      .join("\n---\n"),
    confidence: `${Math.round(requirement.confidence * 100)}%`,
    reviewStatus: humanize(requirement.reviewStatus),
  }));
}

export function serializeComplianceMatrixCsv(rows: ComplianceMatrixRow[]) {
  const records = [
    columns.map((column) => escapeCsvCell(column.heading)).join(","),
    ...rows.map((row) =>
      columns
        .map((column) => escapeCsvCell(String(row[column.value])))
        .join(","),
    ),
  ];

  return `\uFEFF${records.join("\r\n")}\r\n`;
}

export function createComplianceMatrixFileName(title: string | null) {
  const baseName = (title ?? "analysis")
    .normalize("NFKD")
    .replace(/[^a-zA-Z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 80)
    .toLowerCase();

  return `${baseName || "analysis"}-compliance-matrix.csv`;
}

function escapeCsvCell(value: string) {
  const withoutNullBytes = value.replaceAll("\0", "");
  const protectedValue = /^\s*[=+@-]/.test(withoutNullBytes)
    ? `'${withoutNullBytes}`
    : withoutNullBytes;
  return `"${protectedValue.replaceAll('"', '""')}"`;
}

function humanize(value: string) {
  return value
    .replaceAll("_", " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/^./, (character) => character.toUpperCase());
}
