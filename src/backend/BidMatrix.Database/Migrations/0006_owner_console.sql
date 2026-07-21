alter table tasks
    add column owner_idempotency_key text null;

create unique index tasks_owner_idempotency_key_idx
    on tasks (organization_id, owner_idempotency_key)
    where owner_idempotency_key is not null;

create index goals_status_updated_at_idx
    on goals (status, updated_at desc);

create index audit_events_created_at_idx
    on audit_events (created_at desc);
