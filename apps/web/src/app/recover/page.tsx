import type { Metadata } from "next";
import Link from "next/link";
import { AccountRecovery } from "@/components/account-recovery";

export const metadata: Metadata = { title: "Account recovery" };

export default function RecoverPage() {
  return (
    <main className="min-h-screen bg-background px-5 sm:px-8">
      <div className="mx-auto flex min-h-screen w-full max-w-7xl flex-col">
        <Link className="mt-5 flex w-fit items-center gap-3 font-semibold sm:mt-8" href="/">
          <span className="grid size-10 place-items-center rounded-xl bg-ink text-sm font-black text-accent">BM</span>
          BidMatrix
        </Link>
        <div className="flex flex-1 items-center justify-center py-10 sm:py-12">
          <AccountRecovery />
        </div>
      </div>
    </main>
  );
}
