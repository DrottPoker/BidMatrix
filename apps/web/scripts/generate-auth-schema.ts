import { getMigrations } from "better-auth/db/migration";
import { Pool } from "pg";
import {
  createBetterAuth,
  readRuntimeConfiguration,
} from "../src/lib/better-auth";

async function main() {
  const configuration = readRuntimeConfiguration();
  const pool = new Pool(configuration.pool);

  try {
    const auth = createBetterAuth(configuration, pool);
    const migration = await getMigrations(auth.options);
    const sql = await migration.compileMigrations();

    if (process.argv.includes("--check")) {
      if (sql.replace(/[;\s]/g, "").length > 0) {
        throw new Error(
          "Better Auth database schema differs from the configured runtime schema.",
        );
      }

      process.stdout.write("Better Auth schema matches.\n");
      return;
    }

    process.stdout.write(sql);
  } finally {
    await pool.end();
  }
}

void main().catch((error: unknown) => {
  console.error(error instanceof Error ? error.message : "Auth schema generation failed.");
  process.exitCode = 1;
});
