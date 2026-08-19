import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AccountRecovery } from "./account-recovery";

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  window.history.replaceState(null, "", "/");
});

describe("account recovery", () => {
  it("removes the bearer token from the URL and resets without signing in", async () => {
    const recoveryToken = "b".repeat(64);
    const requestBodies: Record<string, unknown>[] = [];
    window.history.replaceState(null, "", `/recover#token=${recoveryToken}`);

    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith("/v1/auth/configuration")) {
        return jsonResponse({
          managedOidcEnabled: false,
          managedOidcProviderName: "Managed identity",
          nativeLoginEnabled: true,
          nativeRecoveryEnabled: true,
          identityTransitionMode: false,
        });
      }

      if (url.endsWith("/v1/auth/csrf")) {
        return jsonResponse({ token: "csrf-token", headerName: "X-CSRF-TOKEN" });
      }

      requestBodies.push(JSON.parse(String(init?.body)) as Record<string, unknown>);
      if (url.endsWith("/v1/auth/recovery/inspect")) {
        return jsonResponse({
          recoveryId: "019f9100-0000-7000-8000-000000000001",
          email: "pilot@example.invalid",
          status: "pending",
          expiresAt: "2026-08-11T12:30:00Z",
        });
      }

      return jsonResponse({ status: "password_reset" });
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<AccountRecovery />);

    expect(await screen.findByRole("heading", { name: "Choose a new password" })).toBeInTheDocument();
    expect(window.location.hash).toBe("");
    expect(screen.queryByText(recoveryToken)).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("New password"), {
      target: { value: "Recovered-password-2026!" },
    });
    fireEvent.change(screen.getByLabelText("Confirm new password"), {
      target: { value: "Recovered-password-2026!" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Reset password" }));

    expect(await screen.findByRole("heading", { name: "Password reset complete" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Continue to sign in" })).toHaveAttribute("href", "/login");
    await waitFor(() => expect(requestBodies).toEqual([
      { token: recoveryToken },
      { token: recoveryToken, newPassword: "Recovered-password-2026!" },
    ]));
  });
});

function jsonResponse(value: unknown) {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}
