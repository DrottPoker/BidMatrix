"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { AlertTriangle, FileLock2, FileSearch, LoaderCircle, Quote } from "lucide-react";
import { Analysis, AnalysisRequirements, apiGet } from "@/lib/bidmatrix-api";

export function AnalysisDetail({ analysisId, backHref = "/analyses" }: { analysisId: string; backHref?: string }) {
  const [analysis, setAnalysis] = useState<Analysis | null>(null);
  const [extraction, setExtraction] = useState<AnalysisRequirements | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([
      apiGet<Analysis>(`/v1/analyses/${analysisId}`),
      apiGet<AnalysisRequirements>(`/v1/analyses/${analysisId}/requirements`),
    ])
      .then(([analysisResponse, extractionResponse]) => {
        setAnalysis(analysisResponse);
        setExtraction(extractionResponse);
      })
      .catch((requestError: unknown) =>
        setError(requestError instanceof Error ? requestError.message : "Analysis could not be loaded."),
      );
  }, [analysisId]);

  if (error) {
    return <p className="rounded-xl bg-red-50 p-4 text-red-900">{error}</p>;
  }

  if (!analysis || !extraction) {
    return <LoaderCircle className="animate-spin text-brand" aria-label="Loading analysis" />;
  }

  return (
    <div className="space-y-6">
      <div>
        <Link className="text-sm font-semibold text-brand" href={backHref}>
          Back to analyses
        </Link>
        <h1 className="mt-3 text-4xl font-semibold tracking-tight">
          {analysis.title ?? "Untitled analysis"}
        </h1>
        <p className="mt-2 font-mono text-xs text-muted">{analysis.id}</p>
      </div>
      <section className="rounded-2xl border bg-white p-6">
        <h2 className="font-semibold">Intake state</h2>
        <dl className="mt-4 grid gap-4 text-sm sm:grid-cols-4">
          <div><dt className="text-muted">Status</dt><dd className="mt-1 font-semibold">{analysis.status}</dd></div>
          <div><dt className="text-muted">Files</dt><dd className="mt-1 font-semibold">{analysis.files.length}</dd></div>
          <div><dt className="text-muted">Extraction</dt><dd className="mt-1 font-semibold">{extraction.extractionStatus.replaceAll("_", " ")}</dd></div>
          <div><dt className="text-muted">Human review</dt><dd className="mt-1 font-semibold">Required</dd></div>
        </dl>
      </section>
      <section className={`flex gap-4 rounded-2xl border p-6 ${extraction.extractionStatus === "partial" ? "border-red-200 bg-red-50 text-red-950" : "border-amber-200 bg-amber-50 text-amber-950"}`}>
        <AlertTriangle className="shrink-0" aria-hidden="true" />
        <div>
          <h2 className="font-semibold">Manual review is required</h2>
          <p className="mt-2 text-sm leading-6">{extraction.message}</p>
        </div>
      </section>
      <section className="rounded-2xl border bg-white p-6">
        <h2 className="flex items-center gap-2 font-semibold"><FileSearch aria-hidden="true" size={18} />F1 extraction summary</h2>
        <dl className="mt-4 grid gap-4 text-sm sm:grid-cols-3 lg:grid-cols-6">
          <div><dt className="text-muted">Pages</dt><dd className="mt-1 text-xl font-semibold">{extraction.metrics.pageCount}</dd></div>
          <div><dt className="text-muted">Requirements</dt><dd className="mt-1 text-xl font-semibold">{extraction.metrics.requirementCount}</dd></div>
          <div><dt className="text-muted">Mandatory</dt><dd className="mt-1 text-xl font-semibold">{extraction.metrics.mandatoryRequirementCount}</dd></div>
          <div><dt className="text-muted">Cited</dt><dd className="mt-1 text-xl font-semibold">{extraction.metrics.citedRequirementCount}</dd></div>
          <div><dt className="text-muted">Needs OCR</dt><dd className="mt-1 text-xl font-semibold">{extraction.metrics.filesRequiringOcr}</dd></div>
          <div><dt className="text-muted">Failed files</dt><dd className="mt-1 text-xl font-semibold">{extraction.metrics.failedFileCount}</dd></div>
        </dl>
        <ul className="mt-6 space-y-3">
          {extraction.documents.map((document) => (
            <li className="rounded-xl bg-zinc-50 px-4 py-3 text-sm" key={document.analysisFileId}>
              <span className="font-semibold">{document.originalFileName}</span>
              <span className="ml-3 text-muted">{document.documentType?.replaceAll("_", " ") ?? "unclassified"}</span>
              <span className="ml-3 text-muted">{document.pageCount ?? 0} pages</span>
              <span className="ml-3 font-medium">{document.extractionStatus.replaceAll("_", " ")}</span>
            </li>
          ))}
        </ul>
      </section>
      <section className="rounded-2xl border bg-white p-6">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <h2 className="font-semibold">Extracted requirements</h2>
            <p className="mt-1 text-sm text-muted">Draft findings only. Verify the source quote before relying on a requirement.</p>
          </div>
          <span className="rounded-full bg-amber-100 px-3 py-1 text-xs font-semibold text-amber-900">Pending review</span>
        </div>
        {extraction.requirements.length === 0 ? (
          <p className="mt-5 rounded-xl bg-zinc-50 p-4 text-sm text-muted">No requirements were extracted. Check OCR and file-failure indicators before concluding that the source contains none.</p>
        ) : (
          <ol className="mt-5 space-y-4">
            {extraction.requirements.map((requirement) => (
              <li className="rounded-xl border p-5" key={requirement.id}>
                <div className="flex flex-wrap items-center gap-2 text-xs font-semibold uppercase tracking-wide">
                  <span className={requirement.mandatory ? "rounded-full bg-red-100 px-2 py-1 text-red-900" : "rounded-full bg-zinc-100 px-2 py-1 text-zinc-700"}>
                    {requirement.mandatory ? "Mandatory" : "Optional"}
                  </span>
                  <span className="text-muted">{requirement.category.replaceAll("_", " ")}</span>
                  <span className="text-muted">{Math.round(requirement.confidence * 100)}% confidence</span>
                  {requirement.requirementCode ? <span className="font-mono text-muted">{requirement.requirementCode}</span> : null}
                </div>
                <p className="mt-3 leading-7">{requirement.requirementText}</p>
                <div className="mt-4 space-y-2">
                  {requirement.citations.map((citation) => (
                    <blockquote className="rounded-lg bg-zinc-50 p-3 text-sm" key={citation.id}>
                      <p className="flex items-center gap-2 font-semibold"><Quote aria-hidden="true" size={14} />{citation.originalFileName}, page {citation.pageNumber}</p>
                      <p className="mt-2 text-muted">{citation.quoteText}</p>
                    </blockquote>
                  ))}
                </div>
              </li>
            ))}
          </ol>
        )}
      </section>
      <section className="rounded-2xl border bg-white p-6">
        <h2 className="flex items-center gap-2 font-semibold"><FileLock2 aria-hidden="true" size={18} />Quarantined files</h2>
        <ul className="mt-4 space-y-3">
          {analysis.files.map((file) => (
            <li className="rounded-xl bg-zinc-50 px-4 py-3 text-sm" key={file.id}>
              <span className="font-semibold">{file.originalFileName}</span>
              <span className="ml-3 text-muted">{file.scanStatus.replaceAll("_", " ")}</span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}
