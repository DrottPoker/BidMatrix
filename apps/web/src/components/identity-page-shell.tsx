import type { ReactNode } from "react";
import Link from "next/link";
import { ShieldCheck } from "lucide-react";

type IdentityPageShellProps = {
  children: ReactNode;
  description: string;
  title: string;
};

export function IdentityPageShell({
  children,
  description,
  title,
}: IdentityPageShellProps) {
  return (
    <main className="grid min-h-screen place-items-center bg-background px-5 py-12 sm:px-8">
      <section className="w-full max-w-md">
        <Link className="mb-10 flex items-center gap-3 font-semibold" href="/">
          <span className="grid size-10 place-items-center rounded-xl bg-ink text-sm font-black text-accent">
            BM
          </span>
          BidMatrix
        </Link>
        <span className="grid size-11 place-items-center rounded-2xl bg-white text-brand shadow-sm">
          <ShieldCheck size={20} />
        </span>
        <h1 className="mt-6 text-3xl font-semibold tracking-[-0.035em]">{title}</h1>
        <p className="mt-3 text-sm leading-6 text-muted">{description}</p>
        {children}
      </section>
    </main>
  );
}
