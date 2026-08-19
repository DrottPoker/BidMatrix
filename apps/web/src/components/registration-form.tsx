"use client";

import { FormEvent, useState } from "react";
import Link from "next/link";
import { CheckCircle2, LoaderCircle, UserPlus } from "lucide-react";
import { MINIMUM_PASSWORD_LENGTH } from "@/lib/auth-contract";
import { authClient, betterAuthErrorMessage } from "@/lib/auth-client";

export function RegistrationForm() {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [submitting, setSubmitting] = useState(false);
  const [verificationSent, setVerificationSent] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submitPassword(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    if (password !== confirmation) {
      setError("The password confirmation does not match.");
      return;
    }

    setSubmitting(true);
    try {
      const result = await authClient.signUp.email({
        name,
        email,
        password,
        callbackURL: "/email-verified",
      });
      if (result.error) throw result.error;
      setVerificationSent(true);
    } catch (submissionError) {
      setError(betterAuthErrorMessage(submissionError));
    } finally {
      setSubmitting(false);
    }
  }

  if (verificationSent) {
    return (
      <div className="mt-8 space-y-5">
        <div
          className="rounded-2xl border border-emerald-200 bg-emerald-50 p-5 text-emerald-950"
          role="status"
        >
          <CheckCircle2 aria-hidden="true" size={22} />
          <h3 className="mt-3 font-semibold">Check your email</h3>
          <p className="mt-2 text-sm leading-6">
            If the address can be registered, we sent a verification link to it.
            Open the link before signing in.
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
      <form className="space-y-5" onSubmit={submitPassword}>
        <label className="block text-sm font-semibold">
          Name
          <input
            className="field mt-2 h-12 font-normal"
            autoComplete="name"
            maxLength={120}
            minLength={2}
            required
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        </label>
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
        <label className="block text-sm font-semibold">
          Password
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
          Confirm password
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
            <UserPlus aria-hidden="true" size={17} />
          )}
          Create account
        </button>
      </form>

      {error ? (
        <p className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-800" role="alert">
          {error}
        </p>
      ) : null}

      <p className="text-center text-sm text-muted">
        Already have an account?{" "}
        <Link className="font-semibold text-brand hover:underline" href="/login">
          Sign in
        </Link>
      </p>
    </div>
  );
}
