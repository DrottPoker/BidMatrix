import { Pool } from "pg";
import {
  createBetterAuth,
  readRuntimeConfiguration,
} from "../src/lib/better-auth";

const SIGNING_ALGORITHM = "ES256";

async function main() {
  const configuration = readRuntimeConfiguration();
  const pool = new Pool(configuration.pool);

  try {
    await pool.query(
      `update "jwks"
       set "expiresAt" = now()
       where coalesce("alg", 'EdDSA') <> $1
         and ("expiresAt" is null or "expiresAt" > now())`,
      [SIGNING_ALGORITHM],
    );

    const auth = createBetterAuth(configuration, pool);
    const result = await auth.api.signJWT({
      body: {
        payload: {
          aud: "bidmatrix-signing-key-rotation",
          exp: Math.floor(Date.now() / 1_000) + 60,
          sub: "bidmatrix-signing-key-rotation",
        },
      },
    });
    const header = parseProtectedHeader(result.token);
    if (header.alg !== SIGNING_ALGORITHM) {
      throw new Error(
        `Better Auth minted an unexpected signing algorithm: ${String(header.alg)}.`,
      );
    }

    process.stdout.write(
      `Better Auth ${SIGNING_ALGORITHM} signing key is active.\n`,
    );
  } finally {
    await pool.end();
  }
}

function parseProtectedHeader(token: string) {
  const segment = token.split(".", 1)[0];
  if (!segment) throw new Error("Better Auth returned an invalid JWT.");
  return JSON.parse(Buffer.from(segment, "base64url").toString("utf8")) as {
    alg?: unknown;
  };
}

void main().catch((error: unknown) => {
  const message = error instanceof Error
    ? `${error.name}: ${error.message}`
    : "Signing-key rotation failed.";
  process.stderr.write(`${message}\n`);
  process.exitCode = 1;
});
