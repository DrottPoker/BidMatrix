"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import {
  AlertCircle,
  ArrowLeft,
  CalendarDays,
  CheckCircle2,
  ChevronDown,
  ClipboardCheck,
  FileCheck2,
  FileText,
  Gauge,
  ListChecks,
  LoaderCircle,
  Quote,
  ShieldCheck,
  Sparkles,
} from "lucide-react";
import {
  Analysis,
  AnalysisCitation,
  AnalysisFinding,
  AnalysisRequirements,
  apiGet,
  formatApiError,
} from "@/lib/bidmatrix-api";
import { ComplianceMatrix } from "@/components/compliance-matrix";
import { StatusPill, humanize } from "@/components/ui/status-pill";

type Tab = "overview" | "requirements" | "dates" | "documents" | "evaluation";

const tabs: { key: Tab; label: string; icon: typeof Gauge }[] = [
  { key: "overview", label: "Overview", icon: Gauge },
  { key: "requirements", label: "Compliance matrix", icon: ListChecks },
  { key: "dates", label: "Key dates", icon: CalendarDays },
  { key: "documents", label: "Requested documents", icon: FileCheck2 },
  { key: "evaluation", label: "Evaluation", icon: ClipboardCheck },
];

export function AnalysisDetail({ analysisId, backHref = "/app/analyses" }: { analysisId: string; backHref?: string }) {
  const [analysis, setAnalysis] = useState<Analysis | null>(null);
  const [result, setResult] = useState<AnalysisRequirements | null>(null);
  const [tab, setTab] = useState<Tab>("overview");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([
      apiGet<Analysis>(`/v1/analyses/${analysisId}`),
      apiGet<AnalysisRequirements>(`/v1/analyses/${analysisId}/requirements`),
    ])
      .then(([analysisResponse, resultResponse]) => {
        setAnalysis(analysisResponse);
        setResult(resultResponse);
      })
      .catch((requestError: unknown) => setError(formatApiError(requestError)));
  }, [analysisId]);

  if (error) {
    return <div className="page-shell"><ErrorState backHref={backHref} message={error} /></div>;
  }

  if (!analysis || !result) {
    return <div className="page-shell grid min-h-[70vh] place-items-center"><LoaderCircle className="animate-spin text-brand" aria-label="Loading analysis" /></div>;
  }

  const published = result.publication.isPublished;

  return (
    <div className="page-shell space-y-7">
      <header>
        <Link className="inline-flex items-center gap-2 text-sm font-semibold text-muted hover:text-brand" href={backHref}><ArrowLeft size={15} /> Back to analyses</Link>
        <div className="mt-6 flex flex-col justify-between gap-5 xl:flex-row xl:items-end">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-3"><p className="eyebrow">RFP analysis</p><StatusPill status={analysis.status} /></div>
            <h1 className="mt-3 truncate text-3xl font-semibold tracking-[-0.04em] sm:text-4xl">{analysis.title ?? "Untitled analysis"}</h1>
            <p className="mt-3 text-sm text-muted">{analysis.files.length} {analysis.files.length === 1 ? "source document" : "source documents"} · Last updated {formatDate(analysis.updatedAt)}</p>
          </div>
          {published ? (
            <div className="flex items-center gap-3 rounded-2xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm text-emerald-900">
              <ShieldCheck className="shrink-0" size={19} />
              <div><p className="font-semibold">Quality reviewed</p><p className="text-xs text-emerald-800/70">Published {formatDate(result.publication.publishedAt!)}</p></div>
            </div>
          ) : null}
        </div>
      </header>

      {!published ? (
        <ProcessingView analysis={analysis} result={result} />
      ) : (
        <>
          <nav aria-label="Analysis result sections" className="-mx-5 overflow-x-auto border-y border-ink/8 bg-white/70 px-5 backdrop-blur sm:-mx-8 sm:px-8 xl:-mx-12 xl:px-12">
            <div className="mx-auto flex min-w-max max-w-[92rem] gap-1 py-2">
              {tabs.map((item) => {
                const Icon = item.icon;
                const count = item.key === "requirements" ? result.requirements.length : item.key === "dates" ? result.keyDates.length : item.key === "documents" ? result.requestedDocuments.length : item.key === "evaluation" ? result.evaluationCriteria.length : null;
                return (
                  <button aria-label={`${item.label}${count === null ? "" : ` (${count})`}`} className={`flex items-center gap-2 rounded-xl px-3.5 py-2.5 text-sm font-semibold transition ${tab === item.key ? "bg-ink text-white shadow-sm" : "text-muted hover:bg-white hover:text-foreground"}`} key={item.key} onClick={() => setTab(item.key)} type="button">
                    <Icon size={16} /> {item.label}{count !== null ? <span className={`rounded-full px-1.5 py-0.5 text-[10px] ${tab === item.key ? "bg-white/15" : "bg-surface-muted"}`}>{count}</span> : null}
                  </button>
                );
              })}
            </div>
          </nav>

          {tab === "overview" ? <Overview result={result} onNavigate={setTab} /> : null}
          {tab === "requirements" ? <ComplianceMatrix analysisTitle={analysis.title} requirements={result.requirements} /> : null}
          {tab === "dates" ? <FindingSection empty="No key dates were identified in the supplied documents." findings={result.keyDates} kind="date" /> : null}
          {tab === "documents" ? <FindingSection empty="No requested submission documents were identified." findings={result.requestedDocuments} kind="document" /> : null}
          {tab === "evaluation" ? <FindingSection empty="No weighted evaluation criteria were identified." findings={result.evaluationCriteria} kind="evaluation" /> : null}
        </>
      )}
    </div>
  );
}

function ProcessingView({ analysis, result }: { analysis: Analysis; result: AnalysisRequirements }) {
  if (analysis.status === "failed" || result.extractionStatus === "failed") {
    return (
      <AnalysisIssueView
        analysis={analysis}
        message={analysis.failureMessage ?? result.message}
        title="This analysis needs attention"
      />
    );
  }

  if (analysis.status === "cancelled") {
    return (
      <AnalysisIssueView
        analysis={analysis}
        message="This analysis was cancelled before a result was published. Create a new analysis when you are ready to continue."
        title="Analysis cancelled"
      />
    );
  }

  const steps = [
    { label: "Documents received", complete: analysis.files.length > 0 },
    { label: "Content extracted", complete: ["succeeded", "partial"].includes(result.extractionStatus) },
    { label: "Quality review", complete: analysis.status === "completed", active: analysis.status === "requires_review" },
    { label: "Result ready", complete: result.publication.isPublished },
  ];
  return (
    <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
      <section className="panel overflow-hidden">
        <div className="bg-ink p-7 text-white sm:p-9">
          <span className="grid size-12 place-items-center rounded-2xl bg-accent/15 text-accent"><Sparkles size={22} /></span>
          <h2 className="mt-6 text-2xl font-semibold tracking-tight">Your analysis is being prepared.</h2>
          <p className="mt-3 max-w-2xl text-sm leading-7 text-white/65">{result.message}</p>
        </div>
        <div className="p-6 sm:p-8">
          {result.metrics.failedFileCount > 0 || result.metrics.filesRequiringOcr > 0 ? (
            <div className="mb-6 rounded-xl border border-amber-200 bg-amber-50 p-4 text-sm text-amber-950" role="status">
              <p className="font-semibold">Some source documents need attention</p>
              <p className="mt-1 text-xs leading-5 text-amber-900/75">
                {result.metrics.failedFileCount > 0 ? `${result.metrics.failedFileCount} documents could not be extracted. ` : ""}
                {result.metrics.filesRequiringOcr > 0 ? `${result.metrics.filesRequiringOcr} documents appear to require OCR, which is outside the first version. ` : ""}
                Results remain hidden until the quality review is complete.
              </p>
            </div>
          ) : null}
          <ol className="grid gap-5 sm:grid-cols-4">
            {steps.map((step, index) => (
              <li className="relative" key={step.label}>
                <div className={`grid size-8 place-items-center rounded-full text-xs font-bold ${step.complete ? "bg-brand text-white" : step.active ? "bg-accent text-ink ring-4 ring-accent/20" : "bg-surface-muted text-muted"}`}>
                  {step.complete ? <CheckCircle2 size={16} /> : index + 1}
                </div>
                <p className={`mt-3 text-sm font-semibold ${step.active ? "text-brand" : ""}`}>{step.label}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>
      <section className="panel p-6">
        <p className="eyebrow">Source package</p>
        <h2 className="mt-2 font-semibold">{analysis.files.length} documents secured</h2>
        <ul className="mt-5 space-y-3">
          {analysis.files.map((file) => <li className="flex items-center gap-3 text-sm" key={file.id}><span className="grid size-9 shrink-0 place-items-center rounded-xl bg-surface-muted text-brand"><FileText size={16} /></span><span className="min-w-0"><span className="block truncate font-semibold">{file.originalFileName}</span><span className="text-xs text-muted">{formatBytes(file.sizeBytes)}</span></span></li>)}
        </ul>
      </section>
    </div>
  );
}

function AnalysisIssueView({
  analysis,
  title,
  message,
}: {
  analysis: Analysis;
  title: string;
  message: string;
}) {
  return (
    <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
      <section className="panel p-7 sm:p-9">
        <span className="grid size-12 place-items-center rounded-2xl bg-red-50 text-red-700">
          <AlertCircle size={22} />
        </span>
        <h2 className="mt-6 text-2xl font-semibold tracking-tight">{title}</h2>
        <p className="mt-3 max-w-2xl text-sm leading-7 text-muted">{message}</p>
        <p className="mt-5 rounded-xl bg-surface-muted px-4 py-3 text-xs leading-5 text-muted">
          BidMatrix does not fabricate missing content. Use a digital English PDF and start a new analysis, or contact support if the file should be readable.
        </p>
      </section>
      <section className="panel p-6">
        <p className="eyebrow">Source package</p>
        <h2 className="mt-2 font-semibold">{analysis.files.length} uploaded documents</h2>
        <ul className="mt-5 space-y-3">
          {analysis.files.map((file) => (
            <li className="flex items-center gap-3 text-sm" key={file.id}>
              <span className="grid size-9 shrink-0 place-items-center rounded-xl bg-surface-muted text-brand"><FileText size={16} /></span>
              <span className="min-w-0">
                <span className="block truncate font-semibold">{file.originalFileName}</span>
                <span className="text-xs text-muted">{formatBytes(file.sizeBytes)}</span>
              </span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}

function Overview({ result, onNavigate }: { result: AnalysisRequirements; onNavigate: (tab: Tab) => void }) {
  const cards = [
    { label: "Requirements", value: result.metrics.requirementCount, detail: `${result.metrics.mandatoryRequirementCount} mandatory`, icon: ListChecks, tab: "requirements" as Tab },
    { label: "Key dates", value: result.metrics.keyDateCount, detail: "Deadlines and milestones", icon: CalendarDays, tab: "dates" as Tab },
    { label: "Requested documents", value: result.metrics.requestedDocumentCount, detail: "Submission materials", icon: FileCheck2, tab: "documents" as Tab },
    { label: "Evaluation criteria", value: result.metrics.evaluationCriterionCount, detail: "Weighted criteria", icon: ClipboardCheck, tab: "evaluation" as Tab },
  ];
  return (
    <div className="space-y-6">
      <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {cards.map((card) => { const Icon = card.icon; return <button className="panel group p-5 text-left transition hover:-translate-y-0.5 hover:border-brand/30" key={card.label} onClick={() => onNavigate(card.tab)} type="button"><span className="grid size-10 place-items-center rounded-xl bg-surface-muted text-brand"><Icon size={18} /></span><p className="mt-5 text-3xl font-semibold tracking-[-0.04em]">{card.value}</p><p className="mt-1 text-sm font-semibold group-hover:text-brand">{card.label}</p><p className="mt-1 text-xs text-muted">{card.detail}</p></button>; })}
      </section>
      <div className="grid gap-5 xl:grid-cols-[1.2fr_0.8fr]">
        <section className="panel p-6 sm:p-7">
          <div className="flex items-start justify-between gap-4"><div><p className="eyebrow">Documents</p><h2 className="mt-2 text-lg font-semibold">Source package</h2></div><span className="text-xs font-semibold text-muted">{result.metrics.pageCount} pages</span></div>
          <ul className="mt-5 divide-y divide-ink/7">
            {result.documents.map((document) => <li className="flex flex-col justify-between gap-3 py-4 first:pt-0 last:pb-0 sm:flex-row sm:items-center" key={document.analysisFileId}><div className="flex min-w-0 items-center gap-3"><span className="grid size-10 shrink-0 place-items-center rounded-xl bg-surface-muted text-brand"><FileText size={18} /></span><div className="min-w-0"><p className="truncate text-sm font-semibold">{document.originalFileName}</p><p className="mt-1 text-xs text-muted">{humanize(document.documentType ?? "procurement_document")}</p></div></div><p className="text-xs font-semibold text-muted">{document.pageCount ?? 0} pages</p></li>)}
          </ul>
        </section>
        <section className="rounded-[1.4rem] bg-ink p-6 text-white sm:p-7">
          <ShieldCheck className="text-accent" size={22} />
          <p className="mt-5 text-xs font-bold uppercase tracking-[0.18em] text-accent">Review record</p>
          <h2 className="mt-2 text-lg font-semibold">Quality reviewed before delivery</h2>
          <p className="mt-3 text-sm leading-6 text-white/60">{result.publication.reviewNote}</p>
          <dl className="mt-6 grid grid-cols-2 gap-4 border-t border-white/10 pt-5 text-sm"><div><dt className="text-xs text-white/45">Corrections</dt><dd className="mt-1 font-semibold">{result.publication.correctionCount}</dd></div><div><dt className="text-xs text-white/45">Source coverage</dt><dd className="mt-1 font-semibold">{result.metrics.citedRequirementCount}/{result.metrics.requirementCount}</dd></div></dl>
        </section>
      </div>
    </div>
  );
}

function FindingSection({ findings, empty, kind }: { findings: AnalysisFinding[]; empty: string; kind: "date" | "document" | "evaluation" }) {
  return findings.length === 0 ? <div className="panel"><EmptyState text={empty} /></div> : (
    <div className="grid gap-4 xl:grid-cols-2">
      {findings.map((finding) => (
        <article className="panel p-5 sm:p-6" key={finding.id}>
          <div className="flex items-start justify-between gap-4">
            <span className="grid size-10 place-items-center rounded-xl bg-surface-muted text-brand">{kind === "date" ? <CalendarDays size={18} /> : kind === "document" ? <FileCheck2 size={18} /> : <ClipboardCheck size={18} />}</span>
            {finding.weightPercent !== null ? <span className="rounded-full bg-accent px-3 py-1 text-xs font-bold text-ink">{finding.weightPercent}% weight</span> : <span className="rounded-full bg-emerald-50 px-2.5 py-1 text-[10px] font-bold uppercase tracking-wide text-emerald-800">Reviewed</span>}
          </div>
          {finding.dateValue ? <p className="mt-5 text-xs font-bold uppercase tracking-[0.16em] text-brand">{formatLongDate(finding.dateValue)}</p> : null}
          <h2 className="mt-2 text-lg font-semibold tracking-tight">{finding.title}</h2>
          <p className="mt-3 text-sm leading-6 text-muted">{finding.detail}</p>
          <details className="group mt-5 border-t border-ink/8 pt-4"><summary className="inline-flex list-none items-center gap-2 text-xs font-semibold text-brand"><Quote size={14} /> View source <ChevronDown className="transition group-open:rotate-180" size={14} /></summary><Citation citation={finding.citation} className="mt-3" /></details>
        </article>
      ))}
    </div>
  );
}

function Citation({ citation, className = "" }: { citation: AnalysisCitation; className?: string }) {
  return <blockquote className={`rounded-xl border-l-2 border-brand bg-surface-muted/75 px-4 py-3 text-sm ${className}`}><p className="text-xs font-semibold text-brand">{citation.originalFileName} · Page {citation.pageNumber}{citation.sectionText ? ` · ${citation.sectionText}` : ""}</p><p className="mt-2 leading-6 text-muted">“{citation.quoteText}”</p></blockquote>;
}

function EmptyState({ text }: { text: string }) {
  return <div className="px-6 py-14 text-center"><AlertCircle className="mx-auto text-muted" size={24} /><p className="mt-3 text-sm text-muted">{text}</p></div>;
}

function ErrorState({ message, backHref }: { message: string; backHref: string }) {
  return <div className="panel mx-auto max-w-2xl p-8 text-center"><AlertCircle className="mx-auto text-red-700" size={28} /><h1 className="mt-4 text-xl font-semibold">Analysis could not be loaded</h1><p className="mt-2 text-sm text-muted">{message}</p><Link className="button-secondary mt-6" href={backHref}><ArrowLeft size={15} /> Back to analyses</Link></div>;
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en", { month: "short", day: "numeric", year: "numeric" }).format(new Date(value));
}

function formatLongDate(value: string) {
  return new Intl.DateTimeFormat("en", { weekday: "long", month: "long", day: "numeric", year: "numeric", timeZone: "UTC" }).format(new Date(value));
}

function formatBytes(bytes: number) {
  return bytes < 1024 * 1024 ? `${(bytes / 1024).toFixed(1)} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
