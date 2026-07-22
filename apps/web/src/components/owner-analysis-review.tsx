"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { ArrowLeft, Check, FileCheck2, LoaderCircle, Quote, Send, X } from "lucide-react";
import { Analysis, AnalysisFinding, AnalysisRequirement, AnalysisRequirements, apiGet, apiMutation, formatApiError } from "@/lib/bidmatrix-api";
import { StatusPill, humanize } from "@/components/ui/status-pill";

export function OwnerAnalysisReview({ analysisId }: { analysisId: string }) {
  const [analysis, setAnalysis] = useState<Analysis | null>(null);
  const [result, setResult] = useState<AnalysisRequirements | null>(null);
  const [reviewNote, setReviewNote] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([
      apiGet<Analysis>(`/v1/analyses/${analysisId}`),
      apiGet<AnalysisRequirements>(`/owner/v1/analyses/${analysisId}/review`),
    ]).then(([analysisResponse, resultResponse]) => {
      setAnalysis(analysisResponse);
      setResult(resultResponse);
      setReviewNote(resultResponse.publication.reviewNote ?? "Source citations and extracted findings were checked for customer delivery.");
    }).catch((requestError: unknown) => setError(formatApiError(requestError)));
  }, [analysisId]);

  async function publish() {
    if (!result || !window.confirm("Publish this quality-reviewed result to the customer?")) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await apiMutation<AnalysisRequirements>(`/owner/v1/analyses/${analysisId}/publish`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ reviewNote, confirmation: "PUBLISH REVIEWED ANALYSIS" }),
      });
      setResult(updated);
      setAnalysis((current) => current ? { ...current, status: "completed", requiresHumanReview: false } : current);
    } catch (requestError) {
      setError(formatApiError(requestError));
    } finally {
      setBusy(false);
    }
  }

  if (error && (!analysis || !result)) return <div className="rounded-2xl border border-red-200 bg-red-50 p-5 text-sm text-red-900">{error}</div>;
  if (!analysis || !result) return <div className="grid min-h-72 place-items-center"><LoaderCircle className="animate-spin text-brand" aria-label="Loading review" /></div>;

  const findings = [...result.keyDates, ...result.requestedDocuments, ...result.evaluationCriteria];
  const canReview = analysis.status === "requires_review";

  return (
    <div className="space-y-6">
      <header className="flex flex-col justify-between gap-5 lg:flex-row lg:items-end">
        <div><Link className="inline-flex items-center gap-2 text-sm font-semibold text-muted hover:text-brand" href="/owner/analyses"><ArrowLeft size={15} /> Review queue</Link><div className="mt-4 flex flex-wrap items-center gap-3"><h1 className="text-3xl font-semibold tracking-tight">{analysis.title ?? "Untitled analysis"}</h1><StatusPill status={analysis.status} /></div><p className="mt-2 text-sm text-muted">Review extracted content, correct errors, reject false positives, then publish.</p></div>
        <div className="grid grid-cols-3 gap-3 rounded-2xl border bg-white p-4 text-center text-sm"><Metric label="Requirements" value={result.requirements.length} /><Metric label="Findings" value={findings.length} /><Metric label="Pending" value={result.metrics.pendingReviewCount} /></div>
      </header>

      {error ? <div className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-900" role="alert">{error}</div> : null}

      <section className="rounded-2xl border bg-white p-5">
        <h2 className="font-semibold">Source documents</h2>
        <div className="mt-4 flex flex-wrap gap-3">{result.documents.map((document) => <div className="flex items-center gap-2 rounded-xl bg-stone-50 px-3 py-2 text-xs" key={document.analysisFileId}><FileCheck2 className="text-brand" size={15} /><span className="font-semibold">{document.originalFileName}</span><span className="text-muted">{document.pageCount ?? 0} pages</span></div>)}</div>
      </section>

      <section className="space-y-3">
        <div><p className="eyebrow">Review set</p><h2 className="mt-1 text-xl font-semibold">Requirements</h2></div>
        {result.requirements.map((requirement) => <RequirementEditor analysisId={analysisId} disabled={!canReview} key={requirement.id} onUpdated={setResult} requirement={requirement} />)}
      </section>

      <section className="space-y-3">
        <div><p className="eyebrow">Review set</p><h2 className="mt-1 text-xl font-semibold">Dates, documents, and evaluation</h2></div>
        {findings.length === 0 ? <div className="rounded-2xl border border-dashed bg-white p-8 text-center text-sm text-muted">No additional F2 findings were detected.</div> : findings.map((finding) => <FindingEditor analysisId={analysisId} disabled={!canReview} finding={finding} key={finding.id} onUpdated={setResult} />)}
      </section>

      <section className="sticky bottom-4 z-20 rounded-2xl border border-brand/25 bg-white/95 p-5 shadow-[0_18px_60px_rgba(16,36,29,0.16)] backdrop-blur-xl">
        <div className="grid gap-4 lg:grid-cols-[1fr_auto] lg:items-end">
          <label className="block"><span className="text-sm font-semibold">Customer-facing review note</span><textarea className="field mt-2 min-h-20 py-3" disabled={!canReview || busy} maxLength={2000} onChange={(event) => setReviewNote(event.target.value)} value={reviewNote} /></label>
          <button className="button-primary" disabled={!canReview || busy || reviewNote.trim().length < 10} onClick={() => void publish()} type="button">{busy ? <LoaderCircle className="animate-spin" size={16} /> : <Send size={16} />}{result.publication.isPublished ? "Published" : "Publish reviewed result"}</button>
        </div>
      </section>
    </div>
  );
}

function RequirementEditor({ analysisId, requirement, disabled, onUpdated }: { analysisId: string; requirement: AnalysisRequirement; disabled: boolean; onUpdated: (result: AnalysisRequirements) => void }) {
  const [text, setText] = useState(requirement.requirementText);
  const [category, setCategory] = useState(requirement.category);
  const [mandatory, setMandatory] = useState(requirement.mandatory);
  const [note, setNote] = useState(requirement.correctionNote ?? "");
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>, reviewStatus: "accepted" | "corrected" | "rejected") {
    event.preventDefault();
    setBusy(true);
    try {
      onUpdated(await apiMutation<AnalysisRequirements>(`/owner/v1/analyses/${analysisId}/requirements/${requirement.id}`, { method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ requirementText: text, category, mandatory, reviewStatus, correctionNote: reviewStatus === "corrected" ? note || "Corrected during owner review." : note || null, expectedVersion: requirement.version }) }));
    } finally { setBusy(false); }
  }

  return <form className="rounded-2xl border bg-white p-5" onSubmit={(event) => void submit(event, text === requirement.originalRequirementText ? "accepted" : "corrected")}><div className="flex flex-wrap items-center gap-2 text-xs"><span className="rounded-full bg-stone-100 px-2.5 py-1 font-semibold">{requirement.requirementCode ?? "Requirement"}</span><StatusPill status={requirement.reviewStatus} /><span className="text-muted">{Math.round(requirement.confidence * 100)}% confidence</span></div><textarea className="field mt-4 min-h-28 py-3 leading-6" disabled={disabled || busy} onChange={(event) => setText(event.target.value)} value={text} /><div className="mt-3 grid gap-3 sm:grid-cols-[1fr_auto]"><input className="field h-10" disabled={disabled || busy} onChange={(event) => setCategory(event.target.value)} value={category} /><label className="flex items-center gap-2 rounded-xl border px-3 text-xs font-semibold"><input checked={mandatory} disabled={disabled || busy} onChange={(event) => setMandatory(event.target.checked)} type="checkbox" /> Mandatory</label></div><input className="field mt-3 h-10" disabled={disabled || busy} onChange={(event) => setNote(event.target.value)} placeholder="Correction note when content changes" value={note} /><Source quote={requirement.citations[0]?.quoteText} source={requirement.citations[0] ? `${requirement.citations[0].originalFileName}, page ${requirement.citations[0].pageNumber}` : "No source"} /><Actions busy={busy} disabled={disabled} onReject={(event) => void submit(event, "rejected")} /></form>;
}

function FindingEditor({ analysisId, finding, disabled, onUpdated }: { analysisId: string; finding: AnalysisFinding; disabled: boolean; onUpdated: (result: AnalysisRequirements) => void }) {
  const [title, setTitle] = useState(finding.title);
  const [detail, setDetail] = useState(finding.detail);
  const [dateValue, setDateValue] = useState(finding.dateValue?.slice(0, 10) ?? "");
  const [weight, setWeight] = useState(finding.weightPercent?.toString() ?? "");
  const [note, setNote] = useState(finding.correctionNote ?? "");
  const [busy, setBusy] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>, reviewStatus: "accepted" | "corrected" | "rejected") {
    event.preventDefault(); setBusy(true);
    try { onUpdated(await apiMutation<AnalysisRequirements>(`/owner/v1/analyses/${analysisId}/findings/${finding.id}`, { method: "PATCH", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ title, detail, dateValue: dateValue ? new Date(`${dateValue}T00:00:00Z`).toISOString() : null, weightPercent: weight ? Number(weight) : null, reviewStatus, correctionNote: reviewStatus === "corrected" ? note || "Corrected during owner review." : note || null, expectedVersion: finding.version }) })); } finally { setBusy(false); }
  }

  const changed = title !== finding.title || detail !== finding.originalDetail || dateValue !== (finding.dateValue?.slice(0, 10) ?? "") || weight !== (finding.weightPercent?.toString() ?? "");
  return <form className="rounded-2xl border bg-white p-5" onSubmit={(event) => void submit(event, changed ? "corrected" : "accepted")}><div className="flex flex-wrap items-center gap-2 text-xs"><span className="rounded-full bg-brand/8 px-2.5 py-1 font-semibold text-brand">{humanize(finding.findingType)}</span><StatusPill status={finding.reviewStatus} /><span className="text-muted">{Math.round(finding.confidence * 100)}% confidence</span></div><input className="field mt-4 h-11 font-semibold" disabled={disabled || busy} onChange={(event) => setTitle(event.target.value)} value={title} /><textarea className="field mt-3 min-h-24 py-3 leading-6" disabled={disabled || busy} onChange={(event) => setDetail(event.target.value)} value={detail} /><div className="mt-3 grid gap-3 sm:grid-cols-2"><input className="field h-10" disabled={disabled || busy || finding.findingType !== "key_date"} onChange={(event) => setDateValue(event.target.value)} type="date" value={dateValue} /><input className="field h-10" disabled={disabled || busy || finding.findingType !== "evaluation_criterion"} max="100" min="0" onChange={(event) => setWeight(event.target.value)} placeholder="Weight %" step="0.01" type="number" value={weight} /></div><input className="field mt-3 h-10" disabled={disabled || busy} onChange={(event) => setNote(event.target.value)} placeholder="Correction note when content changes" value={note} /><Source quote={finding.citation.quoteText} source={`${finding.citation.originalFileName}, page ${finding.citation.pageNumber}`} /><Actions busy={busy} disabled={disabled} onReject={(event) => void submit(event, "rejected")} /></form>;
}

function Source({ source, quote }: { source: string; quote?: string }) { return <div className="mt-4 rounded-xl bg-stone-50 p-3 text-xs"><p className="flex items-center gap-2 font-semibold text-brand"><Quote size={13} />{source}</p><p className="mt-2 leading-5 text-muted">{quote ?? "No exact quote available."}</p></div>; }
function Actions({ busy, disabled, onReject }: { busy: boolean; disabled: boolean; onReject: (event: FormEvent<HTMLFormElement>) => void }) { return <div className="mt-4 flex flex-wrap gap-2"><button className="inline-flex h-9 items-center gap-2 rounded-xl bg-brand px-3 text-xs font-semibold text-white disabled:opacity-50" disabled={disabled || busy} type="submit"><Check size={14} />Save review</button><button className="inline-flex h-9 items-center gap-2 rounded-xl border border-red-200 px-3 text-xs font-semibold text-red-700 disabled:opacity-50" disabled={disabled || busy} onClick={(event) => onReject(event as unknown as FormEvent<HTMLFormElement>)} type="button"><X size={14} />Reject finding</button></div>; }
function Metric({ label, value }: { label: string; value: number }) { return <div className="min-w-20"><p className="text-xl font-semibold">{value}</p><p className="mt-1 text-[10px] font-bold uppercase tracking-wide text-muted">{label}</p></div>; }
