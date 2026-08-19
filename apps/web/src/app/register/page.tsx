import type { Metadata } from "next";
import Link from "next/link";
import { FileSearch, UserPlus } from "lucide-react";
import { RegistrationForm } from "@/components/registration-form";

export const metadata: Metadata = { title: "Create account" };

export default function RegisterPage() {
  return (
    <main className="grid min-h-screen bg-ink lg:grid-cols-[1.05fr_0.95fr]">
      <section className="relative hidden overflow-hidden p-12 text-white lg:flex lg:flex-col lg:justify-between xl:p-16">
        <div className="absolute -left-20 top-1/3 size-96 rounded-full bg-brand/25 blur-3xl" />
        <Link className="relative flex items-center gap-3 font-semibold" href="/">
          <span className="grid size-10 place-items-center rounded-xl bg-accent text-sm font-black text-ink">BM</span>
          BidMatrix
        </Link>
        <div className="relative max-w-xl">
          <p className="flex items-center gap-2 text-xs font-bold uppercase tracking-[0.2em] text-accent">
            <FileSearch size={15} /> RFP intelligence
          </p>
          <h1 className="mt-6 text-5xl font-semibold leading-[1.04] tracking-[-0.045em]">
            Create your workspace and start organizing procurements.
          </h1>
          <p className="mt-6 max-w-lg text-base leading-8 text-white/60">
            Your account gets a private BidMatrix workspace. You remain in control of every upload, review, and publication.
          </p>
        </div>
        <p className="relative text-xs text-white/35">BidMatrix · Human-controlled by design</p>
      </section>
      <section className="grid place-items-center bg-background px-5 py-12 sm:px-8">
        <div className="w-full max-w-md">
          <Link className="mb-10 flex items-center gap-3 font-semibold lg:hidden" href="/">
            <span className="grid size-10 place-items-center rounded-xl bg-ink text-sm font-black text-accent">BM</span>
            BidMatrix
          </Link>
          <span className="grid size-11 place-items-center rounded-2xl bg-white text-brand shadow-sm">
            <UserPlus size={20} />
          </span>
          <h2 className="mt-6 text-3xl font-semibold tracking-[-0.035em]">Create account</h2>
          <p className="mt-3 text-sm leading-6 text-muted">
            Register with email and password. You will verify your address before
            the first sign-in.
          </p>
          <RegistrationForm />
        </div>
      </section>
    </main>
  );
}
