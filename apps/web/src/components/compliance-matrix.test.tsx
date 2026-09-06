import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { AnalysisRequirement } from "@/lib/bidmatrix-api";
import { ComplianceMatrix } from "@/components/compliance-matrix";

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("ComplianceMatrix", () => {
  it("filters reviewed requirements without changing their stable matrix numbers", () => {
    render(
      <ComplianceMatrix
        analysisTitle="Managed security RFP"
        requirements={[mandatoryRequirement, optionalRequirement]}
      />,
    );

    expect(screen.getByText("Showing 2 of 2 requirements")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Filter by obligation"), {
      target: { value: "mandatory" },
    });

    expect(screen.getByText("Showing 1 of 2 requirements")).toBeInTheDocument();
    expect(screen.getByText(mandatoryRequirement.requirementText)).toBeInTheDocument();
    expect(screen.queryByText(optionalRequirement.requirementText)).not.toBeInTheDocument();
    expect(screen.getByText("01")).toBeInTheDocument();
  });

  it("downloads the complete matrix as a portable CSV", () => {
    const createObjectUrl = vi.fn<(object: Blob | MediaSource) => string>()
      .mockReturnValue("blob:matrix");
    const revokeObjectUrl = vi.fn();
    vi.stubGlobal("URL", {
      createObjectURL: createObjectUrl,
      revokeObjectURL: revokeObjectUrl,
    });
    const click = vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);

    render(
      <ComplianceMatrix
        analysisTitle="Managed security RFP"
        requirements={[mandatoryRequirement, optionalRequirement]}
      />,
    );
    fireEvent.click(screen.getByRole("button", { name: "Download CSV" }));

    expect(createObjectUrl).toHaveBeenCalledOnce();
    expect(createObjectUrl.mock.calls[0][0]).toBeInstanceOf(Blob);
    expect(click).toHaveBeenCalledOnce();
    expect(revokeObjectUrl).toHaveBeenCalledWith("blob:matrix");
  });
});

const mandatoryRequirement: AnalysisRequirement = {
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

const optionalRequirement: AnalysisRequirement = {
  ...mandatoryRequirement,
  id: "requirement-2",
  requirementCode: "OPS-002",
  requirementText: "The supplier should describe optional training.",
  originalRequirementText: "The supplier should describe optional training.",
  normalizedRequirement: "the supplier should describe optional training",
  category: "technical_service",
  mandatory: false,
  requestedEvidence: null,
  confidence: 0.72,
  citations: [
    {
      ...mandatoryRequirement.citations[0],
      id: "citation-2",
      pageNumber: 3,
      sectionText: "OPERATIONS",
      quoteText: "The supplier should describe optional training.",
    },
  ],
};
