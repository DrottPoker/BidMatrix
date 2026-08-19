import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AccountPage } from "./account-page";

const routerReplace = vi.fn();
const routerRefresh = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: routerReplace, refresh: routerRefresh }),
}));

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  routerReplace.mockReset();
  routerRefresh.mockReset();
});

describe("account security", () => {
  it("shows server sessions and signs out after changing the password", async () => {
    let passwordBody: Record<string, unknown> | null = null;
    const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input);
      if (url.endsWith("/v1/me")) {
        return jsonResponse({
          userId: "019f9200-0000-7000-8000-000000000001",
          email: "pilot@example.invalid",
          displayName: "Pilot Owner",
          organizations: [{ organizationId: "019f9200-0000-7000-8000-000000000002", role: "owner" }],
          platformRoles: [],
        });
      }

      if (url.endsWith("/v1/auth/sessions")) {
        return jsonResponse({
          sessions: [{
            id: "019f9200-0000-7000-8000-000000000003",
            createdAt: "2026-08-11T12:00:00Z",
            lastSeenAt: "2026-08-11T12:05:00Z",
            absoluteExpiresAt: "2026-08-11T20:00:00Z",
            revokedAt: null,
            revokedReason: null,
            status: "active",
            isCurrent: true,
            version: 1,
          }],
        });
      }

      if (url.endsWith("/v1/auth/configuration")) {
        return jsonResponse({
          managedOidcEnabled: false,
          managedOidcProviderName: "Managed identity",
          nativeLoginEnabled: true,
          nativeRecoveryEnabled: true,
          identityTransitionMode: false,
        });
      }

      if (url.endsWith("/v1/auth/federated-identities")) {
        return jsonResponse({ identities: [], nativePasswordEnabled: true });
      }

      if (url.endsWith("/v1/auth/csrf")) {
        return jsonResponse({ token: "csrf-token", headerName: "X-CSRF-TOKEN" });
      }

      if (url.endsWith("/v1/auth/password")) {
        passwordBody = JSON.parse(String(init?.body)) as Record<string, unknown>;
        return new Response(null, { status: 204 });
      }

      throw new Error(`Unexpected request ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<AccountPage />);

    expect(await screen.findByText("Pilot Owner")).toBeInTheDocument();
    expect(screen.getByText("current")).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Current password"), {
      target: { value: "Current-password-2026!" },
    });
    fireEvent.change(screen.getByLabelText("New password"), {
      target: { value: "New-password-value-2026!" },
    });
    fireEvent.change(screen.getByLabelText("Confirm new password"), {
      target: { value: "New-password-value-2026!" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Change password" }));

    await waitFor(() => expect(routerReplace).toHaveBeenCalledWith("/login"));
    expect(routerRefresh).toHaveBeenCalledOnce();
    expect(passwordBody).toEqual({
      currentPassword: "Current-password-2026!",
      newPassword: "New-password-value-2026!",
    });
  });

  it("hides password rotation when the account has no native password", async () => {
    const fetchMock = vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      if (url.endsWith("/v1/me")) {
        return jsonResponse({
          userId: "019f9200-0000-7000-8000-000000000011",
          email: "managed@example.invalid",
          displayName: "Managed Pilot",
          organizations: [{ organizationId: "019f9200-0000-7000-8000-000000000012", role: "owner" }],
          platformRoles: [],
        });
      }

      if (url.endsWith("/v1/auth/sessions")) {
        return jsonResponse({
          sessions: [{
            id: "019f9200-0000-7000-8000-000000000013",
            createdAt: "2026-08-11T12:00:00Z",
            lastSeenAt: "2026-08-11T12:05:00Z",
            absoluteExpiresAt: "2026-08-11T20:00:00Z",
            revokedAt: null,
            revokedReason: null,
            status: "active",
            isCurrent: true,
            version: 1,
          }],
        });
      }

      if (url.endsWith("/v1/auth/configuration")) {
        return jsonResponse({
          managedOidcEnabled: true,
          managedOidcProviderName: "Pilot Identity",
          nativeLoginEnabled: true,
          nativeRecoveryEnabled: false,
          identityTransitionMode: true,
        });
      }

      if (url.endsWith("/v1/auth/federated-identities")) {
        return jsonResponse({
          nativePasswordEnabled: false,
          identities: [{
            id: "019f9200-0000-7000-8000-000000000014",
            providerName: "Pilot Identity",
            issuer: "https://identity.example.invalid",
            emailAtLink: "managed@example.invalid",
            status: "active",
            linkedAt: "2026-08-11T12:00:00Z",
            lastAuthenticatedAt: "2026-08-11T12:00:00Z",
            revokedAt: null,
            version: 1,
          }],
        });
      }

      throw new Error(`Unexpected request ${url}`);
    });
    vi.stubGlobal("fetch", fetchMock);

    render(<AccountPage />);

    expect(await screen.findByRole("heading", { name: "Pilot Identity" })).toBeInTheDocument();
    expect(screen.getAllByText("managed@example.invalid").length).toBeGreaterThan(0);
    expect(screen.getByRole("link", { name: "Verify link" })).toHaveAttribute(
      "href",
      "http://localhost:8080/v1/auth/oidc/link?returnUrl=%2Fapp%2Faccount",
    );
    expect(screen.getByText("Required sign-in method")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Revoke and sign out" })).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "Managed-only account" })).toBeInTheDocument();
    expect(screen.queryByRole("heading", { name: "Change password" })).not.toBeInTheDocument();
  });
});

function jsonResponse(value: unknown) {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}
