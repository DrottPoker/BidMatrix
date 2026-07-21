import type { Metadata } from "next";
import Link from "next/link";
import { LoginForm } from "@/components/login-form";

export const metadata: Metadata = { title: "Sign in | BidMatrix" };

export default function LoginPage() {
  return (
    <main className="grid min-h-screen place-items-center bg-background px-5 py-12">
      <section className="w-full max-w-md rounded-3xl border bg-white p-7 shadow-xl sm:p-9">
        <Link className="text-sm font-semibold text-brand" href="/">BidMatrix</Link>
        <h1 className="mt-5 text-3xl font-semibold tracking-tight">Sign in</h1>
        <p className="mt-3 text-sm leading-6 text-muted">
          Development accounts are bootstrapped from environment configuration. Credentials are never embedded in the browser bundle.
        </p>
        <LoginForm />
      </section>
    </main>
  );
}
