import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import Home from "./page";

describe("customer landing page", () => {
  it("presents the focused F2 product without making the customer decision", () => {
    render(<Home />);

    expect(screen.getByRole("heading", { level: 1, name: "Understand the RFP before you commit." })).toBeInTheDocument();
    expect(screen.getByText(/Your company makes the decision/i)).toBeInTheDocument();
    expect(screen.getByText("Quality reviewed")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Requested documents" })).toBeInTheDocument();
  });
});
