import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { ForgotPasswordForm } from "./forgot-password-form";
import { ResetPasswordForm } from "./reset-password-form";

const { requestPasswordReset, resetPassword } = vi.hoisted(() => ({
  requestPasswordReset: vi.fn(),
  resetPassword: vi.fn(),
}));

vi.mock("@/lib/auth-client", () => ({
  authClient: { requestPasswordReset, resetPassword },
  betterAuthErrorMessage: () => "Recovery failed.",
}));

afterEach(() => {
  vi.clearAllMocks();
  window.history.replaceState(null, "", "/");
});

describe("managed password recovery", () => {
  it("uses a neutral response for password reset requests", async () => {
    requestPasswordReset.mockResolvedValue({ data: { status: true }, error: null });
    render(<ForgotPasswordForm />);

    fireEvent.change(screen.getByLabelText("Email"), {
      target: { value: "customer@example.test" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Send reset link" }));

    await waitFor(() => {
      expect(requestPasswordReset).toHaveBeenCalledWith({
        email: "customer@example.test",
        redirectTo: "http://localhost:3000/reset-password/continue",
      });
    });
    expect(await screen.findByText(/if an account exists/i)).toBeInTheDocument();
  });

  it("removes the reset bearer from the address bar and updates the password", async () => {
    resetPassword.mockResolvedValue({ data: { status: true }, error: null });
    window.history.replaceState(null, "", "/reset-password#token=single-use-token");
    render(<ResetPasswordForm />);

    const password = await screen.findByLabelText("New password");
    expect(window.location.hash).toBe("");
    fireEvent.change(password, { target: { value: "new-password-2026" } });
    fireEvent.change(screen.getByLabelText("Confirm new password"), {
      target: { value: "new-password-2026" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Update password" }));

    await waitFor(() => {
      expect(resetPassword).toHaveBeenCalledWith({
        newPassword: "new-password-2026",
        token: "single-use-token",
      });
    });
    expect(await screen.findByText("Password updated")).toBeInTheDocument();
    expect(screen.getByText(/existing sessions were revoked/i)).toBeInTheDocument();
  });
});
