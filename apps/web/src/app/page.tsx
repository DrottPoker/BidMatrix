import {
  ArrowDownRight,
  ArrowRight,
  Blocks,
  Box,
  Braces,
  CheckCircle2,
  CloudCog,
  Database,
  FileLock2,
  Fingerprint,
  Network,
  ShieldCheck,
  Workflow,
} from "lucide-react";
import Link from "next/link";
import { PhaseCard } from "@/components/phase-card";
import { foundationMetrics, foundationPhases } from "@/data/foundation";

const principles = [
  {
    title: "Sourced, not invented",
    description:
      "F1 requirements point back to exact files and pages. Missing digital text is reported instead of fabricated.",
    icon: FileLock2,
  },
  {
    title: "Policy before action",
    description:
      "Models can propose work. Deterministic application code decides whether an action is allowed.",
    icon: ShieldCheck,
  },
  {
    title: "Human accountability",
    description:
      "Material actions stay payload-bound, reviewable, and owner-approved with a durable audit trail.",
    icon: Fingerprint,
  },
];

const services = [
  { name: "Web shell", runtime: "Next.js 16 + Tailwind 4", state: "Installed" },
  { name: "API boundary", runtime: ".NET 10", state: "Verified" },
  { name: "Agent worker", runtime: "Python 3.14", state: "Verified" },
  { name: "Local stack", runtime: "Docker Compose", state: "Healthy" },
];

const architectureNodes = [
  { label: "Customer and owner UI", detail: "Next.js", icon: Braces },
  { label: "Authoritative boundary", detail: "ASP.NET Core", icon: Network },
  { label: "Durable coordination", detail: "Temporal + Python", icon: Workflow },
  { label: "Private data plane", detail: "Postgres + S3", icon: Database },
];

export default function Home() {
  return (
    <main className="min-h-screen overflow-hidden bg-background">
      <header className="border-b bg-surface/90 backdrop-blur">
        <div className="mx-auto flex h-18 max-w-7xl items-center justify-between px-5 sm:px-8">
          <a className="flex items-center gap-3" href="#top" aria-label="BidMatrix home">
            <span className="grid size-9 place-items-center rounded-xl bg-brand text-sm font-bold text-white shadow-sm">
              BM
            </span>
            <span className="text-lg font-semibold tracking-tight">BidMatrix</span>
          </a>
          <nav className="hidden items-center gap-7 text-sm text-muted md:flex" aria-label="Primary navigation">
            <Link className="transition hover:text-foreground" href="/app/analyses">
              Analyses
            </Link>
            <a className="transition hover:text-foreground" href="#architecture">
              Architecture
            </a>
            <a className="transition hover:text-foreground" href="#roadmap">
              Roadmap
            </a>
            <a className="transition hover:text-foreground" href="#scope">
              F1 boundary
            </a>
          </nav>
          <span className="inline-flex items-center gap-2 rounded-full bg-zinc-100 px-3 py-1.5 text-xs font-semibold text-zinc-700 ring-1 ring-inset ring-zinc-200">
            <span className="size-1.5 rounded-full bg-amber-500" />
            F1 extraction prototype
          </span>
        </div>
      </header>

      <section id="top" className="relative isolate border-b">
        <div className="pointer-events-none absolute inset-0 -z-10 bg-[radial-gradient(circle_at_10%_0%,rgba(216,255,114,0.34),transparent_34%),radial-gradient(circle_at_90%_20%,rgba(11,107,87,0.12),transparent_32%)]" />
        <div className="mx-auto grid max-w-7xl items-center gap-14 px-5 py-20 sm:px-8 sm:py-28 lg:grid-cols-[1.08fr_0.92fr] lg:py-32">
          <div>
            <div className="inline-flex items-center gap-2 rounded-full bg-brand/8 px-3 py-1.5 text-xs font-bold uppercase tracking-[0.18em] text-brand ring-1 ring-inset ring-brand/15">
              <Blocks aria-hidden="true" size={14} />
              Release F1
            </div>
            <h1 className="mt-7 max-w-3xl text-5xl leading-[0.98] font-semibold tracking-[-0.045em] text-foreground sm:text-6xl lg:text-7xl">
              Trustworthy bid intelligence starts with control.
            </h1>
            <p className="mt-7 max-w-2xl text-lg leading-8 text-muted sm:text-xl">
              BidMatrix is becoming a sourced, reviewable workspace for RFP decision
              support. F1 extracts digital PDF text, classifies documents, and presents
              requirement candidates with page citations for mandatory human review.
            </p>
            <div className="mt-9 flex flex-col gap-3 sm:flex-row">
              <a
                href="#roadmap"
                className="inline-flex h-12 items-center justify-center gap-2 rounded-xl bg-brand px-5 text-sm font-semibold text-white shadow-[0_10px_28px_rgba(11,107,87,0.2)] transition hover:bg-brand-dark"
              >
                Review the build sequence
                <ArrowRight aria-hidden="true" size={17} />
              </a>
              <a
                href="#scope"
                className="inline-flex h-12 items-center justify-center gap-2 rounded-xl border bg-surface px-5 text-sm font-semibold text-foreground transition hover:border-brand/30 hover:bg-white"
              >
                See the F1 boundary
                <ArrowDownRight aria-hidden="true" size={17} />
              </a>
            </div>
          </div>

          <aside className="overflow-hidden rounded-[2rem] border border-white/10 bg-[#10201b] text-white shadow-[0_28px_80px_rgba(16,32,27,0.2)]" aria-label="Foundation system snapshot">
            <div className="flex items-center justify-between border-b border-white/10 px-6 py-5">
              <div>
                <p className="text-xs font-bold uppercase tracking-[0.18em] text-accent">
                  System snapshot
                </p>
                <h2 className="mt-1 text-lg font-semibold">F1 extraction runtime</h2>
              </div>
              <CloudCog aria-hidden="true" className="text-accent" size={28} />
            </div>
            <div className="space-y-1 p-3">
              {services.map((service) => (
                <div key={service.name} className="grid grid-cols-[1fr_auto] items-center gap-4 rounded-2xl px-4 py-4 transition hover:bg-white/5">
                  <div>
                    <p className="text-sm font-semibold">{service.name}</p>
                    <p className="mt-0.5 text-xs text-white/55">{service.runtime}</p>
                  </div>
                  <span className="inline-flex items-center gap-1.5 rounded-full bg-white/7 px-2.5 py-1 text-xs text-white/75 ring-1 ring-inset ring-white/10">
                    <span className="size-1.5 rounded-full bg-accent" />
                    {service.state}
                  </span>
                </div>
              ))}
            </div>
            <div className="border-t border-white/10 bg-white/3 px-6 py-4 text-xs leading-5 text-white/55">
              Status labels describe repository state, not live service health. Health is
              verified separately through the Compose gate.
            </div>
          </aside>
        </div>
      </section>

      <section className="border-b bg-surface" aria-label="Foundation metrics">
        <div className="mx-auto grid max-w-7xl divide-y px-5 sm:grid-cols-2 sm:divide-x sm:divide-y-0 sm:px-8 lg:grid-cols-4">
          {foundationMetrics.map((metric) => (
            <div key={metric.label} className="py-7 sm:px-6 first:pl-0 last:pr-0">
              <p className="text-xs font-bold uppercase tracking-[0.16em] text-muted">
                {metric.label}
              </p>
              <div className="mt-2 flex items-end gap-3">
                <strong className="text-3xl font-semibold tracking-tight text-foreground">
                  {metric.value}
                </strong>
                <span className="pb-1 text-xs text-muted">{metric.detail}</span>
              </div>
            </div>
          ))}
        </div>
      </section>

      <section id="architecture" className="mx-auto max-w-7xl px-5 py-20 sm:px-8 sm:py-28">
        <div className="grid gap-10 lg:grid-cols-[0.72fr_1.28fr] lg:gap-16">
          <div>
            <p className="text-xs font-bold uppercase tracking-[0.2em] text-brand">
              Operating principles
            </p>
            <h2 className="mt-4 text-4xl font-semibold tracking-[-0.035em] text-foreground">
              Confidence is earned at every boundary.
            </h2>
            <p className="mt-5 max-w-lg leading-7 text-muted">
              The foundation separates model reasoning from authoritative data and side
              effects. Every later capability inherits these constraints.
            </p>
          </div>
          <div className="grid gap-4 sm:grid-cols-3">
            {principles.map((principle) => {
              const Icon = principle.icon;

              return (
                <article key={principle.title} className="rounded-3xl border bg-surface p-6 shadow-[0_12px_35px_rgba(23,32,29,0.04)]">
                  <span className="grid size-10 place-items-center rounded-xl bg-brand/8 text-brand">
                    <Icon aria-hidden="true" size={20} />
                  </span>
                  <h3 className="mt-6 font-semibold tracking-tight">{principle.title}</h3>
                  <p className="mt-2 text-sm leading-6 text-muted">{principle.description}</p>
                </article>
              );
            })}
          </div>
        </div>

        <div className="mt-14 rounded-[2rem] border bg-[#edf3f0] p-4 sm:p-6 lg:p-8">
          <div className="mb-6 flex items-center justify-between gap-4">
            <div>
              <p className="text-xs font-bold uppercase tracking-[0.18em] text-brand">Runtime flow</p>
              <h3 className="mt-2 text-xl font-semibold">One controlled route through the system</h3>
            </div>
            <Box aria-hidden="true" className="hidden text-brand sm:block" size={26} />
          </div>
          <div className="grid gap-3 lg:grid-cols-4">
            {architectureNodes.map((node, index) => {
              const Icon = node.icon;

              return (
                <div key={node.label} className="relative rounded-2xl border bg-surface p-5">
                  <div className="flex items-center justify-between">
                    <span className="grid size-9 place-items-center rounded-lg bg-brand text-white">
                      <Icon aria-hidden="true" size={18} />
                    </span>
                    <span className="font-mono text-xs text-muted">0{index + 1}</span>
                  </div>
                  <p className="mt-5 text-sm font-semibold">{node.label}</p>
                  <p className="mt-1 text-xs text-muted">{node.detail}</p>
                </div>
              );
            })}
          </div>
        </div>
      </section>

      <section id="roadmap" className="border-y bg-[#e9efec]">
        <div className="mx-auto max-w-7xl px-5 py-20 sm:px-8 sm:py-28">
          <div className="flex flex-col justify-between gap-5 sm:flex-row sm:items-end">
            <div>
              <p className="text-xs font-bold uppercase tracking-[0.2em] text-brand">
                Controlled delivery
              </p>
              <h2 className="mt-4 max-w-2xl text-4xl font-semibold tracking-[-0.035em] text-foreground">
                Every phase unlocks the next one.
              </h2>
            </div>
            <p className="max-w-md text-sm leading-6 text-muted">
              Later business features remain locked until the infrastructure and security
              gates beneath them are verified.
            </p>
          </div>
          <div className="mt-10 grid gap-4 md:grid-cols-2 xl:grid-cols-4">
            {foundationPhases.map((phase) => (
              <PhaseCard key={phase.id} phase={phase} />
            ))}
          </div>
        </div>
      </section>

      <section id="scope" className="mx-auto max-w-7xl px-5 py-20 sm:px-8 sm:py-28">
        <div className="relative overflow-hidden rounded-[2rem] bg-brand-dark px-6 py-10 text-white sm:px-10 sm:py-12 lg:px-14">
          <div className="pointer-events-none absolute -top-24 -right-20 size-80 rounded-full border border-accent/20" />
          <div className="pointer-events-none absolute -top-10 -right-4 size-52 rounded-full border border-accent/20" />
          <div className="relative grid gap-10 lg:grid-cols-[1fr_0.78fr] lg:items-end">
            <div>
              <div className="flex items-center gap-2 text-xs font-bold uppercase tracking-[0.18em] text-accent">
                <CheckCircle2 aria-hidden="true" size={16} />
                Honest capability boundary
              </div>
              <h2 className="mt-5 max-w-3xl text-3xl font-semibold tracking-[-0.03em] sm:text-4xl">
                F1 adds sourced extraction without weakening the control plane.
              </h2>
              <p className="mt-5 max-w-2xl leading-7 text-white/68">
                Digital PDF text, page preservation, document classification, strict
                requirement records, and citations now run through the durable workflow.
                Every result remains pending human review.
              </p>
            </div>
            <div className="rounded-2xl bg-white/7 p-5 ring-1 ring-inset ring-white/10">
              <p className="text-xs font-bold uppercase tracking-[0.16em] text-white/50">
                Explicitly unavailable in F1
              </p>
              <p className="mt-3 text-sm leading-6 text-white/80">
                OCR, company matching, compliance scoring, bid/no-bid recommendations,
                outbound actions, billing, and production deployment.
              </p>
            </div>
          </div>
        </div>
      </section>

      <footer className="border-t bg-surface">
        <div className="mx-auto flex max-w-7xl flex-col gap-3 px-5 py-7 text-xs text-muted sm:flex-row sm:items-center sm:justify-between sm:px-8">
          <p>BidMatrix Extraction Prototype F1</p>
          <p>Local, sourced, controlled, and reviewable by design.</p>
        </div>
      </footer>
    </main>
  );
}
