import Link from "next/link";

export default function CustomerAppLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <div className="min-h-screen bg-background">
      <header className="border-b bg-white">
        <div className="mx-auto flex h-16 max-w-6xl items-center justify-between px-5 sm:px-8">
          <Link className="flex items-center gap-3 font-semibold" href="/app"><span className="grid size-9 place-items-center rounded-xl bg-brand text-sm text-white">BM</span>BidMatrix</Link>
          <nav aria-label="Customer workspace" className="flex items-center gap-5 text-sm text-muted">
            <Link className="hover:text-foreground" href="/app/analyses">Analyses</Link>
            <Link className="rounded-xl border px-3 py-2 font-semibold text-foreground" href="/login">Sign in</Link>
          </nav>
        </div>
      </header>
      {children}
    </div>
  );
}
