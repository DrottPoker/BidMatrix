"use client";

import { useEffect, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import {
  AlertTriangle,
  Bot,
  Check,
  CircleOff,
  Clock3,
  FileJson2,
  LoaderCircle,
  Pause,
  Play,
  Plus,
  RefreshCw,
  X,
} from "lucide-react";
import { ApiError, apiGet, apiMutation } from "@/lib/bidmatrix-api";

type Section = "dashboard" | "tasks" | "approvals" | "agents" | "runs" | "audit" | "goals" | "controls";

type SystemControl = {
  controlKey: string;
  enabled: boolean;
  value: unknown;
  updatedByUserId: string;
  updatedAt: string;
  version: number;
  lockedForF0: boolean;
};

type AgentRun = {
  id: string;
  organizationId: string;
  taskId: string;
  agentKey: string;
  status: string;
  modelName: string;
  promptVersion: string;
  outputArtifactId: string | null;
  inputTokens: number;
  outputTokens: number;
  startedAt: string;
  completedAt: string | null;
  failureCode: string | null;
  failureMessage: string | null;
};

type Dashboard = {
  analysesByStatus: Record<string, number>;
  openTasks: number;
  pendingApprovals: number;
  workflowFailures: number;
  recentAgentRuns: AgentRun[];
  systemControls: SystemControl[];
  auditChainValid: boolean;
  draftOnly: boolean;
  externalActionsDisabled: boolean;
  generatedAt: string;
};

type OwnerTask = {
  id: string;
  goalId: string | null;
  type: string;
  title: string;
  description: string;
  priority: string;
  status: string;
  assignedAgentKey: string | null;
  input: unknown;
  constraints: unknown;
  resultArtifactId: string | null;
  errorCode: string | null;
  errorMessage: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
  updatedAt: string;
  version: number;
};

type Approval = {
  id: string;
  taskId: string | null;
  actionType: string;
  status: string;
  summary: string;
  normalizedPayload: unknown;
  payloadHash: string;
  policyVersion: string;
  riskLevel: string;
  technicallyEnabled: boolean;
  requestedAt: string;
  expiresAt: string;
  decisionNote: string | null;
  executionStatus: string | null;
  version: number;
};

type AgentDefinition = {
  agentKey: string;
  displayName: string;
  description: string;
  status: string;
  version: number;
  promptVersion: string;
  modelKey: string;
  toolPermissions: string[];
  totalRuns: number;
  failedRuns: number;
  inputTokens: number;
  outputTokens: number;
  lastRunAt: string | null;
};

type Workflow = {
  id: string;
  workflowType: string;
  workflowId: string;
  temporalRunId: string | null;
  taskId: string | null;
  status: string;
  startedAt: string;
  completedAt: string | null;
  updatedAt: string;
};

type AuditEvent = {
  id: string;
  sequenceNumber: number;
  actorType: string;
  actorId: string;
  action: string;
  targetType: string | null;
  targetId: string | null;
  requestId: string | null;
  traceId: string | null;
  summary: string;
  eventHash: string;
  chainLinkValid: boolean;
  createdAt: string;
};

type Goal = {
  id: string;
  title: string;
  description: string;
  metricKey: string | null;
  targetValue: number | null;
  targetDate: string | null;
  status: string;
  constraints: Record<string, unknown>;
  createdAt: string;
  updatedAt: string;
  version: number;
};

const sectionTitles: Record<Section, { title: string; description: string }> = {
  dashboard: { title: "Operating dashboard", description: "Live F1 state from the authoritative API." },
  tasks: { title: "Tasks", description: "Create, inspect, filter, and safely cancel internal work." },
  approvals: { title: "Approvals", description: "Review the exact normalized payload before deciding." },
  agents: { title: "Agents", description: "The four versioned roles, permissions, usage, and offline demos." },
  runs: { title: "Runs and workflows", description: "Durable execution state, usage, failures, and artifacts." },
  audit: { title: "Audit trail", description: "Append-only actions with request, trace, and hash-chain evidence." },
  goals: { title: "Owner goals", description: "Set measurable intent and explicit constraints for internal work." },
  controls: { title: "System controls", description: "Owner-only kill switches with optimistic concurrency." },
};

export function OwnerConsole({ section }: { section: Section }) {
  const [error, setError] = useState<string | null>(null);
  const [refreshKey, setRefreshKey] = useState(0);
  const heading = sectionTitles[section];

  return (
    <div className="space-y-6">
      <header className="flex flex-col justify-between gap-4 sm:flex-row sm:items-end">
        <div>
          <p className="text-xs font-bold uppercase tracking-[0.18em] text-brand">BidMatrix OS</p>
          <h1 className="mt-2 text-3xl font-semibold tracking-tight">{heading.title}</h1>
          <p className="mt-2 text-sm text-muted">{heading.description}</p>
        </div>
        <button
          className="inline-flex h-10 items-center justify-center gap-2 rounded-xl border bg-white px-4 text-sm font-semibold"
          onClick={() => {
            setError(null);
            setRefreshKey((value) => value + 1);
          }}
          type="button"
        >
          <RefreshCw aria-hidden="true" size={16} />
          Refresh
        </button>
      </header>

      {error ? <ErrorBanner message={error} /> : null}
      {section === "dashboard" ? <DashboardView refreshKey={refreshKey} onError={setError} /> : null}
      {section === "tasks" ? <TasksView refreshKey={refreshKey} onError={setError} /> : null}
      {section === "approvals" ? <ApprovalsView refreshKey={refreshKey} onError={setError} /> : null}
      {section === "agents" ? <AgentsView refreshKey={refreshKey} onError={setError} /> : null}
      {section === "runs" ? <RunsView refreshKey={refreshKey} onError={setError} /> : null}
      {section === "audit" ? <AuditView refreshKey={refreshKey} onError={setError} /> : null}
      {section === "goals" ? <GoalsView refreshKey={refreshKey} onError={setError} /> : null}
      {section === "controls" ? <ControlsView refreshKey={refreshKey} onError={setError} /> : null}
    </div>
  );
}

function DashboardView({ refreshKey, onError }: ViewProps) {
  const { data, loading } = useOwnerData<Dashboard>("/owner/v1/dashboard", refreshKey, onError);
  if (loading) return <Loading />;
  if (!data) return <EmptyState>No dashboard data is available.</EmptyState>;

  const analysisCount = Object.values(data.analysesByStatus).reduce((total, value) => total + value, 0);
  return (
    <div className="space-y-6">
      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="Analyses" value={analysisCount} detail={formatStatusCounts(data.analysesByStatus)} />
        <Metric label="Open tasks" value={data.openTasks} detail="Excludes final states" />
        <Metric label="Pending approvals" value={data.pendingApprovals} detail="Exact payload required" />
        <Metric label="Workflow failures" value={data.workflowFailures} detail="Requires owner attention" />
      </div>
      <section className="grid gap-4 lg:grid-cols-[1.3fr_0.7fr]">
        <Panel title="Recent agent runs">
          {data.recentAgentRuns.length ? data.recentAgentRuns.map((run) => <RunRow key={run.id} run={run} />) : <Muted>No agent runs yet.</Muted>}
        </Panel>
        <Panel title="Foundation controls">
          <div className="space-y-3">
            {data.systemControls.map((control) => (
              <div className="flex items-center justify-between gap-4" key={control.controlKey}>
                <span className="text-sm">{humanize(control.controlKey)}</span>
                <Status status={control.enabled ? "enabled" : "disabled"} />
              </div>
            ))}
          </div>
          <div className="mt-5 rounded-xl bg-zinc-50 p-3 text-xs text-muted">
            Audit chain: <strong className={data.auditChainValid ? "text-emerald-700" : "text-red-700"}>{data.auditChainValid ? "verified" : "invalid"}</strong>
            <br />Generated {formatDate(data.generatedAt)}
          </div>
        </Panel>
      </section>
    </div>
  );
}

function TasksView({ refreshKey, onError }: ViewProps) {
  const [localKey, setLocalKey] = useState(0);
  const { data, loading } = useOwnerData<{ tasks: OwnerTask[] }>("/owner/v1/tasks", refreshKey + localKey, onError);
  const [busy, setBusy] = useState(false);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [type, setType] = useState("support");
  const [agent, setAgent] = useState("");

  async function createTask(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusy(true);
    try {
      await apiMutation("/owner/v1/tasks", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Idempotency-Key": crypto.randomUUID() },
        body: JSON.stringify({
          type,
          title,
          description,
          priority: "normal",
          assignedAgentKey: agent || null,
          goalId: null,
          input: {},
          constraints: { externalActionsEnabled: false },
        }),
      });
      setTitle("");
      setDescription("");
      setLocalKey((value) => value + 1);
    } catch (requestError) {
      onError(formatError(requestError));
    } finally {
      setBusy(false);
    }
  }

  async function cancelTask(task: OwnerTask) {
    if (!window.confirm(`Cancel task "${task.title}"? This does not undo completed effects.`)) return;
    try {
      await apiMutation(`/owner/v1/tasks/${task.id}/cancel`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ expectedVersion: task.version, note: "Cancelled from Owner Console." }),
      });
      setLocalKey((value) => value + 1);
    } catch (requestError) {
      onError(formatError(requestError));
    }
  }

  return (
    <div className="space-y-5">
      <form className="grid gap-3 rounded-2xl border bg-white p-5 lg:grid-cols-2" onSubmit={createTask}>
        <input className="h-11 rounded-xl border px-4" maxLength={200} onChange={(event) => setTitle(event.target.value)} placeholder="Task title" required value={title} />
        <input className="h-11 rounded-xl border px-4" maxLength={5000} onChange={(event) => setDescription(event.target.value)} placeholder="Clear task description" required value={description} />
        <select className="h-11 rounded-xl border bg-white px-4" onChange={(event) => setType(event.target.value)} value={type}>
          <option value="support">Support</option><option value="executive">Executive</option><option value="product-review">Product review</option><option value="engineering">Engineering</option>
        </select>
        <select className="h-11 rounded-xl border bg-white px-4" onChange={(event) => setAgent(event.target.value)} value={agent}>
          <option value="">Unassigned</option><option value="executive">Executive Agent</option><option value="support">Support Agent</option><option value="product-analyst">Product Analyst Agent</option><option value="engineering">Engineering Agent</option>
        </select>
        <button className="inline-flex h-11 items-center justify-center gap-2 rounded-xl bg-brand px-5 text-sm font-semibold text-white disabled:opacity-50 lg:col-span-2" disabled={busy} type="submit"><Plus size={16} />Create internal task</button>
      </form>
      {loading ? <Loading /> : null}
      {data && data.tasks.length === 0 ? <EmptyState>No tasks match the current organization.</EmptyState> : null}
      <div className="space-y-3">
        {data?.tasks.map((task) => (
          <Panel key={task.id} title={task.title} trailing={<Status status={task.status} />}>
            <p className="text-sm text-muted">{task.description}</p>
            <div className="mt-4 flex flex-wrap gap-2 text-xs"><Tag>{task.type}</Tag><Tag>{task.priority}</Tag>{task.assignedAgentKey ? <Tag>{task.assignedAgentKey}</Tag> : null}<Tag>v{task.version}</Tag></div>
            <details className="mt-4 text-xs"><summary className="cursor-pointer font-semibold">Inputs and constraints</summary><JsonViewer value={{ input: task.input, constraints: task.constraints }} /></details>
            {!isFinal(task.status) ? <button className="mt-4 inline-flex h-9 items-center gap-2 rounded-xl border border-red-200 px-3 text-xs font-semibold text-red-700" onClick={() => void cancelTask(task)} type="button"><X size={14} />Cancel task</button> : null}
          </Panel>
        ))}
      </div>
    </div>
  );
}

function ApprovalsView({ refreshKey, onError }: ViewProps) {
  const [localKey, setLocalKey] = useState(0);
  const { data, loading } = useOwnerData<{ approvals: Approval[] }>("/owner/v1/approvals", refreshKey + localKey, onError);

  async function decide(approval: Approval, decision: "approve" | "reject") {
    const verb = decision === "approve" ? "approve" : "reject";
    if (!window.confirm(`${verb[0].toUpperCase()}${verb.slice(1)} this exact payload?`)) return;
    try {
      await apiMutation(`/owner/v1/approvals/${approval.id}/${decision}`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ payload: approval.normalizedPayload, expectedVersion: approval.version, note: `Owner chose ${decision}.` }),
      });
      setLocalKey((value) => value + 1);
    } catch (requestError) {
      onError(formatError(requestError));
    }
  }

  const approvals = [...(data?.approvals ?? [])].sort((left, right) => Number(right.status === "pending") - Number(left.status === "pending"));
  return (
    <div className="space-y-4">
      {loading ? <Loading /> : null}
      {!loading && approvals.length === 0 ? <EmptyState>No approvals have been requested.</EmptyState> : null}
      {approvals.map((approval) => (
        <Panel key={approval.id} title={approval.summary} trailing={<Status status={approval.status} />}>
          <div className="grid gap-4 lg:grid-cols-[1fr_1.2fr]">
            <dl className="grid content-start grid-cols-[auto_1fr] gap-x-4 gap-y-2 text-xs">
              <dt className="text-muted">Action</dt><dd>{approval.actionType}</dd>
              <dt className="text-muted">Risk</dt><dd>{approval.riskLevel}</dd>
              <dt className="text-muted">Policy</dt><dd>{approval.policyVersion}</dd>
              <dt className="text-muted">Task</dt><dd className="font-mono">{approval.taskId ?? "none"}</dd>
              <dt className="text-muted">Expires</dt><dd>{formatDate(approval.expiresAt)}</dd>
              <dt className="text-muted">Execution</dt><dd>{approval.executionStatus ?? "notStarted"}</dd>
              <dt className="text-muted">Adapter</dt><dd>{approval.technicallyEnabled ? "enabled" : "disabled in F1"}</dd>
              <dt className="text-muted">Payload hash</dt><dd className="break-all font-mono">{approval.payloadHash}</dd>
            </dl>
            <div><p className="mb-2 text-xs font-bold uppercase tracking-wide text-muted">Exact normalized payload</p><JsonViewer value={approval.normalizedPayload} /></div>
          </div>
          {approval.status === "pending" ? <div className="mt-5 flex gap-3"><button className="inline-flex h-10 items-center gap-2 rounded-xl bg-brand px-4 text-sm font-semibold text-white" onClick={() => void decide(approval, "approve")} type="button"><Check size={16} />Approve exact payload</button><button className="inline-flex h-10 items-center gap-2 rounded-xl border border-red-200 px-4 text-sm font-semibold text-red-700" onClick={() => void decide(approval, "reject")} type="button"><X size={16} />Reject</button></div> : null}
        </Panel>
      ))}
    </div>
  );
}

function AgentsView({ refreshKey, onError }: ViewProps) {
  const [localKey, setLocalKey] = useState(0);
  const { data, loading } = useOwnerData<{ agents: AgentDefinition[] }>("/owner/v1/agents", refreshKey + localKey, onError);
  const [busy, setBusy] = useState<string | null>(null);

  async function runDemo(agentKey: string) {
    setBusy(agentKey);
    try {
      await apiMutation(`/owner/v1/agent-demos/${agentKey}`, {
        method: "POST",
        headers: { "Content-Type": "application/json", "Idempotency-Key": crypto.randomUUID() },
        body: JSON.stringify({ input: null }),
      });
      setLocalKey((value) => value + 1);
    } catch (requestError) {
      onError(formatError(requestError));
    } finally {
      setBusy(null);
    }
  }

  if (loading) return <Loading />;
  return <div className="grid gap-4 lg:grid-cols-2">{data?.agents.map((agent) => (
    <Panel key={agent.agentKey} title={agent.displayName} trailing={<Status status={agent.status} />}>
      <p className="text-sm text-muted">{agent.description}</p>
      <div className="mt-4 grid grid-cols-3 gap-3"><SmallMetric label="Runs" value={agent.totalRuns} /><SmallMetric label="Failures" value={agent.failedRuns} /><SmallMetric label="Tokens" value={agent.inputTokens + agent.outputTokens} /></div>
      <p className="mt-4 text-xs text-muted">Prompt {agent.promptVersion} · version {agent.version} · {agent.modelKey}</p>
      <div className="mt-4 flex flex-wrap gap-2">{agent.toolPermissions.map((tool) => <Tag key={tool}>{tool}</Tag>)}</div>
      <button className="mt-5 inline-flex h-10 items-center gap-2 rounded-xl bg-brand px-4 text-sm font-semibold text-white disabled:opacity-50" disabled={busy === agent.agentKey} onClick={() => void runDemo(agent.agentKey)} type="button"><Play size={15} />Run offline demo</button>
    </Panel>
  ))}</div>;
}

function RunsView({ refreshKey, onError }: ViewProps) {
  const { data: runs, loading: runsLoading } = useOwnerData<{ runs: AgentRun[] }>("/owner/v1/runs", refreshKey, onError);
  const { data: workflows, loading: workflowsLoading } = useOwnerData<{ workflows: Workflow[] }>("/owner/v1/workflows", refreshKey, onError);
  if (runsLoading || workflowsLoading) return <Loading />;
  return <div className="grid gap-5 xl:grid-cols-2"><Panel title="Agent runs">{runs?.runs.length ? runs.runs.map((run) => <RunRow key={run.id} run={run} />) : <Muted>No agent runs yet.</Muted>}</Panel><Panel title="Temporal workflow records">{workflows?.workflows.length ? workflows.workflows.map((workflow) => <div className="border-b py-3 last:border-0" key={workflow.id}><div className="flex justify-between gap-4"><div><p className="text-sm font-semibold">{workflow.workflowType}</p><p className="mt-1 break-all font-mono text-xs text-muted">{workflow.workflowId}</p></div><Status status={workflow.status} /></div><p className="mt-2 text-xs text-muted">{formatDate(workflow.startedAt)} · {duration(workflow.startedAt, workflow.completedAt)}</p></div>) : <Muted>No workflow records yet.</Muted>}</Panel></div>;
}

function AuditView({ refreshKey, onError }: ViewProps) {
  const { data, loading } = useOwnerData<{ chainValid: boolean; events: AuditEvent[] }>("/owner/v1/audit", refreshKey, onError);
  if (loading) return <Loading />;
  return <Panel title="Append-only event chain" trailing={<Status status={data?.chainValid ? "verified" : "invalid"} />}>
    {data?.events.map((event) => <div className="border-b py-4 last:border-0" key={event.id}><div className="flex flex-col justify-between gap-2 sm:flex-row"><div><p className="text-sm font-semibold">{event.action}</p><p className="mt-1 text-xs text-muted">{event.summary}</p></div><span className="text-xs text-muted">#{event.sequenceNumber} · {formatDate(event.createdAt)}</span></div><div className="mt-3 grid gap-1 font-mono text-[11px] text-muted"><span>actor={event.actorType}:{event.actorId}</span><span>target={event.targetType ?? "none"}:{event.targetId ?? "none"}</span><span>request={event.requestId ?? "none"} trace={event.traceId ?? "none"}</span><span className="break-all">hash={event.eventHash} link={event.chainLinkValid ? "valid" : "invalid"}</span></div></div>)}
  </Panel>;
}

function GoalsView({ refreshKey, onError }: ViewProps) {
  const [localKey, setLocalKey] = useState(0);
  const { data, loading } = useOwnerData<{ goals: Goal[] }>("/owner/v1/goals", refreshKey + localKey, onError);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");

  async function createGoal(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    try {
      await apiMutation("/owner/v1/goals", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ title, description, metricKey: null, targetValue: null, targetDate: null, status: "active", constraints: { externalEffects: "draft-only" } }) });
      setTitle(""); setDescription(""); setLocalKey((value) => value + 1);
    } catch (requestError) { onError(formatError(requestError)); }
  }

  async function toggleGoal(goal: Goal) {
    try {
      await apiMutation(`/owner/v1/goals/${goal.id}`, { method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ ...goal, status: goal.status === "active" ? "paused" : "active", expectedVersion: goal.version }) });
      setLocalKey((value) => value + 1);
    } catch (requestError) { onError(formatError(requestError)); }
  }

  return <div className="space-y-5"><form className="grid gap-3 rounded-2xl border bg-white p-5 sm:grid-cols-2" onSubmit={createGoal}><input className="h-11 rounded-xl border px-4" maxLength={200} onChange={(event) => setTitle(event.target.value)} placeholder="Goal title" required value={title} /><input className="h-11 rounded-xl border px-4" maxLength={5000} onChange={(event) => setDescription(event.target.value)} placeholder="Outcome and constraints" required value={description} /><button className="inline-flex h-11 items-center justify-center gap-2 rounded-xl bg-brand text-sm font-semibold text-white sm:col-span-2" type="submit"><Plus size={16} />Create goal</button></form>{loading ? <Loading /> : null}{data?.goals.map((goal) => <Panel key={goal.id} title={goal.title} trailing={<Status status={goal.status} />}><p className="text-sm text-muted">{goal.description}</p><p className="mt-3 text-xs text-muted">Metric: {goal.metricKey ?? "not configured"} · Target: {goal.targetValue ?? "not configured"} · version {goal.version}</p><JsonViewer value={goal.constraints} /><button className="mt-4 inline-flex h-9 items-center gap-2 rounded-xl border px-3 text-xs font-semibold" onClick={() => void toggleGoal(goal)} type="button">{goal.status === "active" ? <Pause size={14} /> : <Play size={14} />}{goal.status === "active" ? "Pause goal" : "Activate goal"}</button></Panel>)}</div>;
}

function ControlsView({ refreshKey, onError }: ViewProps) {
  const [localKey, setLocalKey] = useState(0);
  const { data, loading } = useOwnerData<{ controls: SystemControl[] }>("/owner/v1/system-controls", refreshKey + localKey, onError);
  async function toggle(control: SystemControl) {
    if (control.lockedForF0) return;
    if (!window.confirm(`Set ${control.controlKey} to ${!control.enabled}? This affects future policy evaluations.`)) return;
    try {
      await apiMutation(`/owner/v1/system-controls/${control.controlKey}`, { method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ enabled: !control.enabled, expectedVersion: control.version, confirmation: "CONFIRM F0 CONTROL CHANGE" }) });
      setLocalKey((value) => value + 1);
    } catch (requestError) { onError(formatError(requestError)); }
  }
  if (loading) return <Loading />;
  return <div className="space-y-3"><div className="rounded-2xl border border-amber-300 bg-amber-50 p-4 text-sm text-amber-950"><strong>Owner-only control surface.</strong> Locked controls preserve draft-only operation and disabled external execution throughout F1.</div>{data?.controls.map((control) => <Panel key={control.controlKey} title={humanize(control.controlKey)} trailing={<Status status={control.enabled ? "enabled" : "disabled"} />}><div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-center"><div><p className="text-sm text-muted">Updated {formatDate(control.updatedAt)} · version {control.version}</p><p className="mt-1 text-xs text-muted">{control.lockedForF0 ? "Locked to the safe baseline value." : "Owner-editable kill switch."}</p></div><button className="inline-flex h-10 items-center justify-center gap-2 rounded-xl border px-4 text-sm font-semibold disabled:cursor-not-allowed disabled:opacity-45" disabled={control.lockedForF0} onClick={() => void toggle(control)} type="button">{control.enabled ? <CircleOff size={16} /> : <Play size={16} />}{control.enabled ? "Disable" : "Enable"}</button></div></Panel>)}</div>;
}

type ViewProps = { refreshKey: number; onError: (message: string | null) => void };

function useOwnerData<T>(path: string, refreshKey: number, onError: (message: string | null) => void) {
  const [data, setData] = useState<T | null>(null);
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    let active = true;
    apiGet<T>(path).then((result) => { if (active) setData(result); }).catch((requestError: unknown) => { if (active) onError(formatError(requestError)); }).finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [path, refreshKey, onError]);
  return { data, loading };
}

function Panel({ title, trailing, children }: { title: string; trailing?: ReactNode; children: ReactNode }) { return <section className="rounded-2xl border bg-white p-5 shadow-sm"><div className="mb-4 flex items-start justify-between gap-4"><h2 className="font-semibold">{title}</h2>{trailing}</div>{children}</section>; }
function Metric({ label, value, detail }: { label: string; value: number; detail: string }) { return <div className="rounded-2xl border bg-white p-5"><p className="text-xs font-bold uppercase tracking-wide text-muted">{label}</p><p className="mt-2 text-3xl font-semibold">{value}</p><p className="mt-2 truncate text-xs text-muted">{detail}</p></div>; }
function SmallMetric({ label, value }: { label: string; value: number }) { return <div className="rounded-xl bg-zinc-50 p-3"><p className="text-[10px] font-bold uppercase text-muted">{label}</p><p className="mt-1 text-lg font-semibold">{value}</p></div>; }
function Status({ status }: { status: string }) { const good = ["active", "completed", "approved", "verified", "enabled", "clean"].includes(status); const bad = ["failed", "rejected", "invalid", "blocked"].includes(status); return <span className={`inline-flex shrink-0 rounded-full px-2.5 py-1 text-xs font-semibold ${good ? "bg-emerald-50 text-emerald-800" : bad ? "bg-red-50 text-red-800" : "bg-zinc-100 text-zinc-700"}`}>{humanize(status)}</span>; }
function Tag({ children }: { children: ReactNode }) { return <span className="rounded-full bg-zinc-100 px-2.5 py-1 text-xs text-zinc-700">{children}</span>; }
function JsonViewer({ value }: { value: unknown }) { return <pre className="mt-2 max-h-80 overflow-auto rounded-xl bg-[#10201b] p-4 text-xs leading-5 text-emerald-100">{JSON.stringify(value, null, 2)}</pre>; }
function Loading() { return <div className="grid min-h-36 place-items-center rounded-2xl border bg-white"><LoaderCircle className="animate-spin text-brand" aria-label="Loading" /></div>; }
function EmptyState({ children }: { children: ReactNode }) { return <div className="rounded-2xl border border-dashed bg-white p-10 text-center text-sm text-muted">{children}</div>; }
function Muted({ children }: { children: ReactNode }) { return <p className="text-sm text-muted">{children}</p>; }
function ErrorBanner({ message }: { message: string }) { return <div className="flex gap-3 rounded-2xl bg-red-50 p-4 text-sm text-red-900" role="alert"><AlertTriangle className="shrink-0" size={18} /><p>{message}</p></div>; }
function RunRow({ run }: { run: AgentRun }) { return <div className="border-b py-3 last:border-0"><div className="flex items-start justify-between gap-4"><div className="flex gap-3"><Bot className="mt-0.5 text-brand" size={17} /><div><p className="text-sm font-semibold">{humanize(run.agentKey)}</p><p className="mt-1 text-xs text-muted">{run.modelName} · {run.inputTokens + run.outputTokens} tokens</p></div></div><Status status={run.status} /></div><div className="mt-2 flex items-center gap-2 text-xs text-muted"><Clock3 size={13} />{formatDate(run.startedAt)} · {duration(run.startedAt, run.completedAt)}{run.outputArtifactId ? <><FileJson2 size={13} /><span className="font-mono">{run.outputArtifactId.slice(0, 8)}</span></> : null}</div>{run.failureMessage ? <p className="mt-2 text-xs text-red-700">{run.failureCode}: {run.failureMessage}</p> : null}</div>; }

function formatError(error: unknown) { if (error instanceof ApiError && error.status === 401) return "Sign in as the platform owner to open BidMatrix OS."; return error instanceof Error ? error.message : "The owner request failed."; }
function humanize(value: string) { return value.replaceAll("_", " ").replace(/([a-z])([A-Z])/g, "$1 $2").replace(/^./, (character) => character.toUpperCase()); }
function formatDate(value: string) { return new Intl.DateTimeFormat(undefined, { year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit", timeZoneName: "short" }).format(new Date(value)); }
function duration(start: string, end: string | null) { const milliseconds = new Date(end ?? Date.now()).getTime() - new Date(start).getTime(); return milliseconds < 1000 ? `${milliseconds} ms` : `${(milliseconds / 1000).toFixed(1)} s`; }
function formatStatusCounts(counts: Record<string, number>) { const entries = Object.entries(counts); return entries.length ? entries.map(([status, count]) => `${status}: ${count}`).join(", ") : "No analyses"; }
function isFinal(status: string) { return ["completed", "failed", "cancelled"].includes(status); }
