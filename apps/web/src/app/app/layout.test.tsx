import { render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import CustomerAppLayout from "./layout";

vi.mock("next/navigation", () => ({
  usePathname: () => "/app",
  useRouter: () => ({ push: vi.fn(), refresh: vi.fn() }),
}));

describe("customer navigation", () => {
  it("exposes the focused customer flow without internal operating-system pages", () => {
    render(<CustomerAppLayout><p>Customer content</p></CustomerAppLayout>);
    const navigation = screen.getByRole("navigation", { name: "Customer workspace" });
    expect(navigation).toBeInTheDocument();
    expect(within(navigation).getByRole("link", { name: "Analyses" })).toHaveAttribute("href", "/app/analyses");
    expect(within(navigation).getByRole("link", { name: "New analysis" })).toHaveAttribute("href", "/app/analyses/new");
    expect(within(navigation).getByRole("link", { name: "Account" })).toHaveAttribute("href", "/app/account");
    expect(screen.queryByText("Approvals")).not.toBeInTheDocument();
    expect(screen.queryByText("Agents")).not.toBeInTheDocument();
    expect(screen.queryByText("Audit")).not.toBeInTheDocument();
  });
});
