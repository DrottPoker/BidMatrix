"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import type { FormEvent } from "react";
import {
  AlertTriangle,
  FileCheck2,
  FileUp,
  LoaderCircle,
  Plus,
  RefreshCw,
  Send,
} from "lucide-react";
import {
  Analysis,
  ApiError,
  apiGet,
  apiMutation,
} from "@/lib/bidmatrix-api";

type AnalysisListResponse = { analyses: Analysis[] };

export type AnalysisWorkspaceProps = {
  heading?: string;
  listEndpoint?: string;
  detailBasePath?: string;
};

export function AnalysisWorkspace({
  heading = "RFP analyses",
  listEndpoint = "/v1/analyses",
  detailBasePath = "/analyses",
}: AnalysisWorkspaceProps) {
  const [analyses, setAnalyses] = useState<Analysis[]>([]);
  const [title, setTitle] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setError(null);
    try {
      const response = await apiGet<AnalysisListResponse>(listEndpoint);
      setAnalyses(response.analyses);
    } catch (requestError) {
      setError(formatError(requestError));
    } finally {
      setLoading(false);
    }
  }, [listEndpoint]);

  useEffect(() => {
    let active = true;

    apiGet<AnalysisListResponse>(listEndpoint)
      .then((response) => {
        if (active) setAnalyses(response.analyses);
      })
      .catch((requestError: unknown) => {
        if (active) setError(formatError(requestError));
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => {
      active = false;
    };
  }, [listEndpoint]);

  async function createAnalysis(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setBusyId("create");
    setError(null);
    try {
      await apiMutation<Analysis>("/v1/analyses", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "Idempotency-Key": crypto.randomUUID(),
        },
        body: JSON.stringify({ title: title || null }),
      });
      setTitle("");
      await refresh();
    } catch (requestError) {
      setError(formatError(requestError));
    } finally {
      setBusyId(null);
    }
  }

  async function upload(analysisId: string, file: File) {
    setBusyId(analysisId);
    setError(null);
    try {
      const form = new FormData();
      form.append("file", file);
      await apiMutation(`/v1/analyses/${analysisId}/files`, {
        method: "POST",
        body: form,
      });
      await refresh();
    } catch (requestError) {
      setError(formatError(requestError));
    } finally {
      setBusyId(null);
    }
  }

  async function submit(analysisId: string) {
    setBusyId(analysisId);
    setError(null);
    try {
      await apiMutation(`/v1/analyses/${analysisId}/submit`, { method: "POST" });
      await refresh();
    } catch (requestError) {
      setError(formatError(requestError));
    } finally {
      setBusyId(null);
    }
  }

  return (
    <main className="min-h-screen bg-background px-5 py-10 sm:px-8">
      <div className="mx-auto max-w-6xl">
        <div className="flex flex-col justify-between gap-5 sm:flex-row sm:items-end">
          <div>
            <Link className="text-sm font-semibold text-brand" href="/">
              BidMatrix
            </Link>
            <h1 className="mt-3 text-4xl font-semibold tracking-tight">{heading}</h1>
            <p className="mt-3 max-w-2xl text-muted">
              Upload digital English PDF files for page-preserving F1 extraction. Every sourced
              requirement candidate remains pending mandatory human review.
            </p>
          </div>
          <button
            className="inline-flex h-11 items-center justify-center gap-2 rounded-xl border bg-white px-4 text-sm font-semibold"
            onClick={() => void refresh()}
            type="button"
          >
            <RefreshCw aria-hidden="true" size={16} />
            Refresh
          </button>
        </div>

        <form className="mt-10 flex flex-col gap-3 rounded-2xl border bg-white p-5 sm:flex-row" onSubmit={createAnalysis}>
          <input
            className="h-11 flex-1 rounded-xl border px-4 outline-none focus:border-brand"
            placeholder="Analysis title"
            maxLength={200}
            value={title}
            onChange={(event) => setTitle(event.target.value)}
          />
          <button
            className="inline-flex h-11 items-center justify-center gap-2 rounded-xl bg-brand px-5 text-sm font-semibold text-white disabled:opacity-60"
            disabled={busyId === "create"}
            type="submit"
          >
            <Plus aria-hidden="true" size={17} />
            Create analysis
          </button>
        </form>

        {error ? (
          <div className="mt-5 flex gap-3 rounded-2xl bg-red-50 p-4 text-sm text-red-900" role="alert">
            <AlertTriangle className="shrink-0" aria-hidden="true" size={18} />
            <div>
              <p>{error}</p>
              {error.includes("Sign in") ? (
                <Link className="mt-2 inline-block font-semibold underline" href="/login">
                  Open sign in
                </Link>
              ) : null}
            </div>
          </div>
        ) : null}

        <div className="mt-6 space-y-4">
          {loading ? (
            <div className="grid min-h-40 place-items-center rounded-2xl border bg-white">
              <LoaderCircle className="animate-spin text-brand" aria-label="Loading analyses" />
            </div>
          ) : null}
          {!loading && analyses.length === 0 ? (
            <div className="rounded-2xl border border-dashed bg-white p-10 text-center text-sm text-muted">
              No analyses exist for this organization yet.
            </div>
          ) : null}
          {analyses.map((analysis) => (
            <article key={analysis.id} className="rounded-2xl border bg-white p-5 shadow-sm">
              <div className="flex flex-col justify-between gap-4 sm:flex-row sm:items-start">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="text-lg font-semibold">{analysis.title ?? "Untitled analysis"}</h2>
                    <StatusBadge status={analysis.status} />
                  </div>
                  <p className="mt-1 font-mono text-xs text-muted">{analysis.id}</p>
                </div>
                <Link className="text-sm font-semibold text-brand" href={`${detailBasePath}/${analysis.id}`}>
                  View details
                </Link>
              </div>

              <div className="mt-5 grid gap-3">
                {analysis.files.map((file) => (
                  <div key={file.id} className="flex flex-col justify-between gap-3 rounded-xl bg-zinc-50 px-4 py-3 sm:flex-row sm:items-center">
                    <div className="flex items-center gap-3">
                      <FileCheck2 className="text-brand" aria-hidden="true" size={18} />
                      <div>
                        <p className="text-sm font-semibold">{file.originalFileName}</p>
                        <p className="text-xs text-muted">{formatBytes(file.sizeBytes)}</p>
                      </div>
                    </div>
                    <StatusBadge status={file.scanStatus} />
                  </div>
                ))}
              </div>

              {analysis.files.some((file) => file.scanStatus === "development_bypass") ? (
                <p className="mt-4 rounded-xl bg-amber-50 px-4 py-3 text-xs font-medium text-amber-900">
                  Development scan bypass is active. This state is forbidden outside Development.
                </p>
              ) : null}

              <div className="mt-5 flex flex-col gap-3 sm:flex-row">
                <label className="inline-flex h-10 cursor-pointer items-center justify-center gap-2 rounded-xl border px-4 text-sm font-semibold">
                  <FileUp aria-hidden="true" size={16} />
                  Upload PDF
                  <input
                    className="sr-only"
                    type="file"
                    accept="application/pdf,.pdf"
                    disabled={busyId === analysis.id}
                    onChange={(event) => {
                      const file = event.target.files?.[0];
                      if (file) void upload(analysis.id, file);
                      event.target.value = "";
                    }}
                  />
                </label>
                <button
                  className="inline-flex h-10 items-center justify-center gap-2 rounded-xl bg-brand px-4 text-sm font-semibold text-white disabled:opacity-50"
                  disabled={busyId === analysis.id || analysis.files.length === 0 || !["draft", "quarantined"].includes(analysis.status)}
                  onClick={() => void submit(analysis.id)}
                  type="button"
                >
                  <Send aria-hidden="true" size={16} />
                  Submit for manual review
                </button>
              </div>
            </article>
          ))}
        </div>
      </div>
    </main>
  );
}

function StatusBadge({ status }: { status: string }) {
  return (
    <span className="inline-flex rounded-full bg-zinc-100 px-2.5 py-1 text-xs font-semibold text-zinc-700">
      {status.replaceAll("_", " ")}
    </span>
  );
}

function formatBytes(bytes: number) {
  return bytes < 1024 ? `${bytes} bytes` : `${(bytes / 1024).toFixed(1)} KiB`;
}

function formatError(error: unknown) {
  if (error instanceof ApiError && error.status === 401) {
    return "Sign in is required to access this organization workspace.";
  }
  return error instanceof Error ? error.message : "The request failed.";
}
