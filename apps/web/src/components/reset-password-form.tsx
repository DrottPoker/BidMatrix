"use client";

import { FormEvent, useEffect, useState } from "react";
import Link from "next/link";
import { CheckCircle2, KeyRound, LoaderCircle } from "lucide-react";
import { MINIMUM_PASSWORD_LENGTH } from "@/lib/auth-contract";
import { authClient, betterAuthErrorMessage } from "@/lib/auth-client";

type ResetState =
  | { kind: "loading" }
  | { kind: "invalid" }
  | { kind: "ready"; token: string }
  | { kind: "complete" };

export function ResetPasswordForm() {
  const [resetState, setResetState] = useState<ResetState>({ kind: "loading" });
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const url = new URL(window.location.href);
    const fragment = new URLSearchParams(url.hash.slice(1));
    const token = fragment.get("token");
    const callbackError = fragment.get("error");
    window.history.replaceState(null, "", url.pathname);
    const timer = window.setTimeout(
      () => setResetState(
        token && !callbackError ? { kind: "ready", token } : { kind: "invalid" },
      ),
      0,
    );
    return () => window.clearTimeout(timer);
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (resetState.kind !== "ready") return;
    setError(null);
    if (password !== confirmation) {
      setError("The password confirmation does not match.");
      return;
    }

    setSubmitting(true);
    try {
      const result = await authClient.resetPassword({
        newPassword: password,
        token: resetState.token,
      });
      if (result.error) throw result.error;
      setResetState({ kind: "complete" });
    } catch (submissionError) {
      setError(betterAuthErrorMessage(submissionError));
    } finally {
      setSubmitting(false);
    }
  }

  if (resetState.kind === "loading") {
    return (
      <div className="mt-8 flex items-center gap-2 text-sm text-muted" role="status">
        <LoaderCircle className="animate-spin" aria-hidden="true" size={17} />
        Checking secure link
      </div>
    );
  }

  if (resetState.kind === "invalid") {
    return (
      <div className="mt-8 space-y-5">
        <p className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-800" role="alert">
          This reset link is invalid or has expired.
        </p>
        <Link className="button-primary h-12 w-full" href="/forgot-password">
          Request a new link
        </Link>
      </div>
    );
  }

  if (resetState.kind === "complete") {
    return (
      <div className="mt-8 space-y-5">
        <div
          className="rounded-2xl border border-emerald-200 bg-emerald-50 p-5 text-emerald-950"
          role="status"
        >
          <CheckCircle2 aria-hidden="true" size={22} />
          <h3 className="mt-3 font-semibold">Password updated</h3>
          <p className="mt-2 text-sm leading-6">
            Existing sessions were revoked. Sign in again with your new password.
          </p>
        </div>
        <Link className="button-primary h-12 w-full" href="/login">
          Sign in
        </Link>
      </div>
    );
  }

  return (
    <div className="mt-8 space-y-5">
      <form className="space-y-5" onSubmit={submit}>
        <label className="block text-sm font-semibold">
          New password
          <input
            className="field mt-2 h-12 font-normal"
            type="password"
            autoComplete="new-password"
            minLength={MINIMUM_PASSWORD_LENGTH}
            maxLength={128}
            required
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </label>
        <label className="block text-sm font-semibold">
          Confirm new password
          <input
            className="field mt-2 h-12 font-normal"
            type="password"
            autoComplete="new-password"
            minLength={MINIMUM_PASSWORD_LENGTH}
            maxLength={128}
            required
            value={confirmation}
            onChange={(event) => setConfirmation(event.target.value)}
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
            <KeyRound aria-hidden="true" size={17} />
          )}
          Update password
        </button>
      </form>

      {error ? (
        <p className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-800" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}
