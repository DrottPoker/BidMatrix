"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowRight, LoaderCircle, LockKeyhole } from "lucide-react";
import { apiMutation } from "@/lib/bidmatrix-api";

type CurrentUser = {
  userId: string;
  email: string;
};

export function LoginForm() {
  const router = useRouter();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);

    try {
      await apiMutation<CurrentUser>("/v1/auth/login", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      });
      router.push("/app");
      router.refresh();
    } catch (submissionError) {
      setError(
        submissionError instanceof Error
          ? submissionError.message
          : "Authentication failed.",
      );
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <form className="mt-8 space-y-5" onSubmit={submit}>
      <label className="block text-sm font-semibold">
        Email
        <input
          className="field mt-2 h-12 font-normal"
          type="email"
          autoComplete="username"
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
          autoComplete="current-password"
          required
          value={password}
          onChange={(event) => setPassword(event.target.value)}
        />
      </label>
      {error ? (
        <p className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-800" role="alert">
          {error}
        </p>
      ) : null}
      <button
        className="button-primary h-12 w-full"
        disabled={submitting}
        type="submit"
      >
        {submitting ? (
          <LoaderCircle className="animate-spin" aria-hidden="true" size={17} />
        ) : (
          <LockKeyhole aria-hidden="true" size={17} />
        )}
        Sign in
        <ArrowRight aria-hidden="true" size={17} />
      </button>
    </form>
  );
}
