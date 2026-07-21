delete from tool_definitions definition
where definition.tool_key in ('external.email.send', 'github.pullRequest.create')
  and not exists (
      select 1 from tool_calls call where call.tool_definition_id = definition.id
  );

with catalog(tool_key, display_name, description, risk_level, side_effect_class, enabled, approval_mode) as (
    values
        ('context.getCompanyConstitution', 'Get company constitution', 'Reads the current company operating constraints.', 'green', 'read_only', true, 'none'),
        ('context.getProductFacts', 'Get product facts', 'Reads approved BidMatrix product facts.', 'green', 'read_only', true, 'none'),
        ('context.getTask', 'Get task', 'Reads a tenant-scoped task.', 'green', 'read_only', true, 'none'),
        ('context.getAnalysis', 'Get analysis', 'Reads a tenant-scoped analysis summary.', 'green', 'read_only', true, 'none'),
        ('context.getMetricsSnapshot', 'Get metrics snapshot', 'Reads a deterministic operating snapshot.', 'green', 'read_only', true, 'none'),
        ('knowledge.search', 'Search knowledge', 'Searches approved internal knowledge only.', 'green', 'read_only', true, 'none'),
        ('artifact.read', 'Read artifact', 'Reads a tenant-scoped artifact.', 'green', 'read_only', true, 'none'),
        ('repo.readFile', 'Read repository file', 'Reads an allowlisted file in the engineering sandbox.', 'yellow', 'read_only', true, 'policy'),
        ('repo.search', 'Search repository', 'Searches an allowlisted engineering sandbox.', 'yellow', 'read_only', true, 'policy'),
        ('repo.getStatus', 'Get repository status', 'Reads sandbox repository status.', 'yellow', 'read_only', true, 'policy'),
        ('repo.getDiff', 'Get repository diff', 'Reads the current isolated worktree diff.', 'yellow', 'read_only', true, 'policy'),
        ('task.create', 'Create task', 'Creates an internal tenant-scoped task.', 'green', 'internal_write', true, 'none'),
        ('task.addNote', 'Add task note', 'Adds an internal draft note artifact for a task.', 'green', 'internal_write', true, 'none'),
        ('artifact.createDraft', 'Create draft artifact', 'Creates an internal draft artifact.', 'green', 'internal_write', true, 'none'),
        ('approval.request', 'Request approval', 'Creates a payload-bound owner approval request.', 'yellow', 'internal_write', true, 'none'),
        ('agentRun.addFinding', 'Add agent finding', 'Records a structured internal finding.', 'green', 'internal_write', true, 'none'),
        ('repo.createWorktree', 'Create isolated worktree', 'Creates an allowlisted isolated engineering worktree.', 'yellow', 'internal_write', true, 'policy'),
        ('repo.writeFile', 'Write sandbox file', 'Writes an allowlisted file in an isolated worktree.', 'yellow', 'internal_write', true, 'policy'),
        ('repo.runAllowlistedCommand', 'Run allowlisted command', 'Runs a fixed allowlisted command in an isolated worktree.', 'yellow', 'internal_write', true, 'policy'),
        ('repo.createDiffArtifact', 'Create diff artifact', 'Creates a reviewable diff artifact.', 'yellow', 'internal_write', true, 'policy'),
        ('email.send', 'Send email', 'Disabled F0 adapter for outbound email.', 'red', 'external_material', false, 'always'),
        ('social.publish', 'Publish social content', 'Disabled F0 adapter for social publishing.', 'red', 'external_material', false, 'always'),
        ('crm.createExternalLead', 'Create external lead', 'Disabled F0 adapter for external CRM writes.', 'red', 'external_reversible', false, 'always'),
        ('billing.issueRefund', 'Issue refund', 'Disabled F0 adapter for refunds.', 'red', 'external_material', false, 'always'),
        ('billing.createCharge', 'Create charge', 'Disabled F0 adapter for charges.', 'red', 'external_material', false, 'always'),
        ('ads.changeBudget', 'Change advertising budget', 'Disabled F0 adapter for advertising spend.', 'red', 'external_material', false, 'always'),
        ('github.pushBranch', 'Push Git branch', 'Disabled F0 adapter for remote Git writes.', 'red', 'external_material', false, 'always'),
        ('github.openPullRequest', 'Open pull request', 'Disabled F0 adapter for remote pull requests.', 'red', 'external_reversible', false, 'always'),
        ('github.mergePullRequest', 'Merge pull request', 'Disabled F0 adapter for remote merges.', 'red', 'external_material', false, 'always'),
        ('deployment.releaseProduction', 'Release production', 'Disabled F0 adapter for production deployment.', 'red', 'external_material', false, 'always'),
        ('customerData.permanentlyDelete', 'Permanently delete customer data', 'Disabled F0 adapter for permanent deletion.', 'prohibited', 'destructive', false, 'always'),
        ('security.sendIncidentNotice', 'Send incident notice', 'Disabled F0 adapter for external incident notices.', 'red', 'external_material', false, 'always')
)
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
select
    gen_random_uuid(),
    tool_key,
    display_name,
    description,
    risk_level,
    side_effect_class,
    enabled,
    approval_mode,
    '{"type":"object"}'::jsonb,
    '{"type":"object"}'::jsonb,
    now(),
    now()
from catalog
on conflict (tool_key) do update
set display_name = excluded.display_name,
    description = excluded.description,
    risk_level = excluded.risk_level,
    side_effect_class = excluded.side_effect_class,
    enabled = excluded.enabled,
    approval_mode = excluded.approval_mode,
    input_schema = excluded.input_schema,
    output_schema = excluded.output_schema,
    updated_at = now();

update agent_versions version
set tool_permissions = case definition.agent_key
    when 'executive' then '["context.getCompanyConstitution","context.getMetricsSnapshot","knowledge.search","task.create","artifact.createDraft","approval.request"]'::jsonb
    when 'support' then '["context.getProductFacts","context.getAnalysis","knowledge.search","artifact.createDraft","task.create","approval.request"]'::jsonb
    when 'product-analyst' then '["context.getMetricsSnapshot","knowledge.search","task.create","artifact.createDraft","approval.request"]'::jsonb
    when 'engineering' then '["context.getTask","repo.readFile","repo.search","repo.getStatus","repo.createWorktree","repo.writeFile","repo.runAllowlistedCommand","repo.getDiff","repo.createDiffArtifact","artifact.createDraft","approval.request","github.pushBranch","github.openPullRequest"]'::jsonb
    else version.tool_permissions
end
from agent_definitions definition
where definition.id = version.agent_definition_id
  and version.version_number = 1;
