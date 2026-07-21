create table engineering_sandboxes (
    id uuid primary key,
    organization_id uuid not null references organizations(id),
    task_id uuid not null references tasks(id),
    sandbox_key text not null,
    base_revision text not null,
    head_revision text not null,
    status text not null check (status in ('active', 'retained', 'failed', 'expired')),
    created_at timestamptz not null,
    updated_at timestamptz not null,
    retain_until timestamptz not null,
    unique (organization_id, task_id),
    unique (sandbox_key)
);

alter table engineering_sandboxes enable row level security;
alter table engineering_sandboxes force row level security;

create policy tenant_engineering_sandboxes on engineering_sandboxes
    using (organization_id = current_organization_id() or current_platform_access())
    with check (organization_id = current_organization_id() or current_platform_access());

grant select, insert, update, delete on engineering_sandboxes to {{APP_ROLE}};
