import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { CustomerShell } from "./customer-shell";

const mocks = vi.hoisted(() => ({
  apiGet: vi.fn(),
  apiMutation: vi.fn(),
  managedSignOut: vi.fn(),
  routerRefresh: vi.fn(),
  routerReplace: vi.fn(),
}));

vi.mock("next/navigation", () => ({
  usePathname: () => "/app",
  useRouter: () => ({
    refresh: mocks.routerRefresh,
    replace: mocks.routerReplace,
  }),
}));

vi.mock("@/lib/bidmatrix-api", () => ({
  apiGet: mocks.apiGet,
  apiMutation: mocks.apiMutation,
}));

vi.mock("@/lib/auth-client", () => ({
  authClient: {
    signOut: mocks.managedSignOut,
  },
}));

afterEach(() => {
  mocks.apiGet.mockReset();
  mocks.apiMutation.mockReset();
  mocks.managedSignOut.mockReset();
  mocks.routerRefresh.mockReset();
  mocks.routerReplace.mockReset();
});

describe("customer logout", () => {
  it("revokes the BidMatrix session before ending the Better Auth session", async () => {
    mocks.apiGet.mockResolvedValue({
      userId: "user-1",
      email: "customer@example.invalid",
      displayName: "Customer",
      organizations: [],
      platformRoles: [],
    });
    mocks.apiMutation.mockResolvedValue({ success: true });
    mocks.managedSignOut.mockResolvedValue({
      data: { success: true },
      error: null,
    });

    render(<CustomerShell>Workspace</CustomerShell>);

    fireEvent.click(screen.getByRole("button", { name: "Sign out" }));

    await waitFor(() => {
      expect(mocks.apiMutation).toHaveBeenCalledWith("/v1/auth/logout", {
        method: "POST",
      });
      expect(mocks.managedSignOut).toHaveBeenCalledWith({
        callbackURL: "/login",
        disableRedirect: true,
      });
      expect(mocks.routerReplace).toHaveBeenCalledWith("/login");
      expect(mocks.routerRefresh).toHaveBeenCalledOnce();
    });
    expect(mocks.apiMutation.mock.invocationCallOrder[0]).toBeLessThan(
      mocks.managedSignOut.mock.invocationCallOrder[0],
    );
  });

  it("does not end the managed session when local revocation fails", async () => {
    mocks.apiGet.mockResolvedValue({
      userId: "user-1",
      email: "customer@example.invalid",
      displayName: "Customer",
      organizations: [],
      platformRoles: [],
    });
    mocks.apiMutation.mockRejectedValue(new Error("logout failed"));

    render(<CustomerShell>Workspace</CustomerShell>);

    fireEvent.click(screen.getByRole("button", { name: "Sign out" }));

    await waitFor(() => {
      expect(mocks.apiMutation).toHaveBeenCalledOnce();
      expect(screen.getByRole("button", { name: "Sign out" })).toBeEnabled();
    });
    expect(mocks.managedSignOut).not.toHaveBeenCalled();
    expect(mocks.routerReplace).not.toHaveBeenCalled();
  });
});
