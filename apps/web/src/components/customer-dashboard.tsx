"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ArrowRight, CalendarClock, CheckCircle2, FilePlus2, Files, LoaderCircle, Sparkles } from "lucide-react";
import { Analysis, AnalysisList, apiGet, formatApiError } from "@/lib/bidmatrix-api";
import { StatusPill } from "@/components/ui/status-pill";

export function CustomerDashboard() {
  const [analyses, setAnalyses] = useState<Analysis[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    apiGet<AnalysisList>("/v1/analyses")
      .then((result) => {
        if (active) setAnalyses(result.analyses);
      })
      .catch((requestError: unknown) => {
        if (active) setError(formatApiError(requestError));
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, []);

  const ready = analyses.filter((analysis) => analysis.status === "completed").length;
  const inReview = analyses.filter((analysis) => analysis.status === "requires_review").length;
  const active = analyses.filter((analysis) => ["queued", "processing"].includes(analysis.status)).length;

  return (
    <div className="page-shell space-y-8">
      <section className="relative overflow-hidden rounded-[2rem] bg-ink px-6 py-8 text-white shadow-[0_24px_80px_rgba(16,36,29,0.18)] sm:px-10 sm:py-10 xl:px-12">
        <div className="absolute -right-20 -top-24 size-80 rounded-full bg-accent/12 blur-3xl" />
        <div className="absolute bottom-0 right-[18%] h-32 w-px bg-gradient-to-t from-accent/50 to-transparent" />
        <div className="relative grid gap-8 xl:grid-cols-[1fr_auto] xl:items-end">
          <div>
            <p className="flex items-center gap-2 text-xs font-bold uppercase tracking-[0.2em] text-accent"><Sparkles size={14} /> Your procurement workspace</p>
            <h1 className="mt-5 max-w-3xl text-3xl font-semibold leading-tight tracking-[-0.035em] sm:text-5xl">Turn dense RFP documents into clear, traceable facts.</h1>
            <p className="mt-5 max-w-2xl text-sm leading-7 text-white/62 sm:text-base">Upload the source material. BidMatrix extracts the facts, checks every item against its source, and delivers a quality-reviewed analysis your team can evaluate.</p>
          </div>
          <Link className="inline-flex h-12 items-center justify-center gap-2 rounded-xl bg-accent px-5 text-sm font-bold text-ink transition hover:-translate-y-0.5 hover:bg-white" href="/app/analyses/new">
            <FilePlus2 size={17} /> Start new analysis
          </Link>
        </div>
      </section>

      {error ? <div className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-900" role="alert">{error}</div> : null}

      <section className="grid gap-4 sm:grid-cols-3">
        <MetricCard icon={<Files size={19} />} label="Total analyses" value={loading ? "-" : analyses.length} detail="Across your workspace" />
        <MetricCard icon={<CalendarClock size={19} />} label="In progress" value={loading ? "-" : active + inReview} detail={inReview ? `${inReview} in quality review` : "No reviews waiting"} />
        <MetricCard icon={<CheckCircle2 size={19} />} label="Ready to use" value={loading ? "-" : ready} detail="Quality-reviewed results" accent />
      </section>

      <section className="panel overflow-hidden">
        <div className="flex flex-col justify-between gap-4 border-b border-ink/8 px-6 py-5 sm:flex-row sm:items-center">
          <div>
            <p className="eyebrow">Recent work</p>
            <h2 className="mt-1.5 text-xl font-semibold tracking-tight">Latest analyses</h2>
          </div>
          <Link className="inline-flex items-center gap-2 text-sm font-semibold text-brand hover:text-brand-dark" href="/app/analyses">View all <ArrowRight size={15} /></Link>
        </div>
        {loading ? (
          <div className="grid min-h-56 place-items-center"><LoaderCircle className="animate-spin text-brand" aria-label="Loading analyses" /></div>
        ) : analyses.length === 0 ? (
          <div className="px-6 py-14 text-center">
            <span className="mx-auto grid size-12 place-items-center rounded-2xl bg-surface-muted text-brand"><Files size={21} /></span>
            <h3 className="mt-4 font-semibold">Your first analysis starts here</h3>
            <p className="mx-auto mt-2 max-w-md text-sm leading-6 text-muted">Create an analysis and upload one or more digital PDF documents. We will keep every result linked to the exact source page.</p>
            <Link className="button-primary mt-5" href="/app/analyses/new">Create analysis <ArrowRight size={15} /></Link>
          </div>
        ) : (
          <div className="divide-y divide-ink/7">
            {analyses.slice(0, 5).map((analysis) => (
              <Link className="group grid gap-3 px-6 py-5 transition hover:bg-stone-50/80 sm:grid-cols-[1fr_auto_auto] sm:items-center sm:gap-6" href={`/app/analyses/${analysis.id}`} key={analysis.id}>
                <div className="min-w-0">
                  <h3 className="truncate font-semibold group-hover:text-brand">{analysis.title ?? "Untitled analysis"}</h3>
                  <p className="mt-1 text-xs text-muted">{analysis.files.length} {analysis.files.length === 1 ? "document" : "documents"} · Updated {formatDate(analysis.updatedAt)}</p>
                </div>
                <StatusPill status={analysis.status} />
                <ArrowRight className="hidden text-muted transition group-hover:translate-x-0.5 group-hover:text-brand sm:block" size={17} />
              </Link>
            ))}
          </div>
        )}
      </section>
    </div>
  );
}

function MetricCard({ icon, label, value, detail, accent = false }: { icon: React.ReactNode; label: string; value: number | string; detail: string; accent?: boolean }) {
  return (
    <div className={`panel p-5 sm:p-6 ${accent ? "bg-[linear-gradient(135deg,#ffffff_45%,#f3ffd2)]" : ""}`}>
      <div className="flex items-center justify-between">
        <span className="grid size-10 place-items-center rounded-xl bg-surface-muted text-brand">{icon}</span>
        {accent ? <span className="size-2 rounded-full bg-brand shadow-[0_0_0_5px_rgba(20,116,93,0.1)]" /> : null}
      </div>
      <p className="mt-5 text-3xl font-semibold tracking-[-0.04em]">{value}</p>
      <p className="mt-1 text-sm font-semibold">{label}</p>
      <p className="mt-1 text-xs text-muted">{detail}</p>
    </div>
  );
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en", { month: "short", day: "numeric", year: "numeric" }).format(new Date(value));
}
