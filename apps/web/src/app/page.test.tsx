import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import Home from "./page";

describe("foundation home page", () => {
  it("presents the sourced F1 scope without claiming later capabilities", () => {
    render(<Home />);

    expect(
      screen.getByRole("heading", {
        level: 1,
        name: "Trustworthy bid intelligence starts with control.",
      }),
    ).toBeInTheDocument();
    expect(screen.getByText("F1 extraction prototype")).toBeInTheDocument();
    expect(screen.getByText("Explicitly unavailable in F1")).toBeInTheDocument();
    expect(screen.getByText(/bid\/no-bid recommendations/i)).toBeInTheDocument();
  });
});
