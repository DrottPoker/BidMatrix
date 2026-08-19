import { timingSafeEqual } from "node:crypto";
import { betterAuth } from "better-auth";
import { jwt } from "better-auth/plugins";
import { oauthProvider } from "@better-auth/oauth-provider";
import { Pool, type PoolConfig } from "pg";
import {
  MINIMUM_PASSWORD_LENGTH,
  OWNER_BOOTSTRAP_HEADER,
} from "@/lib/auth-contract";
import {
  createIdentityEmailDelivery,
  readEmailDeliveryConfiguration,
  type EmailDeliveryConfiguration,
  type IdentityEmailDelivery,
} from "@/lib/email-delivery";
import {
  readSocialProviderConfiguration,
  type SocialProviderConfiguration,
} from "@/lib/social-providers";

export const BETTER_AUTH_SCHEMA = "better_auth";

type AuthRuntimeConfiguration = {
  baseUrl: string;
  emailDelivery: EmailDeliveryConfiguration;
  ownerBootstrapEmail: string | null;
  ownerBootstrapSecret: string | null;
  pool: PoolConfig;
  secret: string;
  socialProviders: SocialProviderConfiguration;
  trustedOrigins: string[];
};

type EnvironmentValues = Record<string, string | undefined>;

let authInstance: ReturnType<typeof createBetterAuth> | undefined;

export function getBetterAuth() {
  authInstance ??= createBetterAuth(readRuntimeConfiguration());
  return authInstance;
}

export function createBetterAuth(
  configuration: AuthRuntimeConfiguration,
  pool = new Pool(configuration.pool),
  emailDelivery: IdentityEmailDelivery = createIdentityEmailDelivery(
    configuration.emailDelivery,
  ),
) {
  return betterAuth({
    appName: "BidMatrix",
    baseURL: configuration.baseUrl,
    secret: configuration.secret,
    database: pool,
    trustedOrigins: configuration.trustedOrigins,
    emailAndPassword: {
      enabled: true,
      minPasswordLength: MINIMUM_PASSWORD_LENGTH,
      maxPasswordLength: 128,
      requireEmailVerification: true,
      autoSignIn: false,
      resetPasswordTokenExpiresIn: 60 * 30,
      revokeSessionsOnPasswordReset: true,
      sendResetPassword: async ({ user, url }) => {
        await emailDelivery.sendPasswordResetEmail({
          email: user.email,
          name: user.name,
          url,
        });
      },
    },
    emailVerification: {
      autoSignInAfterVerification: false,
      expiresIn: 60 * 60,
      sendOnSignIn: true,
      sendOnSignUp: true,
      sendVerificationEmail: async ({ user, url }) => {
        await emailDelivery.sendVerificationEmail({
          email: user.email,
          name: user.name,
          url,
        });
      },
    },
    user: {
      changeEmail: { enabled: false },
    },
    account: {
      encryptOAuthTokens: true,
      accountLinking: {
        enabled: true,
        allowDifferentEmails: false,
      },
    },
    session: {
      expiresIn: 60 * 60 * 8,
      updateAge: 60 * 15,
    },
    socialProviders: configuration.socialProviders,
    databaseHooks: {
      user: {
        create: {
          before: async (user, context) => {
            if (context?.path !== "/sign-up/email") {
              return { data: user };
            }

            if (
              isOwnerBootstrapRequest(
                context.headers,
                user.email,
                configuration.ownerBootstrapEmail,
                configuration.ownerBootstrapSecret,
              )
            ) {
              return { data: { ...user, emailVerified: true } };
            }

            return { data: { ...user, emailVerified: false } };
          },
        },
      },
    },
    rateLimit: {
      enabled: true,
      storage: "database",
      window: 60,
      max: 100,
      customRules: {
        "/request-password-reset": { window: 60, max: 5 },
        "/send-verification-email": { window: 60, max: 3 },
        "/sign-in/email": { window: 60, max: 10 },
        "/sign-up/email": { window: 60, max: 5 },
      },
    },
    plugins: [
      jwt({
        disableSettingJwtHeader: true,
        jwks: {
          keyPairConfig: { alg: "ES256" },
          rotationInterval: 60 * 60 * 24 * 30,
          gracePeriod: 60 * 60 * 24 * 30,
        },
      }),
      oauthProvider({
        loginPage: "/login",
        consentPage: "/login",
        scopes: ["openid", "profile", "email"],
        grantTypes: ["authorization_code"],
        accessTokenExpiresIn: 600,
        idTokenExpiresIn: 600,
        codeExpiresIn: 120,
        customIdTokenClaims: ({ user }) => ({
          email: user.email,
          email_verified: user.emailVerified,
          name: user.name,
        }),
      }),
    ],
    advanced: {
      cookiePrefix: "bidmatrix_auth",
    },
  });
}

export function readRuntimeConfiguration(
  environment: EnvironmentValues = process.env,
): AuthRuntimeConfiguration {
  const baseUrl = parseOrigin(
    environment.BETTER_AUTH_URL ??
      environment.BIDMATRIX_PUBLIC_BASE_URL ??
      "http://localhost:3000",
    "BETTER_AUTH_URL",
  );
  const development =
    readOptional(environment.BIDMATRIX_ENVIRONMENT)?.toLowerCase() ===
    "development";
  if (!development && new URL(baseUrl).protocol !== "https:") {
    throw new Error("BETTER_AUTH_URL must use HTTPS outside Development.");
  }
  const secret = readRequired(environment, "BETTER_AUTH_SECRET");
  if (secret.length < 32) {
    throw new Error("BETTER_AUTH_SECRET must contain at least 32 characters.");
  }

  const ownerBootstrapEmail = readOptional(environment.BETTER_AUTH_OWNER_EMAIL);
  const ownerBootstrapSecret = readOptional(
    environment.BETTER_AUTH_OWNER_BOOTSTRAP_SECRET,
  );
  if ((ownerBootstrapEmail === null) !== (ownerBootstrapSecret === null)) {
    throw new Error(
      "BETTER_AUTH_OWNER_EMAIL and BETTER_AUTH_OWNER_BOOTSTRAP_SECRET must be configured together.",
    );
  }
  if (ownerBootstrapSecret !== null && ownerBootstrapSecret.length < 32) {
    throw new Error(
      "BETTER_AUTH_OWNER_BOOTSTRAP_SECRET must contain at least 32 characters.",
    );
  }

  const trustedOrigins = parseTrustedOrigins(
    environment.BETTER_AUTH_TRUSTED_ORIGINS,
    baseUrl,
  );

  const emailDelivery = readEmailDeliveryConfiguration(environment);
  if (
    !development &&
    ((!emailDelivery.secure && !emailDelivery.requireTls) ||
      emailDelivery.user === null ||
      emailDelivery.password === null)
  ) {
    throw new Error(
      "Hosted SMTP must require TLS and use authenticated credentials.",
    );
  }

  return {
    baseUrl,
    emailDelivery,
    ownerBootstrapEmail,
    ownerBootstrapSecret,
    pool: readPoolConfiguration(environment),
    secret,
    socialProviders: readSocialProviderConfiguration(environment),
    trustedOrigins,
  };
}

function isOwnerBootstrapRequest(
  headers: Headers | undefined,
  requestedEmail: string,
  ownerEmail: string | null,
  bootstrapSecret: string | null,
) {
  if (!ownerEmail || !bootstrapSecret) return false;
  if (normalizeEmail(ownerEmail) !== normalizeEmail(requestedEmail)) return false;

  const suppliedSecret = headers?.get(OWNER_BOOTSTRAP_HEADER);
  if (!suppliedSecret) return false;
  const supplied = Buffer.from(suppliedSecret, "utf8");
  const expected = Buffer.from(bootstrapSecret, "utf8");
  return supplied.length === expected.length && timingSafeEqual(supplied, expected);
}

function readPoolConfiguration(environment: EnvironmentValues): PoolConfig {
  const connectionString = readOptional(environment.BETTER_AUTH_DATABASE_URL);
  const common: PoolConfig = {
    application_name: "bidmatrix-better-auth",
    connectionTimeoutMillis: 5_000,
    idleTimeoutMillis: 30_000,
    max: 10,
  };

  if (connectionString) {
    return { ...common, connectionString };
  }

  const portValue = environment.BETTER_AUTH_DATABASE_PORT ?? "5432";
  const port = Number.parseInt(portValue, 10);
  if (!Number.isInteger(port) || port < 1 || port > 65_535) {
    throw new Error("BETTER_AUTH_DATABASE_PORT must be a valid TCP port.");
  }

  return {
    ...common,
    host: readRequired(environment, "BETTER_AUTH_DATABASE_HOST"),
    port,
    database: readRequired(environment, "BETTER_AUTH_DATABASE_NAME"),
    user: readRequired(environment, "BETTER_AUTH_DATABASE_USER"),
    password: readRequired(environment, "BETTER_AUTH_DATABASE_PASSWORD"),
    ssl:
      environment.BETTER_AUTH_DATABASE_SSL?.toLowerCase() === "require"
        ? { rejectUnauthorized: true }
        : false,
  };
}

function parseTrustedOrigins(value: string | undefined, baseUrl: string) {
  const origins = (value ?? "")
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean)
    .map((item) => parseOrigin(item, "BETTER_AUTH_TRUSTED_ORIGINS"));
  origins.push(new URL(baseUrl).origin);
  return [...new Set(origins)];
}

function parseOrigin(value: string, key: string) {
  let url: URL;
  try {
    url = new URL(value);
  } catch {
    throw new Error(`${key} must contain an absolute HTTP or HTTPS origin.`);
  }

  if (
    !["http:", "https:"].includes(url.protocol) ||
    url.username ||
    url.password ||
    url.pathname !== "/" ||
    url.search ||
    url.hash
  ) {
    throw new Error(`${key} must contain an HTTP or HTTPS origin without a path.`);
  }

  return url.origin;
}

function readRequired(environment: EnvironmentValues, key: string) {
  const value = readOptional(environment[key]);
  if (value === null) throw new Error(`${key} is required.`);
  return value;
}

function readOptional(value: string | undefined) {
  const normalized = value?.trim();
  return normalized ? normalized : null;
}

function normalizeEmail(value: string) {
  return value.trim().toLowerCase();
}
