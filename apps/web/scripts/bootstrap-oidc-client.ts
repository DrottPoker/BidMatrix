import { createHash, randomBytes } from "node:crypto";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";
import { Pool } from "pg";
import {
  readRuntimeConfiguration,
} from "../src/lib/better-auth";

async function main() {
  debug("loading configuration");
  const configuration = readRuntimeConfiguration();
  const apiOrigin = parseOrigin(
    process.env.BIDMATRIX_API_PUBLIC_BASE_URL ?? "http://localhost:8080",
  );
  const pool = new Pool(configuration.pool);

  try {
    debug("checking existing client");
    const existing = await pool.query<{ id: string; clientId: string }>(
      'select "id", "clientId" from "oauthClient" where "name" = $1 limit 1',
      ["BidMatrix API"],
    );
    const existingClient = existing.rows[0];
    const rotateExisting =
      process.env.BIDMATRIX_ROTATE_OIDC_CLIENT === "true";
    if (existingClient && !rotateExisting) {
      throw new Error(
        "BidMatrix API already has a Better Auth OIDC client. Rotate it deliberately instead of creating a duplicate.",
      );
    }

    debug(existingClient ? "rotating OIDC client" : "creating OIDC client");
    const clientId = existingClient?.clientId ?? randomBytes(16).toString("hex");
    const clientSecret = randomBytes(32).toString("base64url");
    const storedClientSecret = createHash("sha256")
      .update(clientSecret, "utf8")
      .digest("base64url");

    const project =
      process.env.BIDMATRIX_DOTNET_PROJECT ??
      resolve(
        dirname(fileURLToPath(import.meta.url)),
        "../../../src/backend/BidMatrix.Api",
      );
    const authority = `${configuration.baseUrl}/api/auth`;
    const settings: Record<string, string> = {
      BIDMATRIX_OIDC_ENABLED: "true",
      BIDMATRIX_OIDC_PROVIDER_NAME: "Better Auth",
      BIDMATRIX_OIDC_AUTHORITY: authority,
      BIDMATRIX_OIDC_CLIENT_ID: clientId,
      BIDMATRIX_OIDC_CLIENT_SECRET: clientSecret,
      BIDMATRIX_OIDC_REQUIRE_HTTPS_METADATA: authority.startsWith("https://")
        ? "true"
        : "false",
    };

    await pool.query("begin");
    try {
      const scopes = JSON.stringify(["openid", "profile", "email"]);
      const redirectUris = JSON.stringify([`${apiOrigin}/signin-oidc`]);
      const grantTypes = JSON.stringify(["authorization_code"]);
      const responseTypes = JSON.stringify(["code"]);

      if (existingClient) {
        await pool.query(
          `update "oauthClient"
           set "clientSecret" = $1,
               "disabled" = false,
               "skipConsent" = true,
               "enableEndSession" = false,
               "subjectType" = 'public',
               "scopes" = $2::jsonb,
               "clientCredentialsScopes" = '[]'::jsonb,
               "updatedAt" = now(),
               "redirectUris" = $3::jsonb,
               "postLogoutRedirectUris" = '[]'::jsonb,
               "tokenEndpointAuthMethod" = 'client_secret_post',
               "applicationType" = 'web',
               "grantTypes" = $4::jsonb,
               "responseTypes" = $5::jsonb,
               "requirePKCE" = true
           where "id" = $6`,
          [
            storedClientSecret,
            scopes,
            redirectUris,
            grantTypes,
            responseTypes,
            existingClient.id,
          ],
        );
      } else {
        await pool.query(
          `insert into "oauthClient" (
            "id",
            "clientId",
            "clientSecret",
            "disabled",
            "skipConsent",
            "enableEndSession",
            "subjectType",
            "scopes",
            "clientCredentialsScopes",
            "createdAt",
            "updatedAt",
            "name",
            "redirectUris",
            "postLogoutRedirectUris",
            "tokenEndpointAuthMethod",
            "applicationType",
            "grantTypes",
            "responseTypes",
            "requirePKCE"
          ) values (
            $1, $2, $3, false, true, false, 'public', $4::jsonb, '[]'::jsonb,
            now(), now(), $5, $6::jsonb, '[]'::jsonb, 'client_secret_post',
            'web', $7::jsonb, $8::jsonb, true
          )`,
          [
            randomBytes(16).toString("hex"),
            clientId,
            storedClientSecret,
            scopes,
            "BidMatrix API",
            redirectUris,
            grantTypes,
            responseTypes,
          ],
        );
      }

      debug("saving API configuration");
      for (const [key, value] of Object.entries(settings)) {
        saveUserSecret(project, key, value);
      }
      await pool.query("commit");
    } catch (error) {
      await pool.query("rollback");
      throw error;
    }

    process.stdout.write(
      `Better Auth OIDC client ${existingClient ? "rotated" : "created"} and BidMatrix API User Secrets updated.\n`,
    );
  } finally {
    await pool.end();
  }
}

function debug(message: string) {
  if (process.env.BIDMATRIX_AUTH_BOOTSTRAP_DEBUG === "true") {
    process.stderr.write(`[auth-bootstrap] ${message}\n`);
  }
}

function saveUserSecret(project: string, key: string, value: string) {
  const result = spawnSync(
    "dotnet",
    ["user-secrets", "set", key, value, "--project", project],
    { encoding: "utf8", stdio: "pipe" },
  );
  if (result.status !== 0) {
    throw new Error(`Could not save ${key} to .NET User Secrets.`);
  }
}

function parseOrigin(value: string) {
  const url = new URL(value);
  if (
    !["http:", "https:"].includes(url.protocol) ||
    url.username ||
    url.password ||
    url.pathname !== "/" ||
    url.search ||
    url.hash
  ) {
    throw new Error(
      "BIDMATRIX_API_PUBLIC_BASE_URL must be an HTTP or HTTPS origin.",
    );
  }

  return url.origin;
}

void main().catch((error: unknown) => {
  const message = error instanceof Error
    ? `${error.name}: ${error.message}`
    : "OIDC bootstrap failed.";
  process.stderr.write(`${message}\n`);
  process.exitCode = 1;
});
