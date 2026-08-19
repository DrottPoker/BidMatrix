"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { CheckCircle2, LoaderCircle } from "lucide-react";

export function EmailVerificationResult() {
  const [state, setState] = useState<"loading" | "verified" | "invalid">("loading");

  useEffect(() => {
    const url = new URL(window.location.href);
    const hasError = url.searchParams.has("error");
    window.history.replaceState(null, "", url.pathname);
    const timer = window.setTimeout(
      () => setState(hasError ? "invalid" : "verified"),
      0,
    );
    return () => window.clearTimeout(timer);
  }, []);

  if (state === "loading") {
    return (
      <div className="mt-8 flex items-center gap-2 text-sm text-muted" role="status">
        <LoaderCircle className="animate-spin" aria-hidden="true" size={17} />
        Confirming email
      </div>
    );
  }

  if (state === "invalid") {
    return (
      <div className="mt-8 space-y-5">
        <p className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-800" role="alert">
          This verification link is invalid or has expired. Sign in to request a new one.
        </p>
        <Link className="button-primary h-12 w-full" href="/login">
          Return to sign in
        </Link>
      </div>
    );
  }

  return (
    <div className="mt-8 space-y-5">
      <div
        className="rounded-2xl border border-emerald-200 bg-emerald-50 p-5 text-emerald-950"
        role="status"
      >
        <CheckCircle2 aria-hidden="true" size={22} />
        <h3 className="mt-3 font-semibold">Email verified</h3>
        <p className="mt-2 text-sm leading-6">
          Your account is active. You can now sign in and open your private workspace.
        </p>
      </div>
      <Link className="button-primary h-12 w-full" href="/login">
        Sign in
      </Link>
    </div>
  );
}
