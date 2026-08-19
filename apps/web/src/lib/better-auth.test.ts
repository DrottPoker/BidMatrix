import { describe, expect, it } from "vitest";
import { readRuntimeConfiguration } from "@/lib/better-auth";
import {
  enabledSocialProviderIds,
  readSocialProviderConfiguration,
} from "@/lib/social-providers";

describe("Better Auth security contract", () => {
  it("validates secrets, origins, email delivery, providers, and database configuration", () => {
    const configuration = readRuntimeConfiguration(validEnvironment());

    expect(configuration.baseUrl).toBe("https://auth.example.test");
    expect(configuration.trustedOrigins).toEqual([
      "https://app.example.test",
      "https://auth.example.test",
    ]);
    expect(configuration.pool.user).toBe("bidmatrix_auth");
    expect(configuration.emailDelivery).toMatchObject({
      host: "smtp.example.test",
      port: 587,
      requireTls: true,
      secure: false,
      fromAddress: "security@example.test",
    });
    expect(enabledSocialProviderIds(configuration.socialProviders)).toEqual([
      "google",
      "github",
    ]);
    expect(configuration.socialProviders.google).not.toHaveProperty("disableSignUp");
  });

  it("fails closed when critical identity delivery settings are absent or malformed", () => {
    expect(() =>
      readRuntimeConfiguration({
        ...validEnvironment(),
        BETTER_AUTH_URL: "https://auth.example.test/path",
      }),
    ).toThrow("BETTER_AUTH_URL must contain an HTTP or HTTPS origin without a path.");
    expect(() =>
      readRuntimeConfiguration({
        ...validEnvironment(),
        BETTER_AUTH_SECRET: "too-short",
      }),
    ).toThrow("BETTER_AUTH_SECRET must contain at least 32 characters.");
    expect(() =>
      readRuntimeConfiguration({
        ...validEnvironment(),
        BETTER_AUTH_SMTP_HOST: undefined,
      }),
    ).toThrow("BETTER_AUTH_SMTP_HOST is required.");
    expect(() =>
      readRuntimeConfiguration({
        ...validEnvironment(),
        BETTER_AUTH_SMTP_USER: "smtp-user",
        BETTER_AUTH_SMTP_PASSWORD: undefined,
      }),
    ).toThrow(
      "BETTER_AUTH_SMTP_USER and BETTER_AUTH_SMTP_PASSWORD must be configured together.",
    );
    expect(() =>
      readRuntimeConfiguration({
        ...validEnvironment(),
        BETTER_AUTH_SMTP_REQUIRE_TLS: "false",
      }),
    ).toThrow("Hosted SMTP must require TLS and use authenticated credentials.");
    expect(() =>
      readRuntimeConfiguration({
        ...validEnvironment(),
        BETTER_AUTH_URL: "http://auth.example.test",
      }),
    ).toThrow("BETTER_AUTH_URL must use HTTPS outside Development.");
    expect(() =>
      readSocialProviderConfiguration({
        BETTER_AUTH_GOOGLE_CLIENT_ID: "google-client",
      }),
    ).toThrow(
      "BETTER_AUTH_GOOGLE_CLIENT_ID and BETTER_AUTH_GOOGLE_CLIENT_SECRET must be configured together for google.",
    );
  });
});

function validEnvironment() {
  return {
    BETTER_AUTH_URL: "https://auth.example.test",
    BETTER_AUTH_SECRET: "s".repeat(32),
    BETTER_AUTH_DATABASE_HOST: "database.example.test",
    BETTER_AUTH_DATABASE_NAME: "bidmatrix",
    BETTER_AUTH_DATABASE_USER: "bidmatrix_auth",
    BETTER_AUTH_DATABASE_PASSWORD: "database-password",
    BETTER_AUTH_DATABASE_SSL: "require",
    BETTER_AUTH_TRUSTED_ORIGINS: "https://app.example.test",
    BETTER_AUTH_SMTP_HOST: "smtp.example.test",
    BETTER_AUTH_SMTP_PORT: "587",
    BETTER_AUTH_SMTP_SECURE: "false",
    BETTER_AUTH_SMTP_REQUIRE_TLS: "true",
    BETTER_AUTH_SMTP_USER: "smtp-user",
    BETTER_AUTH_SMTP_PASSWORD: "smtp-password",
    BETTER_AUTH_EMAIL_FROM_ADDRESS: "security@example.test",
    BETTER_AUTH_EMAIL_FROM_NAME: "BidMatrix Security",
    BETTER_AUTH_GOOGLE_CLIENT_ID: "google-client",
    BETTER_AUTH_GOOGLE_CLIENT_SECRET: "google-secret",
    BETTER_AUTH_GITHUB_CLIENT_ID: "github-client",
    BETTER_AUTH_GITHUB_CLIENT_SECRET: "github-secret",
  };
}
