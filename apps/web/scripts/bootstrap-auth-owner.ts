import { Pool } from "pg";
import { OWNER_BOOTSTRAP_HEADER } from "../src/lib/auth-contract";
import {
  createBetterAuth,
  readRuntimeConfiguration,
} from "../src/lib/better-auth";

async function main() {
  const configuration = readRuntimeConfiguration();
  const email = readRequired("BETTER_AUTH_OWNER_EMAIL");
  const password = readRequired("BETTER_AUTH_OWNER_PASSWORD");
  const name = readRequired("BETTER_AUTH_OWNER_NAME");
  const bootstrapSecret = readRequired("BETTER_AUTH_OWNER_BOOTSTRAP_SECRET");
  const pool = new Pool(configuration.pool);

  try {
    const existing = await pool.query(
      'select 1 from "user" where lower("email") = lower($1) limit 1',
      [email],
    );
    if (existing.rowCount) {
      throw new Error(
        "The Better Auth owner identity already exists. Use account recovery instead of bootstrapping again.",
      );
    }

    const auth = createBetterAuth(configuration, pool);
    await auth.api.signUpEmail({
      body: { email, name, password },
      headers: new Headers({ [OWNER_BOOTSTRAP_HEADER]: bootstrapSecret }),
    });
    process.stdout.write("Better Auth owner identity created.\n");
  } finally {
    await pool.end();
  }
}

function readRequired(key: string) {
  const value = process.env[key]?.trim();
  if (!value) throw new Error(`${key} is required.`);
  return value;
}

void main().catch((error: unknown) => {
  const message = error instanceof Error
    ? `${error.name}: ${error.message}`
    : "Owner bootstrap failed.";
  process.stderr.write(`${message}\n`);
  process.exitCode = 1;
});
