create extension if not exists pgcrypto;

create table users (
    id uuid primary key,
    email text unique not null,
    normalized_email text unique not null,
    display_name text null,
    status text not null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    last_login_at timestamptz null
);

create table organizations (
    id uuid primary key,
    name text not null,
    slug text unique not null,
    status text not null,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create table organization_memberships (
    id uuid primary key,
    organization_id uuid not null references organizations(id),
    user_id uuid not null references users(id),
    role text not null check (role in ('owner', 'admin', 'member', 'viewer')),
    created_at timestamptz not null,
    unique (organization_id, user_id)
);

create table user_platform_roles (
    user_id uuid not null references users(id),
    role text not null check (role in ('platform_owner', 'platform_operator', 'platform_auditor')),
    created_at timestamptz not null,
    primary key (user_id, role)
);

create table analyses (
    id uuid primary key,
    organization_id uuid not null references organizations(id),
    title text null,
    status text not null check (status in (
        'draft',
        'uploading',
        'queued',
        'validating',
        'quarantined',
        'ready_for_processing',
        'processing',
        'requires_review',
        'completed',
        'failed',
        'cancelled'
    )),
    source_language text not null default 'en',
    created_by_user_id uuid not null references users(id),
    workflow_id text null,
    requires_human_review boolean not null default true,
    failure_code text null,
    failure_message text null,
    created_at timestamptz not null,
    started_at timestamptz null,
    completed_at timestamptz null,
    updated_at timestamptz not null,
    version integer not null check (version > 0),
    unique (id, organization_id)
);

create table analysis_files (
    id uuid primary key,
    organization_id uuid not null references organizations(id),
    analysis_id uuid not null,
    original_file_name text not null,
    content_type text not null,
    size_bytes bigint not null check (size_bytes >= 0),
    sha256 text not null check (sha256 ~ '^[0-9a-f]{64}$'),
    storage_bucket text not null,
    storage_key text not null,
    scan_status text not null check (scan_status in ('pending', 'clean', 'blocked', 'failed', 'development_bypass')),
    document_type text null,
    page_count integer null check (page_count is null or page_count > 0),
    created_at timestamptz not null,
    updated_at timestamptz not null,
    unique (storage_bucket, storage_key),
    unique (id, organization_id),
    unique (id, analysis_id, organization_id),
    foreign key (analysis_id, organization_id) references analyses(id, organization_id)
);

create table analysis_requirements (
    id uuid primary key,
    organization_id uuid not null references organizations(id),
    analysis_id uuid not null,
    requirement_code text null,
    requirement_text text not null,
    normalized_requirement text not null,
    category text not null,
    mandatory boolean null,
    requested_evidence text null,
    confidence numeric(5,4) null check (confidence between 0 and 1),
    review_status text not null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    unique (id, organization_id),
    unique (id, analysis_id, organization_id),
    foreign key (analysis_id, organization_id) references analyses(id, organization_id)
);

create table analysis_citations (
    id uuid primary key,
    organization_id uuid not null references organizations(id),
    analysis_id uuid not null,
    requirement_id uuid null,
    analysis_file_id uuid not null,
    page_number integer null check (page_number is null or page_number > 0),
    section_text text null,
    quote_text text null,
    bounding_data jsonb null,
    created_at timestamptz not null,
    foreign key (analysis_id, organization_id) references analyses(id, organization_id),
    foreign key (requirement_id, analysis_id, organization_id) references analysis_requirements(id, analysis_id, organization_id),
    foreign key (analysis_file_id, analysis_id, organization_id) references analysis_files(id, analysis_id, organization_id)
);

create table company_profiles (
    id uuid primary key,
    organization_id uuid unique not null references organizations(id),
    services jsonb not null,
    delivery_regions jsonb not null,
    contract_limits jsonb not null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    version integer not null check (version > 0)
);

create table evidence_items (
    id uuid primary key,
    organization_id uuid not null references organizations(id),
    type text not null,
    name text not null,
    status text not null,
    issued_at timestamptz null,
    expires_at timestamptz null,
    metadata jsonb not null,
    storage_key text null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    version integer not null check (version > 0),
    unique (id, organization_id)
);

create table requirement_evidence_matches (
    id uuid primary key,
    organization_id uuid not null references organizations(id),
    requirement_id uuid not null,
    evidence_item_id uuid null,
    status text not null,
    confidence numeric(5,4) null check (confidence between 0 and 1),
    reason text null,
    requires_review boolean not null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    foreign key (requirement_id, organization_id) references analysis_requirements(id, organization_id),
    foreign key (evidence_item_id, organization_id) references evidence_items(id, organization_id)
);

create table goals (
    id uuid primary key,
    title text not null,
    description text not null,
    metric_key text null,
    target_value numeric null,
    target_date timestamptz null,
    status text not null,
    constraints jsonb not null,
    created_by_user_id uuid not null references users(id),
    created_at timestamptz not null,
    updated_at timestamptz not null,
    version integer not null check (version > 0)
);

create table tasks (
    id uuid primary key,
    organization_id uuid null references organizations(id),
    goal_id uuid null references goals(id),
    parent_task_id uuid null,
    type text not null,
    title text not null,
    description text not null,
    priority text not null,
    status text not null check (status in (
        'queued',
        'assigned',
        'running',
        'waiting_for_input',
        'waiting_for_approval',
        'completed',
        'failed',
        'cancelled'
    )),
    assigned_agent_key text null,
    input jsonb not null,
    constraints jsonb not null,
    result_artifact_id uuid null,
    error_code text null,
    error_message text null,
    created_by_type text not null,
    created_by_id text not null,
    created_at timestamptz not null,
    started_at timestamptz null,
    completed_at timestamptz null,
    updated_at timestamptz not null,
    version integer not null check (version > 0),
    unique (id, organization_id),
    foreign key (parent_task_id, organization_id) references tasks(id, organization_id)
);

create table task_dependencies (
    task_id uuid not null references tasks(id),
    depends_on_task_id uuid not null references tasks(id),
    dependency_type text not null,
    primary key (task_id, depends_on_task_id),
    check (task_id <> depends_on_task_id)
);

create table agent_definitions (
    id uuid primary key,
    agent_key text unique not null,
    display_name text not null,
    description text not null,
    status text not null,
    active_version_id uuid null,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create table agent_versions (
    id uuid primary key,
    agent_definition_id uuid not null references agent_definitions(id),
    version_number integer not null check (version_number > 0),
    prompt_version text not null,
    model_key text not null,
    tool_permissions jsonb not null,
    input_schema_name text not null,
    output_schema_name text not null,
    configuration jsonb not null,
    created_at timestamptz not null,
    unique (agent_definition_id, version_number)
);

alter table agent_definitions
    add constraint agent_definitions_active_version_fk
    foreign key (active_version_id) references agent_versions(id);

create table workflow_runs (
    id uuid primary key,
    workflow_type text not null,
    workflow_id text unique not null,
    temporal_run_id text null,
    task_id uuid null references tasks(id),
    status text not null,
    input jsonb not null,
    result jsonb null,
    started_at timestamptz not null,
    completed_at timestamptz null,
    updated_at timestamptz not null
);

create table agent_runs (
    id uuid primary key,
    workflow_run_id uuid null references workflow_runs(id),
    task_id uuid null references tasks(id),
    agent_version_id uuid not null references agent_versions(id),
    status text not null,
    input_artifact_id uuid null,
    output_artifact_id uuid null,
    model_name text not null,
    prompt_version text not null,
    trace_id text null,
    input_hash text not null check (input_hash ~ '^[0-9a-f]{64}$'),
    output_hash text null check (output_hash is null or output_hash ~ '^[0-9a-f]{64}$'),
    request_count integer not null default 0 check (request_count >= 0),
    input_tokens bigint not null default 0 check (input_tokens >= 0),
    output_tokens bigint not null default 0 check (output_tokens >= 0),
    reasoning_tokens bigint null check (reasoning_tokens is null or reasoning_tokens >= 0),
    estimated_cost_minor bigint null check (estimated_cost_minor is null or estimated_cost_minor >= 0),
    estimated_cost_currency text null,
    started_at timestamptz not null,
    completed_at timestamptz null,
    failure_code text null,
    failure_message text null
);

create table policies (
    id uuid primary key,
    policy_key text unique not null,
    display_name text not null,
    description text not null,
    active_version_id uuid null,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create table policy_versions (
    id uuid primary key,
    policy_id uuid not null references policies(id),
    version_number integer not null check (version_number > 0),
    rules jsonb not null,
    created_by_user_id uuid not null references users(id),
    created_at timestamptz not null,
    unique (policy_id, version_number)
);

alter table policies
    add constraint policies_active_version_fk
    foreign key (active_version_id) references policy_versions(id);

create table tool_definitions (
    id uuid primary key,
    tool_key text unique not null,
    display_name text not null,
    description text not null,
    risk_level text not null check (risk_level in ('green', 'yellow', 'red', 'prohibited')),
    side_effect_class text not null check (side_effect_class in (
        'read_only',
        'internal_write',
        'external_reversible',
        'external_material',
        'destructive'
    )),
    enabled boolean not null,
    approval_mode text not null check (approval_mode in ('none', 'always', 'policy', 'disabled')),
    input_schema jsonb not null,
    output_schema jsonb not null,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create table tool_calls (
    id uuid primary key,
    agent_run_id uuid null references agent_runs(id),
    task_id uuid null references tasks(id),
    tool_definition_id uuid not null references tool_definitions(id),
    status text not null,
    normalized_input jsonb not null,
    input_hash text not null check (input_hash ~ '^[0-9a-f]{64}$'),
    idempotency_key text not null,
    policy_version_id uuid not null references policy_versions(id),
    approval_id uuid null,
    output jsonb null,
    external_reference text null,
    requested_at timestamptz not null,
    executed_at timestamptz null,
    completed_at timestamptz null,
    failure_code text null,
    failure_message text null,
    unique (tool_definition_id, idempotency_key)
);

create table approvals (
    id uuid primary key,
    tool_call_id uuid null references tool_calls(id),
    task_id uuid null references tasks(id),
    action_type text not null,
    status text not null check (status in ('pending', 'approved', 'rejected', 'expired', 'cancelled', 'invalidated')),
    summary text not null,
    normalized_payload jsonb not null,
    payload_hash text not null check (payload_hash ~ '^[0-9a-f]{64}$'),
    policy_version_id uuid not null references policy_versions(id),
    requested_by_agent_run_id uuid null references agent_runs(id),
    requested_at timestamptz not null,
    expires_at timestamptz not null,
    decided_by_user_id uuid null references users(id),
    decided_at timestamptz null,
    decision_note text null,
    execution_status text null,
    version integer not null check (version > 0),
    check (expires_at > requested_at)
);

alter table tool_calls
    add constraint tool_calls_approval_fk
    foreign key (approval_id) references approvals(id);

create table artifacts (
    id uuid primary key,
    organization_id uuid null references organizations(id),
    artifact_type text not null,
    title text not null,
    content_type text not null,
    storage_bucket text null,
    storage_key text null,
    inline_content jsonb null,
    sha256 text not null check (sha256 ~ '^[0-9a-f]{64}$'),
    sensitivity text not null,
    created_by_type text not null,
    created_by_id text not null,
    created_at timestamptz not null,
    supersedes_artifact_id uuid null references artifacts(id),
    unique (id, organization_id),
    check (storage_key is not null or inline_content is not null)
);

alter table tasks
    add constraint tasks_result_artifact_fk
    foreign key (result_artifact_id, organization_id) references artifacts(id, organization_id);

alter table agent_runs
    add constraint agent_runs_input_artifact_fk
        foreign key (input_artifact_id) references artifacts(id),
    add constraint agent_runs_output_artifact_fk
        foreign key (output_artifact_id) references artifacts(id);

create table outbox_events (
    id uuid primary key,
    event_type text not null,
    aggregate_type text not null,
    aggregate_id uuid not null,
    payload jsonb not null,
    occurred_at timestamptz not null,
    available_at timestamptz not null,
    lease_owner text null,
    lease_expires_at timestamptz null,
    attempt_count integer not null default 0 check (attempt_count >= 0),
    processed_at timestamptz null,
    dead_lettered_at timestamptz null,
    last_error text null
);

create sequence audit_event_sequence;

create table audit_events (
    id uuid primary key,
    sequence_number bigint unique not null default nextval('audit_event_sequence'),
    actor_type text not null,
    actor_id text not null,
    action text not null,
    target_type text null,
    target_id text null,
    organization_id uuid null references organizations(id),
    request_id text null,
    trace_id text null,
    summary text not null,
    metadata jsonb not null,
    previous_hash text null check (previous_hash is null or previous_hash ~ '^[0-9a-f]{64}$'),
    event_hash text unique not null check (event_hash ~ '^[0-9a-f]{64}$'),
    created_at timestamptz not null
);

create table system_controls (
    control_key text primary key,
    enabled boolean not null,
    value jsonb not null,
    updated_by_user_id uuid not null references users(id),
    updated_at timestamptz not null,
    version integer not null check (version > 0)
);

create index organization_memberships_organization_id_user_id_idx
    on organization_memberships (organization_id, user_id);
create index analyses_organization_id_created_at_idx
    on analyses (organization_id, created_at);
create index analysis_files_organization_id_analysis_id_idx
    on analysis_files (organization_id, analysis_id);
create index analysis_requirements_organization_id_analysis_id_idx
    on analysis_requirements (organization_id, analysis_id);
create index analysis_citations_organization_id_analysis_id_idx
    on analysis_citations (organization_id, analysis_id);
create index evidence_items_organization_id_status_idx
    on evidence_items (organization_id, status);
create index requirement_evidence_matches_organization_id_requirement_id_idx
    on requirement_evidence_matches (organization_id, requirement_id);
create index tasks_organization_id_status_idx
    on tasks (organization_id, status);
create index artifacts_organization_id_created_at_idx
    on artifacts (organization_id, created_at);
create index outbox_events_available_idx
    on outbox_events (processed_at, dead_lettered_at, available_at);
create index audit_events_organization_id_sequence_number_idx
    on audit_events (organization_id, sequence_number);

create function current_organization_id()
returns uuid
language sql
stable
as $$
    select nullif(current_setting('app.organization_id', true), '')::uuid
$$;

create function current_platform_access()
returns boolean
language sql
stable
as $$
    select coalesce(nullif(current_setting('app.platform_access', true), '')::boolean, false)
$$;

alter table organizations enable row level security;
alter table organization_memberships enable row level security;
alter table analyses enable row level security;
alter table analysis_files enable row level security;
alter table analysis_requirements enable row level security;
alter table analysis_citations enable row level security;
alter table company_profiles enable row level security;
alter table evidence_items enable row level security;
alter table requirement_evidence_matches enable row level security;
alter table tasks enable row level security;
alter table task_dependencies enable row level security;
alter table artifacts enable row level security;

alter table organizations force row level security;
alter table organization_memberships force row level security;
alter table analyses force row level security;
alter table analysis_files force row level security;
alter table analysis_requirements force row level security;
alter table analysis_citations force row level security;
alter table company_profiles force row level security;
alter table evidence_items force row level security;
alter table requirement_evidence_matches force row level security;
alter table tasks force row level security;
alter table task_dependencies force row level security;
alter table artifacts force row level security;

create policy tenant_organizations on organizations
    using (id = current_organization_id())
    with check (id = current_organization_id());
create policy tenant_memberships on organization_memberships
    using (organization_id = current_organization_id())
    with check (organization_id = current_organization_id());
create policy tenant_analyses on analyses
    using (organization_id = current_organization_id())
    with check (organization_id = current_organization_id());
create policy tenant_analysis_files on analysis_files
    using (organization_id = current_organization_id())
    with check (organization_id = current_organization_id());
create policy tenant_analysis_requirements on analysis_requirements
    using (organization_id = current_organization_id())
    with check (organization_id = current_organization_id());
create policy tenant_analysis_citations on analysis_citations
    using (organization_id = current_organization_id())
    with check (organization_id = current_organization_id());
create policy tenant_company_profiles on company_profiles
    using (organization_id = current_organization_id())
    with check (organization_id = current_organization_id());
create policy tenant_evidence_items on evidence_items
    using (organization_id = current_organization_id())
    with check (organization_id = current_organization_id());
create policy tenant_requirement_evidence_matches on requirement_evidence_matches
    using (organization_id = current_organization_id())
    with check (organization_id = current_organization_id());
create policy tenant_tasks on tasks
    using (
        organization_id = current_organization_id()
        or (organization_id is null and current_platform_access())
    )
    with check (
        organization_id = current_organization_id()
        or (organization_id is null and current_platform_access())
    );
create policy tenant_task_dependencies on task_dependencies
    using (
        exists (select 1 from tasks where tasks.id = task_dependencies.task_id)
        and exists (select 1 from tasks where tasks.id = task_dependencies.depends_on_task_id)
    )
    with check (
        exists (select 1 from tasks where tasks.id = task_dependencies.task_id)
        and exists (select 1 from tasks where tasks.id = task_dependencies.depends_on_task_id)
    );
create policy tenant_artifacts on artifacts
    using (
        organization_id = current_organization_id()
        or (organization_id is null and current_platform_access())
    )
    with check (
        organization_id = current_organization_id()
        or (organization_id is null and current_platform_access())
    );

create function reject_audit_event_mutation()
returns trigger
language plpgsql
as $$
begin
    raise exception using
        errcode = '55000',
        message = 'Audit events are append-only.';
end;
$$;

create trigger audit_events_reject_update_or_delete
before update or delete on audit_events
for each row execute function reject_audit_event_mutation();

create function append_audit_event(
    p_id uuid,
    p_actor_type text,
    p_actor_id text,
    p_action text,
    p_target_type text,
    p_target_id text,
    p_organization_id uuid,
    p_request_id text,
    p_trace_id text,
    p_summary text,
    p_metadata jsonb,
    p_created_at timestamptz
)
returns audit_events
language plpgsql
security definer
set search_path = public, pg_temp
as $$
declare
    prior_hash text;
    calculated_hash text;
    inserted_event audit_events;
begin
    perform pg_advisory_xact_lock(49089055130002);

    select event_hash
    into prior_hash
    from audit_events
    order by sequence_number desc
    limit 1;

    calculated_hash := encode(
        digest(
            jsonb_build_object(
                'id', p_id,
                'actorType', p_actor_type,
                'actorId', p_actor_id,
                'action', p_action,
                'targetType', p_target_type,
                'targetId', p_target_id,
                'organizationId', p_organization_id,
                'requestId', p_request_id,
                'traceId', p_trace_id,
                'summary', p_summary,
                'metadata', p_metadata,
                'previousHash', prior_hash,
                'createdAt', p_created_at
            )::text,
            'sha256'
        ),
        'hex'
    );

    insert into audit_events (
        id,
        actor_type,
        actor_id,
        action,
        target_type,
        target_id,
        organization_id,
        request_id,
        trace_id,
        summary,
        metadata,
        previous_hash,
        event_hash,
        created_at
    )
    values (
        p_id,
        p_actor_type,
        p_actor_id,
        p_action,
        p_target_type,
        p_target_id,
        p_organization_id,
        p_request_id,
        p_trace_id,
        p_summary,
        p_metadata,
        prior_hash,
        calculated_hash,
        p_created_at
    )
    returning * into inserted_event;

    return inserted_event;
end;
$$;

revoke all on function append_audit_event(uuid, text, text, text, text, text, uuid, text, text, text, jsonb, timestamptz) from public;

grant usage on schema public to {{APP_ROLE}};
grant select, insert, update, delete on all tables in schema public to {{APP_ROLE}};
grant usage, select on all sequences in schema public to {{APP_ROLE}};
revoke insert, update, delete on audit_events from {{APP_ROLE}};
revoke insert, update, delete on schema_migrations from {{APP_ROLE}};

grant usage on schema public to {{AUDIT_ROLE}};
grant execute on function append_audit_event(uuid, text, text, text, text, text, uuid, text, text, text, jsonb, timestamptz) to {{AUDIT_ROLE}};
