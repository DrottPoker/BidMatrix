import { render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { LoginForm } from "./login-form";

const routerPush = vi.fn();
const routerRefresh = vi.fn();
const routerReplace = vi.fn();

vi.mock("next/navigation", () => ({
  useRouter: () => ({
    push: routerPush,
    refresh: routerRefresh,
    replace: routerReplace,
  }),
}));

afterEach(() => {
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
  routerPush.mockReset();
  routerRefresh.mockReset();
  routerReplace.mockReset();
  window.history.replaceState(null, "", "/");
});

describe("managed identity login", () => {
  it("shows the configured provider and hides native credentials when disabled", async () => {
    window.history.replaceState(
      null,
      "",
      "/login?identityError=identity_not_linked",
    );
    installFetch(
      {
        managedOidcEnabled: true,
        managedOidcProviderName: "Pilot Identity",
        nativeLoginEnabled: false,
        nativeRecoveryEnabled: false,
        identityTransitionMode: false,
      },
      [],
    );

    render(<LoginForm />);

    const providerLink = await screen.findByRole("link", {
      name: "Open sign in",
    });
    expect(providerLink).toHaveAttribute(
      "href",
      "http://localhost:8080/v1/auth/oidc/login?returnUrl=%2Fapp",
    );
    expect(screen.queryByLabelText("Email")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("Password")).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Use a recovery link" })).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Create account" })).toHaveAttribute(
      "href",
      "/register",
    );
  });

  it("offers verified email and password recovery inside the Better Auth flow", async () => {
    window.history.replaceState(null, "", "/login?client_id=test&sig=test");
    installFetch(
      {
        managedOidcEnabled: true,
        managedOidcProviderName: "Better Auth",
        nativeLoginEnabled: true,
        nativeRecoveryEnabled: true,
        identityTransitionMode: true,
      },
      [],
    );

    render(<LoginForm />);

    expect(
      await screen.findByRole("button", { name: "Sign in with password" }),
    ).toBeInTheDocument();
    expect(screen.getAllByLabelText("Email")).toHaveLength(1);
    expect(screen.getAllByLabelText("Password")).toHaveLength(1);
    expect(screen.queryByText(/passkey/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/google|github/i)).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Forgot password?" })).toHaveAttribute(
      "href",
      "/forgot-password",
    );
    expect(screen.getByRole("link", { name: "Create account" })).toHaveAttribute(
      "href",
      "/register",
    );
  });
});

function installFetch(
  configuration: Record<string, unknown>,
  providers: Array<"google" | "github">,
) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const url = String(input);
      return url.includes("/api/auth/social-providers")
        ? jsonResponse({ providers })
        : jsonResponse(configuration);
    }),
  );
}

function jsonResponse(value: unknown) {
  return new Response(JSON.stringify(value), {
    status: 200,
    headers: { "Content-Type": "application/json" },
  });
}
