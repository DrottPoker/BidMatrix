import { describe, expect, it } from "vitest";
import type { AnalysisRequirement } from "@/lib/bidmatrix-api";
import {
  buildComplianceMatrixRows,
  createComplianceMatrixFileName,
  serializeComplianceMatrixCsv,
} from "@/lib/compliance-matrix-export";

describe("compliance matrix export", () => {
  it("creates an Excel-compatible requirement matrix with exact source evidence", () => {
    const rows = buildComplianceMatrixRows([requirement]);
    const csv = serializeComplianceMatrixCsv(rows);

    expect(csv.startsWith("\uFEFF")).toBe(true);
    expect(csv).toContain('"Customer status","Customer response"');
    expect(csv).toContain('"SEC-001"');
    expect(csv).toContain('"Yes"');
    expect(csv).toContain('"Security compliance"');
    expect(csv).toContain('"rfp.pdf, page 2, SECURITY REQUIREMENTS"');
    expect(csv).toContain('"The supplier must provide an ISO 27001 certificate."');
    expect(csv).toContain('"97%"');
  });

  it("neutralizes spreadsheet formulas and escapes quotes", () => {
    const rows = buildComplianceMatrixRows([
      {
        ...requirement,
        requirementText: '=HYPERLINK("https://invalid.example","Open")',
      },
    ]);

    const csv = serializeComplianceMatrixCsv(rows);

    expect(csv).toContain('"\'=HYPERLINK(""https://invalid.example"",""Open"")"');
  });

  it("creates a bounded portable file name", () => {
    expect(createComplianceMatrixFileName("NordicCare / Security RFP 2026"))
      .toBe("nordiccare-security-rfp-2026-compliance-matrix.csv");
    expect(createComplianceMatrixFileName(null))
      .toBe("analysis-compliance-matrix.csv");
  });
});

const requirement: AnalysisRequirement = {
  id: "requirement-1",
  requirementCode: "SEC-001",
  requirementText: "The supplier must provide an ISO 27001 certificate.",
  originalRequirementText: "The supplier must provide an ISO 27001 certificate.",
  normalizedRequirement: "the supplier must provide an iso 27001 certificate",
  category: "security_compliance",
  mandatory: true,
  requestedEvidence: "ISO 27001 certificate",
  confidence: 0.97,
  reviewStatus: "accepted",
  correctionNote: null,
  version: 2,
  citations: [
    {
      id: "citation-1",
      analysisFileId: "file-1",
      originalFileName: "rfp.pdf",
      pageNumber: 2,
      sectionText: "SECURITY REQUIREMENTS",
      quoteText: "The supplier must provide an ISO 27001 certificate.",
    },
  ],
};
