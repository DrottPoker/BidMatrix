import type { ReactNode } from "react";
import { Circle, CircleCheck, CircleDashed, CircleX, Clock3 } from "lucide-react";

const statusConfig: Record<string, { label: string; className: string; icon: ReactNode }> = {
  completed: { label: "Ready", className: "bg-emerald-50 text-emerald-800 ring-emerald-600/15", icon: <CircleCheck size={13} /> },
  ready: { label: "Ready", className: "bg-emerald-50 text-emerald-800 ring-emerald-600/15", icon: <CircleCheck size={13} /> },
  requires_review: { label: "Quality review", className: "bg-amber-50 text-amber-900 ring-amber-600/20", icon: <Clock3 size={13} /> },
  qualityReview: { label: "Quality review", className: "bg-amber-50 text-amber-900 ring-amber-600/20", icon: <Clock3 size={13} /> },
  processing: { label: "Processing", className: "bg-sky-50 text-sky-800 ring-sky-600/15", icon: <CircleDashed className="animate-spin" size={13} /> },
  queued: { label: "Queued", className: "bg-sky-50 text-sky-800 ring-sky-600/15", icon: <CircleDashed size={13} /> },
  draft: { label: "Draft", className: "bg-stone-100 text-stone-700 ring-stone-600/10", icon: <Circle size={13} /> },
  quarantined: { label: "Files uploaded", className: "bg-violet-50 text-violet-800 ring-violet-600/15", icon: <CircleCheck size={13} /> },
  failed: { label: "Needs attention", className: "bg-red-50 text-red-800 ring-red-600/15", icon: <CircleX size={13} /> },
  cancelled: { label: "Cancelled", className: "bg-stone-100 text-stone-600 ring-stone-600/10", icon: <CircleX size={13} /> },
};

export function StatusPill({ status }: { status: string }) {
  const config = statusConfig[status] ?? {
    label: humanize(status),
    className: "bg-stone-100 text-stone-700 ring-stone-600/10",
    icon: <Circle size={13} />,
  };

  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold ring-1 ring-inset ${config.className}`}>
      {config.icon}
      {config.label}
    </span>
  );
}

export function humanize(value: string) {
  return value
    .replaceAll("_", " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/^./, (character) => character.toUpperCase());
}
