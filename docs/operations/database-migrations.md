# Database migrations

BidMatrix stores Phase 2 schema changes as ordered SQL files embedded in `BidMatrix.Database`.

## Development

The API applies pending migrations during Development startup. Docker Compose creates a database owner role from `POSTGRES_USER` and a separate application role named `bidmatrix_app` from `infra/postgres/init/001-create-application-role.sh`.

The API uses:

- `POSTGRES_USER` and `POSTGRES_PASSWORD` for Development migration execution.
- `POSTGRES_APP_USER` and `POSTGRES_APP_PASSWORD` for normal application connections.

The application role must not own the database and must not have `BYPASSRLS`.

## Rollback strategy

Use forward-fix migrations. Do not rewrite already-applied migration files after they have been shared, because audit, policy, and approval records are security-sensitive. If a bad migration is applied, add a later migration that corrects the schema or seed data while preserving auditability.

## Non-Development

Automatic migration execution is disabled outside Development. Cloud deployment must run migrations as a controlled release step with an owner or deployment role, then run the API with the normal application role.
