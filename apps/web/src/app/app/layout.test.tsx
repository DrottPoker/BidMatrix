import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import CustomerAppLayout from "./layout";

describe("customer navigation", () => {
  it("does not expose internal operating-system pages", () => {
    render(<CustomerAppLayout><p>Customer content</p></CustomerAppLayout>);
    expect(screen.getByRole("navigation", { name: "Customer workspace" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Analyses" })).toHaveAttribute("href", "/app/analyses");
    expect(screen.queryByText("Approvals")).not.toBeInTheDocument();
    expect(screen.queryByText("Agents")).not.toBeInTheDocument();
    expect(screen.queryByText("Audit")).not.toBeInTheDocument();
  });
});
