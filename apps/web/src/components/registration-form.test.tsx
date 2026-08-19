import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { RegistrationForm } from "./registration-form";

const { signUpEmail } = vi.hoisted(() => ({ signUpEmail: vi.fn() }));

vi.mock("@/lib/auth-client", () => ({
  authClient: { signUp: { email: signUpEmail } },
  betterAuthErrorMessage: () => "Registration failed.",
}));

afterEach(() => {
  vi.clearAllMocks();
});

describe("self-service registration", () => {
  it("offers ordinary email registration without invitation or social-provider noise", () => {
    render(<RegistrationForm />);

    expect(screen.getByLabelText("Name")).toBeInTheDocument();
    expect(screen.getByLabelText("Email")).toBeInTheDocument();
    expect(screen.getByLabelText("Password")).toHaveAttribute("minLength", "8");
    expect(screen.getByLabelText("Confirm password")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Create account" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Sign in" })).toHaveAttribute("href", "/login");
    expect(screen.queryByText(/invitation/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/google|github/i)).not.toBeInTheDocument();
  });

  it("sends verification and does not sign the account in", async () => {
    signUpEmail.mockResolvedValue({ data: { user: { id: "user-1" } }, error: null });
    render(<RegistrationForm />);

    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Test User" } });
    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: "test@example.test" },
    });
    fireEvent.change(screen.getByLabelText("Password"), {
      target: { value: "password-2026" },
    });
    fireEvent.change(screen.getByLabelText("Confirm password"), {
      target: { value: "password-2026" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Create account" }));

    await waitFor(() => {
      expect(signUpEmail).toHaveBeenCalledWith({
        name: "Test User",
        email: "test@example.test",
        password: "password-2026",
        callbackURL: "/email-verified",
      });
    });
    expect(await screen.findByText("Check your email")).toBeInTheDocument();
    expect(screen.getByText(/open the link before signing in/i)).toBeInTheDocument();
  });
});
