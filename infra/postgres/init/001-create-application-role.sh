#!/bin/sh
set -eu

psql \
  --username "$POSTGRES_USER" \
  --dbname "$POSTGRES_DB" \
  --set=app_user="$POSTGRES_APP_USER" \
  --set=app_password="$POSTGRES_APP_PASSWORD" \
  --set=audit_user="$POSTGRES_AUDIT_USER" \
  --set=audit_password="$POSTGRES_AUDIT_PASSWORD" <<'SQL'
select format('create role %I login password %L', :'app_user', :'app_password')
where not exists (select 1 from pg_roles where rolname = :'app_user')
\gexec

select format('create role %I login password %L', :'audit_user', :'audit_password')
where not exists (select 1 from pg_roles where rolname = :'audit_user')
\gexec

select format('alter role %I nosuperuser nocreatedb nocreaterole noinherit nobypassrls', :'app_user')
\gexec

select format('alter role %I nosuperuser nocreatedb nocreaterole noinherit nobypassrls', :'audit_user')
\gexec

select format('grant connect on database %I to %I', current_database(), :'app_user')
\gexec

select format('grant connect on database %I to %I', current_database(), :'audit_user')
\gexec
SQL
