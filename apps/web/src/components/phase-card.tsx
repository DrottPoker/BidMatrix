import { Check, LockKeyhole, Radio } from "lucide-react";
import type { FoundationPhase, PhaseStatus } from "@/data/foundation";

const statusStyles: Record<PhaseStatus, string> = {
  Complete: "bg-emerald-50 text-emerald-700 ring-emerald-600/20",
  Active: "bg-lime-100 text-brand-dark ring-brand/20",
  Locked: "bg-zinc-100 text-zinc-500 ring-zinc-400/20",
};

const statusIcons: Record<PhaseStatus, typeof Check> = {
  Complete: Check,
  Active: Radio,
  Locked: LockKeyhole,
};

type PhaseCardProps = {
  phase: FoundationPhase;
};

export function PhaseCard({ phase }: PhaseCardProps) {
  const StatusIcon = statusIcons[phase.status];

  return (
    <article className="group flex min-h-80 flex-col rounded-3xl border bg-surface p-6 shadow-[0_14px_40px_rgba(23,32,29,0.05)] transition hover:-translate-y-1 hover:shadow-[0_20px_50px_rgba(23,32,29,0.09)]">
      <div className="flex items-start justify-between gap-4">
        <span className="font-mono text-sm tracking-[0.24em] text-muted">
          {phase.number}
        </span>
        <span
          className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-semibold ring-1 ring-inset ${statusStyles[phase.status]}`}
        >
          <StatusIcon aria-hidden="true" size={13} />
          {phase.status}
        </span>
      </div>
      <h3 className="mt-8 text-xl font-semibold tracking-tight text-foreground">
        {phase.title}
      </h3>
      <p className="mt-3 text-sm leading-6 text-muted">{phase.outcome}</p>
      <ul className="mt-auto space-y-2 border-t pt-5 text-sm text-foreground/80">
        {phase.deliverables.map((deliverable) => (
          <li key={deliverable} className="flex items-center gap-2">
            <span className="size-1.5 rounded-full bg-brand/55" />
            {deliverable}
          </li>
        ))}
      </ul>
    </article>
  );
}
