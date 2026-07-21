import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { OwnerConsole } from "./owner-console";

const approval = {
  id: "019f8000-0000-7000-8000-000000000001",
  organizationId: "019f8000-0000-7000-8000-000000000002",
  toolCallId: "019f8000-0000-7000-8000-000000000003",
  taskId: "019f8000-0000-7000-8000-000000000004",
  actionType: "email.send",
  status: "pending",
  summary: "Send the proposed support reply",
  normalizedPayload: { body: "Exact draft", destination: "customer@example.invalid" },
  payloadHash: "a".repeat(64),
  policyVersion: "f0-v1",
  riskLevel: "red",
  technicallyEnabled: false,
  requestedAt: "2026-07-20T12:00:00Z",
  expiresAt: "2026-07-21T12:00:00Z",
  decidedByUserId: null,
  decidedAt: null,
  decisionNote: null,
  executionStatus: "notStarted",
  version: 1,
  supersedesApprovalId: null,
};

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

describe("Owner Console approvals", () => {
  it("shows and submits the exact normalized payload", async () => {
    const requestBodies: string[] = [];
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith("/v1/auth/csrf")) {
        return jsonResponse({ token: "csrf-token", headerName: "X-CSRF-TOKEN" });
      }
      if (url.endsWith(`/owner/v1/approvals/${approval.id}/approve`)) {
        requestBodies.push(String(init?.body));
        return jsonResponse({ ...approval, status: "approved", executionStatus: "disabled" });
      }
      return jsonResponse({ approvals: [approval] });
    });
    vi.stubGlobal("fetch", fetchMock);
    vi.stubGlobal("confirm", vi.fn(() => true));

    render(<OwnerConsole section="approvals" />);

    expect(await screen.findByText("Exact normalized payload")).toBeInTheDocument();
    expect(screen.getByText(/customer@example.invalid/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Approve exact payload" }));

    await waitFor(() => expect(requestBodies).toHaveLength(1));
    expect(JSON.parse(requestBodies[0])).toEqual({
      payload: approval.normalizedPayload,
      expectedVersion: approval.version,
      note: "Owner chose approve.",
    });
  });
});

function jsonResponse(value: unknown) {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}
