alter table workflow_runs
    add column organization_id uuid null;

update workflow_runs workflow
set organization_id = task.organization_id
from tasks task
where task.id = workflow.task_id;

alter table workflow_runs
    alter column organization_id set not null,
    add constraint workflow_runs_organization_id_fkey
        foreign key (organization_id) references organizations(id);

alter table agent_runs
    add column organization_id uuid null;

update agent_runs run
set organization_id = task.organization_id
from tasks task
where task.id = run.task_id;

alter table agent_runs
    alter column organization_id set not null,
    add constraint agent_runs_organization_id_fkey
        foreign key (organization_id) references organizations(id),
    add constraint agent_runs_task_id_key unique (task_id);

create index workflow_runs_organization_id_started_at_idx
    on workflow_runs (organization_id, started_at desc);
create index agent_runs_organization_id_started_at_idx
    on agent_runs (organization_id, started_at desc);

alter table workflow_runs enable row level security;
alter table agent_runs enable row level security;
alter table workflow_runs force row level security;
alter table agent_runs force row level security;

create policy tenant_workflow_runs on workflow_runs
    using (organization_id = current_organization_id() or current_platform_access())
    with check (organization_id = current_organization_id() or current_platform_access());

create policy tenant_agent_runs on agent_runs
    using (organization_id = current_organization_id() or current_platform_access())
    with check (organization_id = current_organization_id() or current_platform_access());

grant select, insert, update, delete on workflow_runs, agent_runs to {{APP_ROLE}};
