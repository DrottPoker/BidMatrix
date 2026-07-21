alter table analyses add column idempotency_key text null;
alter table analysis_files add column retention_until timestamptz null;
alter table tasks add column idempotency_key text null;

create unique index analyses_organization_id_idempotency_key_uidx
    on analyses (organization_id, idempotency_key)
    where idempotency_key is not null;

create unique index analysis_files_analysis_id_sha256_uidx
    on analysis_files (analysis_id, sha256);

create unique index tasks_organization_id_idempotency_key_uidx
    on tasks (organization_id, idempotency_key)
    where organization_id is not null and idempotency_key is not null;

create unique index tasks_global_idempotency_key_uidx
    on tasks (idempotency_key)
    where organization_id is null and idempotency_key is not null;

grant select, insert, update, delete on analyses to {{APP_ROLE}};
grant select, insert, update, delete on analysis_files to {{APP_ROLE}};
grant select, insert, update, delete on tasks to {{APP_ROLE}};
