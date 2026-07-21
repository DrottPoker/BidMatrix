"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { AlertTriangle, FileLock2, LoaderCircle } from "lucide-react";
import { Analysis, apiGet } from "@/lib/bidmatrix-api";

export function AnalysisDetail({ analysisId, backHref = "/analyses" }: { analysisId: string; backHref?: string }) {
  const [analysis, setAnalysis] = useState<Analysis | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    apiGet<Analysis>(`/v1/analyses/${analysisId}`)
      .then(setAnalysis)
      .catch((requestError: unknown) =>
        setError(requestError instanceof Error ? requestError.message : "Analysis could not be loaded."),
      );
  }, [analysisId]);

  if (error) {
    return <p className="rounded-xl bg-red-50 p-4 text-red-900">{error}</p>;
  }

  if (!analysis) {
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
        <dl className="mt-4 grid gap-4 text-sm sm:grid-cols-3">
          <div><dt className="text-muted">Status</dt><dd className="mt-1 font-semibold">{analysis.status}</dd></div>
          <div><dt className="text-muted">Files</dt><dd className="mt-1 font-semibold">{analysis.files.length}</dd></div>
          <div><dt className="text-muted">Human review</dt><dd className="mt-1 font-semibold">Required</dd></div>
        </dl>
      </section>
      <section className="flex gap-4 rounded-2xl border border-amber-200 bg-amber-50 p-6 text-amber-950">
        <AlertTriangle className="shrink-0" aria-hidden="true" />
        <div>
          <h2 className="font-semibold">Requirement extraction is not implemented</h2>
          <p className="mt-2 text-sm leading-6">
            Foundation Release F0 stores and validates the PDF, then creates a manual-review task. It does not generate requirements, scores, or legal conclusions.
          </p>
        </div>
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
