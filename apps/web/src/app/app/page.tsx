import Link from "next/link";
import { ArrowRight, FileLock2 } from "lucide-react";

export default function CustomerAppPage() {
  return (
    <main className="mx-auto max-w-6xl px-5 py-12 sm:px-8">
      <section className="rounded-[2rem] border bg-white p-8 sm:p-12">
        <span className="inline-flex items-center gap-2 rounded-full bg-brand/8 px-3 py-1.5 text-xs font-bold uppercase tracking-wide text-brand"><FileLock2 size={15} />Controlled RFP intake</span>
        <h1 className="mt-6 max-w-2xl text-4xl font-semibold tracking-tight sm:text-5xl">Trace every requirement back to its source.</h1>
        <p className="mt-5 max-w-2xl leading-7 text-muted">Extraction Prototype F1 processes digital English PDFs, preserves page identity, and creates sourced requirement candidates for mandatory human review.</p>
        <Link className="mt-8 inline-flex h-11 items-center gap-2 rounded-xl bg-brand px-5 text-sm font-semibold text-white" href="/app/analyses">Open analyses <ArrowRight size={16} /></Link>
      </section>
    </main>
  );
}
