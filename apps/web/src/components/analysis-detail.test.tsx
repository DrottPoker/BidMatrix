import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AnalysisDetail } from "./analysis-detail";

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("AnalysisDetail", () => {
  it("shows a published F2 result with reviewed requirements and exact page citations", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/requirements")) {
        return jsonResponse({
          analysisId: "analysis-1",
          capabilityStatus: "ready",
          extractionStatus: "succeeded",
          extractionVersion: "pdfpig-0.1.15+rules-en-v1+findings-en-v1",
          completedAt: "2026-07-21T12:00:00Z",
          documents: [{ analysisFileId: "file-1", originalFileName: "rfp.pdf", extractionStatus: "extracted", documentType: "request_for_proposal", pageCount: 2, extractionMethod: "pdfpig-0.1.15", failureCode: null }],
          requirements: [{
            id: "requirement-1",
            requirementCode: "SEC-001",
            requirementText: "The supplier must provide an ISO 27001 certificate.",
            originalRequirementText: "The supplier must provide an ISO 27001 certificate.",
            normalizedRequirement: "the supplier must provide an iso 27001 certificate",
            category: "security_compliance",
            mandatory: true,
            requestedEvidence: "The supplier must provide an ISO 27001 certificate.",
            confidence: 0.97,
            reviewStatus: "accepted",
            correctionNote: null,
            version: 2,
            citations: [{ id: "citation-1", analysisFileId: "file-1", originalFileName: "rfp.pdf", pageNumber: 2, sectionText: "SECURITY REQUIREMENTS", quoteText: "The supplier must provide an ISO 27001 certificate." }],
          }],
          keyDates: [],
          requestedDocuments: [],
          evaluationCriteria: [],
          publication: { analysisStatus: "completed", reviewedAt: "2026-07-21T12:05:00Z", publishedAt: "2026-07-21T12:05:00Z", reviewNote: "Sources checked for customer delivery.", correctionCount: 0, processingDurationMilliseconds: 3000, isPublished: true },
          metrics: { documentCount: 1, pageCount: 2, requirementCount: 1, mandatoryRequirementCount: 1, citedRequirementCount: 1, keyDateCount: 0, requestedDocumentCount: 0, evaluationCriterionCount: 0, pendingReviewCount: 0, filesRequiringOcr: 0, failedFileCount: 0 },
          message: "This analysis was quality reviewed and published by BidMatrix.",
        });
      }

      return jsonResponse({
        id: "analysis-1", title: "Managed services RFP", status: "completed", sourceLanguage: "en", workflowId: "analysis-intake-analysis-1", requiresHumanReview: false, failureCode: null, failureMessage: null, createdAt: "2026-07-21T12:00:00Z", updatedAt: "2026-07-21T12:05:00Z", version: 4,
        files: [{ id: "file-1", originalFileName: "rfp.pdf", contentType: "application/pdf", sizeBytes: 1000, sha256: "a".repeat(64), scanStatus: "development_bypass", retentionUntil: null, createdAt: "2026-07-21T12:00:00Z" }],
      });
    }));

    render(<AnalysisDetail analysisId="analysis-1" />);

    expect(await screen.findByText("Quality reviewed")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Compliance matrix (1)" }));
    expect(screen.getByText("1 reviewed requirements")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Download CSV" })).toBeInTheDocument();
    expect(screen.getByText("Mandatory")).toBeInTheDocument();
    expect(screen.getByText(/rfp\.pdf · Page 2/)).toBeInTheDocument();
    expect(screen.getAllByText(/ISO 27001 certificate/)).toHaveLength(2);
  });

  it("shows a clear terminal state when document processing fails", async () => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/requirements")) {
        return jsonResponse({
          analysisId: "analysis-failed",
          capabilityStatus: "unavailable",
          extractionStatus: "failed",
          extractionVersion: "pdfpig-0.1.15+rules-en-v1+findings-en-v1",
          completedAt: "2026-08-19T10:00:00Z",
          documents: [{ analysisFileId: "file-1", originalFileName: "scan.pdf", extractionStatus: "failed", documentType: null, pageCount: null, extractionMethod: null, failureCode: "pdf_text_extraction_failed" }],
          requirements: [],
          keyDates: [],
          requestedDocuments: [],
          evaluationCriteria: [],
          publication: { analysisStatus: "failed", reviewedAt: null, publishedAt: null, reviewNote: null, correctionCount: 0, processingDurationMilliseconds: null, isPublished: false },
          metrics: { documentCount: 1, pageCount: 0, requirementCount: 0, mandatoryRequirementCount: 0, citedRequirementCount: 0, keyDateCount: 0, requestedDocumentCount: 0, evaluationCriterionCount: 0, pendingReviewCount: 0, filesRequiringOcr: 1, failedFileCount: 1 },
          message: "The PDF did not contain extractable digital text.",
        });
      }

      return jsonResponse({
        id: "analysis-failed", title: "Scanned RFP", status: "failed", sourceLanguage: "en", workflowId: "analysis-intake-analysis-failed", requiresHumanReview: true, failureCode: "pdf_text_extraction_failed", failureMessage: "The PDF did not contain extractable digital text.", createdAt: "2026-08-19T10:00:00Z", updatedAt: "2026-08-19T10:01:00Z", version: 3,
        files: [{ id: "file-1", originalFileName: "scan.pdf", contentType: "application/pdf", sizeBytes: 1000, sha256: "b".repeat(64), scanStatus: "development_bypass", retentionUntil: null, createdAt: "2026-08-19T10:00:00Z" }],
      });
    }));

    render(<AnalysisDetail analysisId="analysis-failed" />);

    expect(await screen.findByText("This analysis needs attention")).toBeInTheDocument();
    expect(screen.getByText("The PDF did not contain extractable digital text.")).toBeInTheDocument();
    expect(screen.getByText(/does not fabricate missing content/i)).toBeInTheDocument();
    expect(screen.queryByText("Your analysis is being prepared.")).not.toBeInTheDocument();
  });
});

function jsonResponse(value: unknown) {
  return new Response(JSON.stringify(value), { status: 200, headers: { "Content-Type": "application/json" } });
}
