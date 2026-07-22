import Link from "next/link";
import { AlertTriangle, LockKeyhole } from "lucide-react";

const navigation = [
  ["Dashboard", "/owner"], ["Tasks", "/owner/tasks"], ["Approvals", "/owner/approvals"],
  ["Agents", "/owner/agents"], ["Runs", "/owner/runs"], ["Audit", "/owner/audit"],
  ["Goals", "/owner/goals"], ["Analyses", "/owner/analyses"],
  ["Controls", "/owner/settings/system-controls"],
] as const;

export default function OwnerLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <div className="min-h-screen bg-background">
      <div className="border-b border-amber-300 bg-amber-50 px-5 py-2 text-center text-xs font-semibold text-amber-950">
        <span className="inline-flex items-center gap-2"><AlertTriangle size={14} />F2 concierge mode: every customer result requires owner quality review and explicit publication.</span>
      </div>
      <header className="border-b bg-[#10201b] text-white">
        <div className="mx-auto flex max-w-7xl flex-col justify-between gap-4 px-5 py-5 sm:px-8 lg:flex-row lg:items-center">
          <Link className="flex items-center gap-3 font-semibold" href="/owner"><span className="grid size-9 place-items-center rounded-xl bg-accent text-brand-dark">BM</span><span>BidMatrix OS</span><LockKeyhole className="text-accent" size={16} /></Link>
          <nav aria-label="Owner Console" className="flex flex-wrap gap-x-5 gap-y-3 text-xs text-white/70">{navigation.map(([label, href]) => <Link className="transition hover:text-white" href={href} key={href}>{label}</Link>)}</nav>
        </div>
      </header>
      <main className="mx-auto max-w-7xl px-5 py-8 sm:px-8 sm:py-10">{children}</main>
    </div>
  );
}
