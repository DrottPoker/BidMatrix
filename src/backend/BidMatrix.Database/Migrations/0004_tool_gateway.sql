alter table tool_calls
    add column request_id uuid not null default gen_random_uuid(),
    add column organization_id uuid null,
    add column decision text null,
    add column reason_code text null,
    add column execution_status text null,
    add column correlation_id text null;

update tool_calls call
set organization_id = task.organization_id
from tasks task
where task.id = call.task_id;

alter table tool_calls
    alter column organization_id set not null,
    add constraint tool_calls_organization_id_fkey
        foreign key (organization_id) references organizations(id),
    add constraint tool_calls_decision_check
        check (decision is null or decision in (
            'allowed',
            'denied',
            'approvalRequired',
            'disabled',
            'alreadyExecuted',
            'invalid'
        )),
    add constraint tool_calls_execution_status_check
        check (execution_status is null or execution_status in (
            'notStarted',
            'pending',
            'completed',
            'disabled',
            'failed'
        ));

alter table tool_calls
    drop constraint tool_calls_tool_definition_id_idempotency_key_key;

alter table tool_calls
    add constraint tool_calls_organization_tool_idempotency_key
        unique (organization_id, tool_definition_id, idempotency_key),
    add constraint tool_calls_request_id_key unique (request_id);

alter table approvals
    add column organization_id uuid null,
    add column presentation jsonb not null default '{}'::jsonb,
    add column supersedes_approval_id uuid null references approvals(id);

update approvals approval
set organization_id = task.organization_id
from tasks task
where task.id = approval.task_id;

update approvals approval
set organization_id = call.organization_id
from tool_calls call
where call.id = approval.tool_call_id
  and approval.organization_id is null;

alter table approvals
    alter column organization_id set not null,
    add constraint approvals_organization_id_fkey
        foreign key (organization_id) references organizations(id),
    add constraint approvals_execution_status_check
        check (execution_status is null or execution_status in (
            'notStarted',
            'pending',
            'disabled',
            'completed',
            'failed'
        ));

create index tool_calls_organization_id_requested_at_idx
    on tool_calls (organization_id, requested_at desc);
create index approvals_organization_id_status_expires_at_idx
    on approvals (organization_id, status, expires_at);

alter table tool_calls enable row level security;
alter table approvals enable row level security;
alter table tool_calls force row level security;
alter table approvals force row level security;

create policy tenant_tool_calls on tool_calls
    using (organization_id = current_organization_id() or current_platform_access())
    with check (organization_id = current_organization_id() or current_platform_access());

create policy tenant_approvals on approvals
    using (organization_id = current_organization_id() or current_platform_access())
    with check (organization_id = current_organization_id() or current_platform_access());

grant select, insert, update, delete on tool_calls, approvals to {{APP_ROLE}};
