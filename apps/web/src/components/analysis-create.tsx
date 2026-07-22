"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useMemo, useRef, useState } from "react";
import type { FormEvent } from "react";
import { ArrowLeft, ArrowRight, CheckCircle2, FileText, LoaderCircle, LockKeyhole, ShieldCheck, UploadCloud, X } from "lucide-react";
import { Analysis, apiMutation, formatApiError } from "@/lib/bidmatrix-api";

export function AnalysisCreate() {
  const router = useRouter();
  const inputRef = useRef<HTMLInputElement>(null);
  const [title, setTitle] = useState("");
  const [files, setFiles] = useState<File[]>([]);
  const [busy, setBusy] = useState(false);
  const [progress, setProgress] = useState("");
  const [error, setError] = useState<string | null>(null);
  const totalBytes = useMemo(() => files.reduce((total, file) => total + file.size, 0), [files]);

  function addFiles(selected: FileList | null) {
    if (!selected) return;
    const pdfs = Array.from(selected).filter((file) => file.type === "application/pdf" || file.name.toLowerCase().endsWith(".pdf"));
    setFiles((current) => [...current, ...pdfs.filter((file) => !current.some((item) => item.name === file.name && item.size === file.size))]);
  }

  async function create(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!title.trim() || files.length === 0) return;
    setBusy(true);
    setError(null);
    try {
      setProgress("Creating secure analysis workspace");
      const analysis = await apiMutation<Analysis>("/v1/analyses", {
        method: "POST",
        headers: { "Content-Type": "application/json", "Idempotency-Key": crypto.randomUUID() },
        body: JSON.stringify({ title: title.trim() }),
      });

      for (const [index, file] of files.entries()) {
        setProgress(`Uploading document ${index + 1} of ${files.length}`);
        const form = new FormData();
        form.append("file", file);
        await apiMutation(`/v1/analyses/${analysis.id}/files`, { method: "POST", body: form });
      }

      setProgress("Submitting for extraction and quality review");
      await apiMutation(`/v1/analyses/${analysis.id}/submit`, { method: "POST" });
      router.push(`/app/analyses/${analysis.id}`);
      router.refresh();
    } catch (requestError) {
      setError(formatApiError(requestError));
      setBusy(false);
      setProgress("");
    }
  }

  return (
    <div className="page-shell">
      <Link className="inline-flex items-center gap-2 text-sm font-semibold text-muted hover:text-brand" href="/app/analyses"><ArrowLeft size={15} /> Back to analyses</Link>
      <div className="mt-7 grid gap-8 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <section>
          <p className="eyebrow">New analysis</p>
          <h1 className="mt-2 max-w-2xl text-3xl font-semibold tracking-[-0.035em] sm:text-4xl">Upload an RFP for a clear, source-linked review.</h1>
          <p className="mt-4 max-w-2xl text-sm leading-7 text-muted">Give the analysis a useful internal name, then add the digital PDF documents that belong to the procurement.</p>

          <form className="panel mt-8 overflow-hidden" onSubmit={create}>
            <div className="border-b border-ink/8 p-6 sm:p-8">
              <label className="block text-sm font-semibold" htmlFor="analysis-title">Analysis name</label>
              <p className="mt-1 text-xs text-muted">Use the customer or procurement name your team will recognize.</p>
              <input className="field mt-4 h-12" disabled={busy} id="analysis-title" maxLength={200} onChange={(event) => setTitle(event.target.value)} placeholder="Example: NordicCare Security Operations RFP" required value={title} />
            </div>

            <div className="p-6 sm:p-8">
              <div className="flex items-end justify-between gap-4">
                <div><p className="text-sm font-semibold">Source documents</p><p className="mt-1 text-xs text-muted">Digital English PDFs, up to the configured file limit.</p></div>
                {files.length ? <button className="text-xs font-semibold text-brand" onClick={() => inputRef.current?.click()} type="button">Add more</button> : null}
              </div>
              <button className="mt-4 grid min-h-48 w-full place-items-center rounded-2xl border border-dashed border-brand/35 bg-brand/[0.035] p-6 text-center transition hover:border-brand hover:bg-brand/[0.06]" disabled={busy} onClick={() => inputRef.current?.click()} type="button">
                <span>
                  <span className="mx-auto grid size-12 place-items-center rounded-2xl bg-white text-brand shadow-sm"><UploadCloud size={22} /></span>
                  <span className="mt-4 block text-sm font-semibold">Choose PDF documents</span>
                  <span className="mt-1 block text-xs text-muted">Select one or several files</span>
                </span>
              </button>
              <input accept="application/pdf,.pdf" className="sr-only" disabled={busy} multiple onChange={(event) => { addFiles(event.target.files); event.target.value = ""; }} ref={inputRef} type="file" />

              {files.length ? (
                <ul className="mt-4 space-y-2">
                  {files.map((file) => (
                    <li className="flex items-center gap-3 rounded-xl border border-ink/8 bg-white px-4 py-3" key={`${file.name}-${file.size}`}>
                      <span className="grid size-9 shrink-0 place-items-center rounded-lg bg-surface-muted text-brand"><FileText size={17} /></span>
                      <div className="min-w-0 flex-1"><p className="truncate text-sm font-semibold">{file.name}</p><p className="mt-0.5 text-xs text-muted">{formatBytes(file.size)}</p></div>
                      <button aria-label={`Remove ${file.name}`} className="grid size-8 place-items-center rounded-lg text-muted hover:bg-red-50 hover:text-red-700" disabled={busy} onClick={() => setFiles((current) => current.filter((item) => item !== file))} type="button"><X size={15} /></button>
                    </li>
                  ))}
                </ul>
              ) : null}

              {error ? <div className="mt-5 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-900" role="alert">{error}</div> : null}
              <div className="mt-7 flex flex-col-reverse justify-between gap-4 border-t border-ink/8 pt-6 sm:flex-row sm:items-center">
                <p className="text-xs text-muted">{files.length ? `${files.length} documents · ${formatBytes(totalBytes)}` : "Add at least one PDF to continue"}</p>
                <button className="button-primary h-12" disabled={busy || !title.trim() || files.length === 0} type="submit">
                  {busy ? <LoaderCircle className="animate-spin" size={17} /> : <ArrowRight size={17} />}
                  {busy ? progress : "Start analysis"}
                </button>
              </div>
            </div>
          </form>
        </section>

        <aside className="space-y-4 xl:pt-24">
          <div className="rounded-2xl bg-ink p-5 text-white">
            <ShieldCheck className="text-accent" size={21} />
            <h2 className="mt-4 font-semibold">What you will receive</h2>
            <ul className="mt-4 space-y-3 text-sm text-white/65">
              {["Requirements and mandatory items", "Key dates and submission deadlines", "Requested documents and evidence", "Evaluation criteria and weightings"].map((item) => <li className="flex gap-2.5" key={item}><CheckCircle2 className="mt-0.5 shrink-0 text-accent" size={15} />{item}</li>)}
            </ul>
          </div>
          <div className="panel p-5">
            <LockKeyhole className="text-brand" size={20} />
            <h2 className="mt-3 text-sm font-semibold">Private and controlled</h2>
            <p className="mt-2 text-xs leading-5 text-muted">Files remain tenant-isolated. Results are not presented as ready until the BidMatrix quality review is complete.</p>
          </div>
        </aside>
      </div>
    </div>
  );
}

function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}
