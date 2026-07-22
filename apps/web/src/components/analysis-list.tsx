"use client";

import Link from "next/link";
import { useCallback, useEffect, useMemo, useState } from "react";
import { ArrowRight, FilePlus2, Files, LoaderCircle, RefreshCw, Search } from "lucide-react";
import { Analysis, AnalysisList as AnalysisListResponse, apiGet, formatApiError } from "@/lib/bidmatrix-api";
import { StatusPill } from "@/components/ui/status-pill";

const filters = [
  { key: "all", label: "All" },
  { key: "active", label: "In progress" },
  { key: "completed", label: "Ready" },
  { key: "draft", label: "Drafts" },
];

export function AnalysisList({
  endpoint = "/v1/analyses",
  detailBasePath = "/app/analyses",
  showCreate = true,
  ownerMode = false,
}: {
  endpoint?: string;
  detailBasePath?: string;
  showCreate?: boolean;
  ownerMode?: boolean;
}) {
  const [analyses, setAnalyses] = useState<Analysis[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState("all");

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await apiGet<AnalysisListResponse>(endpoint);
      setAnalyses(result.analyses);
    } catch (requestError) {
      setError(formatApiError(requestError));
    } finally {
      setLoading(false);
    }
  }, [endpoint]);

  useEffect(() => {
    let active = true;
    apiGet<AnalysisListResponse>(endpoint)
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
  }, [endpoint]);

  const visible = useMemo(() => analyses.filter((analysis) => {
    const matchesQuery = (analysis.title ?? "Untitled analysis").toLowerCase().includes(query.toLowerCase());
    const matchesFilter = filter === "all" ||
      (filter === "active" && ["queued", "processing", "requires_review"].includes(analysis.status)) ||
      (filter === "completed" && analysis.status === "completed") ||
      (filter === "draft" && ["draft", "quarantined"].includes(analysis.status));
    return matchesQuery && matchesFilter;
  }), [analyses, filter, query]);

  return (
    <div className="page-shell space-y-7">
      <header className="flex flex-col justify-between gap-5 sm:flex-row sm:items-end">
        <div>
          <p className="eyebrow">{ownerMode ? "Quality operations" : "Workspace"}</p>
          <h1 className="mt-2 text-3xl font-semibold tracking-[-0.035em] sm:text-4xl">{ownerMode ? "Analysis review queue" : "Analyses"}</h1>
          <p className="mt-3 max-w-2xl text-sm leading-6 text-muted">{ownerMode ? "Review, correct, and publish extracted results before customer delivery." : "Follow each RFP from upload through quality review to a source-linked result."}</p>
        </div>
        {showCreate ? <Link className="button-primary" href="/app/analyses/new"><FilePlus2 size={17} /> New analysis</Link> : null}
      </header>

      <section className="panel p-4 sm:p-5">
        <div className="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
          <label className="relative block max-w-xl flex-1">
            <Search className="absolute left-3.5 top-1/2 -translate-y-1/2 text-muted" size={17} />
            <span className="sr-only">Search analyses</span>
            <input className="field h-11 pl-10" onChange={(event) => setQuery(event.target.value)} placeholder="Search by analysis name" type="search" value={query} />
          </label>
          <div className="flex flex-wrap items-center gap-2">
            {filters.map((item) => (
              <button className={`rounded-full px-3 py-2 text-xs font-semibold transition ${filter === item.key ? "bg-ink text-white" : "bg-surface-muted text-muted hover:text-foreground"}`} key={item.key} onClick={() => setFilter(item.key)} type="button">{item.label}</button>
            ))}
            <button aria-label="Refresh analyses" className="grid size-9 place-items-center rounded-full border border-ink/10 text-muted hover:bg-stone-50 hover:text-brand" onClick={() => void refresh()} type="button"><RefreshCw size={15} /></button>
          </div>
        </div>
      </section>

      {error ? <div className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-900" role="alert">{error}</div> : null}

      {loading ? (
        <div className="panel grid min-h-64 place-items-center"><LoaderCircle className="animate-spin text-brand" aria-label="Loading analyses" /></div>
      ) : visible.length === 0 ? (
        <div className="panel px-6 py-16 text-center">
          <span className="mx-auto grid size-14 place-items-center rounded-2xl bg-surface-muted text-brand"><Files size={23} /></span>
          <h2 className="mt-5 text-lg font-semibold">{analyses.length ? "No analyses match this view" : "No analyses yet"}</h2>
          <p className="mx-auto mt-2 max-w-md text-sm leading-6 text-muted">{analyses.length ? "Try a different filter or search term." : "Create your first analysis and upload the source RFP documents."}</p>
        </div>
      ) : (
        <div className="grid gap-4 xl:grid-cols-2">
          {visible.map((analysis) => (
            <Link className="panel group p-5 transition hover:-translate-y-0.5 hover:border-brand/30 hover:shadow-[0_18px_45px_rgba(16,36,29,0.08)] sm:p-6" href={`${detailBasePath}/${analysis.id}`} key={analysis.id}>
              <div className="flex items-start justify-between gap-5">
                <div className="min-w-0">
                  <p className="text-[0.65rem] font-bold uppercase tracking-[0.18em] text-muted">RFP analysis</p>
                  <h2 className="mt-2 truncate text-lg font-semibold tracking-tight group-hover:text-brand">{analysis.title ?? "Untitled analysis"}</h2>
                </div>
                <StatusPill status={analysis.status} />
              </div>
              <div className="mt-7 grid grid-cols-2 gap-3 rounded-2xl bg-surface-muted/70 p-4 text-sm">
                <div><p className="text-xs text-muted">Documents</p><p className="mt-1 font-semibold">{analysis.files.length}</p></div>
                <div><p className="text-xs text-muted">Last updated</p><p className="mt-1 font-semibold">{formatDate(analysis.updatedAt)}</p></div>
              </div>
              <div className="mt-5 flex items-center justify-between text-sm">
                <span className="text-xs text-muted">Created {formatDate(analysis.createdAt)}</span>
                <span className="inline-flex items-center gap-1.5 font-semibold text-brand">Open analysis <ArrowRight className="transition group-hover:translate-x-0.5" size={15} /></span>
              </div>
            </Link>
          ))}
        </div>
      )}
    </div>
  );
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en", { month: "short", day: "numeric", year: "numeric" }).format(new Date(value));
}
