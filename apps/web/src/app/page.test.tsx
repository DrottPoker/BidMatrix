import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import Home from "./page";

describe("foundation home page", () => {
  it("presents the F0 scope without claiming unavailable analysis", () => {
    render(<Home />);

    expect(
      screen.getByRole("heading", {
        level: 1,
        name: "Trustworthy bid intelligence starts with control.",
      }),
    ).toBeInTheDocument();
    expect(screen.getByText("Draft-only foundation")).toBeInTheDocument();
    expect(screen.getByText("Explicitly unavailable in F0")).toBeInTheDocument();
    expect(screen.getByText(/bid\/no-bid scoring/i)).toBeInTheDocument();
  });
});
