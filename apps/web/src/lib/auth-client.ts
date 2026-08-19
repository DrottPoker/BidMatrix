"use client";

import { createAuthClient } from "better-auth/react";
import { oauthProviderClient } from "@better-auth/oauth-provider/client";

export const authClient = createAuthClient({
  plugins: [oauthProviderClient()],
});

export function followBetterAuthRedirect(data: unknown) {
  if (!data || typeof data !== "object") return false;
  const url = "url" in data && typeof data.url === "string" ? data.url : null;
  if (!url) return false;

  window.location.assign(url);
  return true;
}

export function betterAuthErrorMessage(error: unknown) {
  if (error && typeof error === "object" && "code" in error) {
    const code = error.code;
    if (code === "EMAIL_NOT_VERIFIED") {
      return "Verify your email before signing in. We sent a new verification link.";
    }
    if (code === "TOO_MANY_REQUESTS") {
      return "Too many attempts. Wait a moment and try again.";
    }
    if (code === "INVALID_TOKEN" || code === "TOKEN_EXPIRED") {
      return "This secure link is invalid or has expired. Request a new one.";
    }
  }

  if (error && typeof error === "object" && "message" in error) {
    const message = error.message;
    if (typeof message === "string" && message.trim()) return message;
  }

  return "Secure identity sign-in could not be completed.";
}
