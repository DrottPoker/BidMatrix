import Link from "next/link";
import { ArrowRight, CalendarDays, CheckCircle2, FileCheck2, FileSearch, ListChecks, Quote, ShieldCheck } from "lucide-react";

const outputs = [
  { icon: ListChecks, title: "Requirements", text: "Mandatory and optional requirements presented in a clear reviewable list." },
  { icon: CalendarDays, title: "Key dates", text: "Submission deadlines, questions, awards, and contract milestones." },
  { icon: FileCheck2, title: "Requested documents", text: "Certificates, plans, references, pricing, and other requested material." },
];

export default function Home() {
  return (
    <main className="min-h-screen overflow-hidden bg-background">
      <header className="relative z-20 border-b border-ink/8 bg-background/90 backdrop-blur-xl">
        <div className="mx-auto flex h-20 max-w-7xl items-center justify-between px-5 sm:px-8">
          <Link className="flex items-center gap-3 font-semibold tracking-tight" href="/"><span className="grid size-10 place-items-center rounded-xl bg-ink text-sm font-black text-accent">BM</span>BidMatrix</Link>
          <div className="flex items-center gap-3"><Link className="hidden text-sm font-semibold text-muted hover:text-foreground sm:block" href="/login">Sign in</Link><Link className="button-primary" href="/login">Open workspace <ArrowRight size={15} /></Link></div>
        </div>
      </header>

      <section className="relative isolate">
        <div className="absolute inset-0 -z-10 bg-[radial-gradient(circle_at_80%_5%,rgba(216,255,114,0.42),transparent_28rem)]" />
        <div className="mx-auto grid max-w-7xl items-center gap-14 px-5 py-20 sm:px-8 sm:py-28 lg:grid-cols-[1.05fr_0.95fr] lg:py-32">
          <div>
            <p className="eyebrow flex items-center gap-2"><FileSearch size={15} /> Built for focused IT and cybersecurity teams</p>
            <h1 className="mt-6 max-w-4xl text-5xl font-semibold leading-[0.98] tracking-[-0.055em] sm:text-6xl lg:text-7xl">Understand the RFP before you commit.</h1>
            <p className="mt-7 max-w-2xl text-lg leading-8 text-muted">BidMatrix turns dense procurement documents into a quality-reviewed, source-linked overview. Your company makes the decision. We make the information easier to trust.</p>
            <div className="mt-9 flex flex-col gap-3 sm:flex-row"><Link className="button-primary h-12 px-6" href="/login">Open BidMatrix <ArrowRight size={17} /></Link><a className="button-secondary h-12" href="#how-it-works">See how it works</a></div>
          </div>

          <aside className="relative rounded-[2rem] bg-ink p-5 text-white shadow-[0_32px_100px_rgba(16,36,29,0.24)] sm:p-7">
            <div className="flex items-center justify-between"><div><p className="text-xs font-bold uppercase tracking-[0.18em] text-accent">Analysis ready</p><h2 className="mt-2 text-lg font-semibold">Managed Security Services RFP</h2></div><span className="grid size-11 place-items-center rounded-2xl bg-white/7 text-accent"><ShieldCheck size={21} /></span></div>
            <div className="mt-7 grid grid-cols-3 gap-2">{[["84", "Requirements"], ["7", "Key dates"], ["35%", "Top weighting"]].map(([value, label]) => <div className="rounded-2xl bg-white/6 p-4" key={label}><p className="text-2xl font-semibold">{value}</p><p className="mt-1 text-[10px] font-bold uppercase tracking-wide text-white/40">{label}</p></div>)}</div>
            <div className="mt-4 rounded-2xl bg-white p-5 text-ink"><div className="flex gap-3"><span className="grid size-9 shrink-0 place-items-center rounded-xl bg-surface-muted text-brand"><Quote size={16} /></span><div><p className="text-xs font-bold uppercase tracking-wide text-brand">Verified source</p><p className="mt-2 text-sm leading-6">The supplier must maintain ISO 27001 certification throughout the contract term.</p><p className="mt-3 text-xs font-semibold text-muted">RFP.pdf · Page 14 · Security requirements</p></div></div></div>
            <div className="absolute -bottom-5 -left-5 flex items-center gap-2 rounded-xl bg-accent px-4 py-3 text-xs font-bold text-ink shadow-lg"><CheckCircle2 size={16} /> Quality reviewed</div>
          </aside>
        </div>
      </section>

      <section className="border-y border-ink/8 bg-white" id="how-it-works">
        <div className="mx-auto max-w-7xl px-5 py-20 sm:px-8 sm:py-24">
          <div className="max-w-2xl"><p className="eyebrow">One focused product</p><h2 className="mt-4 text-4xl font-semibold tracking-[-0.04em]">The important facts, without pretending to make your decision.</h2><p className="mt-5 leading-7 text-muted">Upload the documents, wait for quality review, then explore a concise result with exact citations.</p></div>
          <div className="mt-10 grid gap-4 lg:grid-cols-3">{outputs.map((output) => { const Icon = output.icon; return <article className="panel p-6 sm:p-7" key={output.title}><span className="grid size-11 place-items-center rounded-2xl bg-surface-muted text-brand"><Icon size={20} /></span><h3 className="mt-6 text-lg font-semibold">{output.title}</h3><p className="mt-2 text-sm leading-6 text-muted">{output.text}</p></article>; })}</div>
        </div>
      </section>

      <footer className="mx-auto flex max-w-7xl flex-col gap-3 px-5 py-8 text-xs text-muted sm:flex-row sm:items-center sm:justify-between sm:px-8"><p>© 2026 BidMatrix</p><p>Clear facts. Exact sources. Your decision.</p></footer>
    </main>
  );
}
