insert into users (
    id,
    email,
    normalized_email,
    display_name,
    status,
    created_at,
    updated_at
)
values (
    '01982000-0000-7000-8000-000000000001',
    'owner@example.invalid',
    'OWNER@EXAMPLE.INVALID',
    'Development Owner',
    'active',
    now(),
    now()
)
on conflict (id) do nothing;

insert into organizations (id, name, slug, status, created_at, updated_at)
values (
    '01982000-0000-7000-8000-000000000101',
    'BidMatrix Development Organization',
    'bidmatrix-development',
    'active',
    now(),
    now()
)
on conflict (id) do nothing;

insert into organization_memberships (id, organization_id, user_id, role, created_at)
values (
    '01982000-0000-7000-8000-000000000201',
    '01982000-0000-7000-8000-000000000101',
    '01982000-0000-7000-8000-000000000001',
    'owner',
    now()
)
on conflict (organization_id, user_id) do nothing;

insert into user_platform_roles (user_id, role, created_at)
values (
    '01982000-0000-7000-8000-000000000001',
    'platform_owner',
    now()
)
on conflict (user_id, role) do nothing;

with agent_seed(agent_key, display_name, description, input_schema_name, output_schema_name) as (
    values
        (
            'executive',
            'Executive Agent',
            'Prepares owner-facing strategic briefs and internal operating summaries.',
            'ExecutiveAgentInput',
            'ExecutiveAgentOutput'
        ),
        (
            'support',
            'Support Agent',
            'Drafts support responses without sending external communications.',
            'SupportAgentInput',
            'SupportAgentOutput'
        ),
        (
            'product-analyst',
            'Product Analyst Agent',
            'Reviews product signals and drafts scoped product analysis.',
            'ProductAnalystAgentInput',
            'ProductAnalystAgentOutput'
        ),
        (
            'engineering',
            'Engineering Agent',
            'Plans isolated engineering changes and draft pull-request artifacts.',
            'EngineeringAgentInput',
            'EngineeringAgentOutput'
        )
), upserted_definitions as (
    insert into agent_definitions (
        id,
        agent_key,
        display_name,
        description,
        status,
        created_at,
        updated_at
    )
    select
        gen_random_uuid(),
        agent_key,
        display_name,
        description,
        'active',
        now(),
        now()
    from agent_seed
    on conflict (agent_key) do update
    set display_name = excluded.display_name,
        description = excluded.description,
        updated_at = now()
    returning id, agent_key
)
insert into agent_versions (
    id,
    agent_definition_id,
    version_number,
    prompt_version,
    model_key,
    tool_permissions,
    input_schema_name,
    output_schema_name,
    configuration,
    created_at
)
select
    gen_random_uuid(),
    definition.id,
    1,
    'f0-development-v1',
    definition.agent_key || '-deterministic',
    '[]'::jsonb,
    seed.input_schema_name,
    seed.output_schema_name,
    '{"mode":"deterministicOfflineDemo"}'::jsonb,
    now()
from upserted_definitions definition
join agent_seed seed using (agent_key)
on conflict (agent_definition_id, version_number) do nothing;

update agent_definitions definition
set active_version_id = version.id,
    updated_at = now()
from agent_versions version
where version.agent_definition_id = definition.id
  and version.version_number = 1;

insert into policies (
    id,
    policy_key,
    display_name,
    description,
    created_at,
    updated_at
)
values (
    '01982000-0000-7000-8000-000000000301',
    'f0-draft-only',
    'F0 draft-only policy',
    'External effects are disabled or approval-gated during Foundation Release F0.',
    now(),
    now()
)
on conflict (policy_key) do update
set display_name = excluded.display_name,
    description = excluded.description,
    updated_at = now();

insert into policy_versions (
    id,
    policy_id,
    version_number,
    rules,
    created_by_user_id,
    created_at
)
values (
    '01982000-0000-7000-8000-000000000302',
    '01982000-0000-7000-8000-000000000301',
    1,
    '{"externalToolExecutionEnabled":false,"systemDraftOnlyMode":true,"ownerApprovalRequired":["emailSending","externalPublication","spending","deployment","githubRemoteAction"]}'::jsonb,
    '01982000-0000-7000-8000-000000000001',
    now()
)
on conflict (policy_id, version_number) do nothing;

update policies
set active_version_id = '01982000-0000-7000-8000-000000000302',
    updated_at = now()
where id = '01982000-0000-7000-8000-000000000301';

insert into tool_definitions (
    id,
    tool_key,
    display_name,
    description,
    risk_level,
    side_effect_class,
    enabled,
    approval_mode,
    input_schema,
    output_schema,
    created_at,
    updated_at
)
values
    (
        '01982000-0000-7000-8000-000000000401',
        'artifact.createDraft',
        'Create draft artifact',
        'Creates an internal draft artifact.',
        'green',
        'internal_write',
        true,
        'none',
        '{}'::jsonb,
        '{}'::jsonb,
        now(),
        now()
    ),
    (
        '01982000-0000-7000-8000-000000000402',
        'external.email.send',
        'Send email',
        'Disabled F0 placeholder for outbound email.',
        'red',
        'external_material',
        false,
        'disabled',
        '{}'::jsonb,
        '{}'::jsonb,
        now(),
        now()
    ),
    (
        '01982000-0000-7000-8000-000000000403',
        'github.pullRequest.create',
        'Create pull request',
        'Disabled F0 placeholder for remote pull-request creation.',
        'red',
        'external_material',
        false,
        'disabled',
        '{}'::jsonb,
        '{}'::jsonb,
        now(),
        now()
    )
on conflict (tool_key) do update
set enabled = excluded.enabled,
    approval_mode = excluded.approval_mode,
    updated_at = now();

insert into system_controls (
    control_key,
    enabled,
    value,
    updated_by_user_id,
    updated_at,
    version
)
values
    (
        'allAgentsEnabled',
        true,
        'true'::jsonb,
        '01982000-0000-7000-8000-000000000001',
        now(),
        1
    ),
    (
        'externalCommunicationEnabled',
        false,
        'false'::jsonb,
        '01982000-0000-7000-8000-000000000001',
        now(),
        1
    ),
    (
        'engineeringWritesEnabled',
        true,
        'true'::jsonb,
        '01982000-0000-7000-8000-000000000001',
        now(),
        1
    ),
    (
        'externalSpendingEnabled',
        false,
        'false'::jsonb,
        '01982000-0000-7000-8000-000000000001',
        now(),
        1
    ),
    (
        'externalToolExecutionEnabled',
        false,
        'false'::jsonb,
        '01982000-0000-7000-8000-000000000001',
        now(),
        1
    ),
    (
        'systemDraftOnlyMode',
        true,
        'true'::jsonb,
        '01982000-0000-7000-8000-000000000001',
        now(),
        1
    )
on conflict (control_key) do nothing;
