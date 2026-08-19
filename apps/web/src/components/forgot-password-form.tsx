"use client";

import { FormEvent, useState } from "react";
import Link from "next/link";
import { CheckCircle2, LoaderCircle, Mail } from "lucide-react";
import { authClient, betterAuthErrorMessage } from "@/lib/auth-client";

export function ForgotPasswordForm() {
  const [email, setEmail] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [requested, setRequested] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      const result = await authClient.requestPasswordReset({
        email,
        redirectTo: `${window.location.origin}/reset-password/continue`,
      });
      if (result.error) throw result.error;
      setRequested(true);
    } catch (submissionError) {
      setError(betterAuthErrorMessage(submissionError));
    } finally {
      setSubmitting(false);
    }
  }

  if (requested) {
    return (
      <div className="mt-8 space-y-5">
        <div
          className="rounded-2xl border border-emerald-200 bg-emerald-50 p-5 text-emerald-950"
          role="status"
        >
          <CheckCircle2 aria-hidden="true" size={22} />
          <h3 className="mt-3 font-semibold">Check your email</h3>
          <p className="mt-2 text-sm leading-6">
            If an account exists for that address, we sent a single-use reset link.
          </p>
        </div>
        <Link className="button-secondary h-12 w-full" href="/login">
          Return to sign in
        </Link>
      </div>
    );
  }

  return (
    <div className="mt-8 space-y-5">
      <form className="space-y-5" onSubmit={submit}>
        <label className="block text-sm font-semibold">
          Email
          <input
            className="field mt-2 h-12 font-normal"
            type="email"
            autoComplete="email"
            required
            value={email}
            onChange={(event) => setEmail(event.target.value)}
          />
        </label>
        <button
          className="button-primary h-12 w-full"
          disabled={submitting}
          type="submit"
        >
          {submitting ? (
            <LoaderCircle className="animate-spin" aria-hidden="true" size={17} />
          ) : (
            <Mail aria-hidden="true" size={17} />
          )}
          Send reset link
        </button>
      </form>

      {error ? (
        <p className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-800" role="alert">
          {error}
        </p>
      ) : null}

      <p className="text-center text-sm text-muted">
        Remembered it?{" "}
        <Link className="font-semibold text-brand hover:underline" href="/login">
          Sign in
        </Link>
      </p>
    </div>
  );
}
