"use client";

import { FormEvent, useEffect, useRef, useState } from "react";
import Link from "next/link";
import {
  CheckCircle2,
  KeyRound,
  LoaderCircle,
  ShieldX,
} from "lucide-react";
import {
  AccountRecoveryInspection,
  AuthenticationConfiguration,
  ApiError,
  apiGet,
  apiMutation,
  formatApiError,
} from "@/lib/bidmatrix-api";
import { MINIMUM_PASSWORD_LENGTH } from "@/lib/auth-contract";

type RecoveryState = "loading" | "ready" | "missing" | "managed" | "unavailable" | "invalid" | "complete";

export function AccountRecovery() {
  const token = useRef<string | null>(null);
  const [inspection, setInspection] = useState<AccountRecoveryInspection | null>(null);
  const [newPassword, setNewPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [state, setState] = useState<RecoveryState>("loading");
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (token.current === null) {
      const fragment = new URLSearchParams(window.location.hash.slice(1));
      token.current = fragment.get("token") ?? "";
      window.history.replaceState(null, "", `${window.location.pathname}${window.location.search}`);
    }

    const recoveryToken = token.current;
    if (!recoveryToken) {
      setState("missing");
      return;
    }

    let cancelled = false;
    void (async () => {
      try {
        const configuration = await apiGet<AuthenticationConfiguration>(
          "/v1/auth/configuration",
        );
        if (cancelled) return;
        if (!configuration.nativeRecoveryEnabled) {
          setState("managed");
          return;
        }

        const result = await apiMutation<AccountRecoveryInspection>(
          "/v1/auth/recovery/inspect",
          {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ token: recoveryToken }),
          },
        );
        if (!cancelled) {
          setInspection(result);
          setState("ready");
        }
      } catch (requestError) {
        if (!cancelled) {
          setState(requestError instanceof ApiError && requestError.status === 410 ? "unavailable" : "invalid");
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);

    if (newPassword !== confirmation) {
      setError("The password confirmation does not match.");
      return;
    }

    if (!token.current) {
      setState("invalid");
      return;
    }

    setSubmitting(true);
    try {
      await apiMutation<{ status: string }>("/v1/auth/recovery/reset", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ token: token.current, newPassword }),
      });
      token.current = null;
      setState("complete");
    } catch (requestError) {
      if (requestError instanceof ApiError && requestError.status === 410) {
        setState("unavailable");
      } else {
        setError(formatApiError(requestError));
      }
    } finally {
      setSubmitting(false);
    }
  }

  if (state === "loading") {
    return (
      <StatusPanel
        icon={<LoaderCircle className="animate-spin" size={24} aria-hidden="true" />}
        title="Checking recovery link"
        detail="BidMatrix is validating this one-time link."
      />
    );
  }

  if (state === "missing") {
    return (
      <StatusPanel
        icon={<KeyRound size={24} aria-hidden="true" />}
        title="Use your recovery link"
        detail="Open the complete link supplied by BidMatrix support. Recovery links are short lived and can be used once."
      />
    );
  }

  if (state === "managed") {
    return (
      <StatusPanel
        icon={<KeyRound size={24} aria-hidden="true" />}
        title="Use managed identity"
        detail="Password recovery is disabled. Sign in through the managed identity provider or contact BidMatrix support if access is blocked."
      />
    );
  }

  if (state === "invalid") {
    return (
      <StatusPanel
        icon={<ShieldX size={24} aria-hidden="true" />}
        title="Recovery link not found"
        detail="The link is invalid. Ask BidMatrix support to verify your account and issue a new link."
      />
    );
  }

  if (state === "unavailable") {
    return (
      <StatusPanel
        icon={<ShieldX size={24} aria-hidden="true" />}
        title="Recovery link unavailable"
        detail="The link has expired, was revoked, or has already been used. Ask BidMatrix support for a new link."
      />
    );
  }

  if (state === "complete") {
    return (
      <div className="w-full max-w-md text-center">
        <span className="mx-auto grid size-12 place-items-center rounded-2xl bg-emerald-100 text-emerald-800">
          <CheckCircle2 size={24} aria-hidden="true" />
        </span>
        <h1 className="mt-5 text-2xl font-semibold tracking-[-0.03em]">Password reset complete</h1>
        <p className="mx-auto mt-3 max-w-sm text-sm leading-6 text-muted">
          Every previous BidMatrix session was revoked. Sign in again with your new password.
        </p>
        <Link className="button-primary mt-6 inline-flex" href="/login">Continue to sign in</Link>
      </div>
    );
  }

  if (!inspection) {
    return null;
  }

  return (
    <div className="w-full max-w-md">
      <span className="grid size-11 place-items-center rounded-2xl bg-accent text-ink">
        <KeyRound size={21} aria-hidden="true" />
      </span>
      <p className="eyebrow mt-6">Account recovery</p>
      <h1 className="mt-3 text-3xl font-semibold tracking-[-0.035em]">Choose a new password</h1>
      <p className="mt-3 text-sm leading-6 text-muted">
        Reset the password for <strong className="text-foreground">{inspection.email}</strong>. Every previous session will be closed.
      </p>
      <form className="mt-7 space-y-4" onSubmit={submit}>
        <div>
          <label className="block text-sm font-semibold" htmlFor="recovery-password">New password</label>
          <input
            id="recovery-password"
            aria-describedby="recovery-password-hint"
            className="field mt-2 h-12 font-normal"
            type="password"
            autoComplete="new-password"
            minLength={MINIMUM_PASSWORD_LENGTH}
            maxLength={128}
            required
            value={newPassword}
            onChange={(event) => setNewPassword(event.target.value)}
          />
          <p className="mt-1.5 text-xs text-muted" id="recovery-password-hint">Use at least 8 characters.</p>
        </div>
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
        {error ? <p className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-800" role="alert">{error}</p> : null}
        <button className="button-primary h-12 w-full" disabled={submitting} type="submit">
          {submitting ? <LoaderCircle className="animate-spin" size={17} aria-hidden="true" /> : <KeyRound size={17} aria-hidden="true" />}
          Reset password
        </button>
      </form>
      <p className="mt-5 text-center text-xs leading-5 text-muted">This link expires {formatDate(inspection.expiresAt)}.</p>
    </div>
  );
}

function StatusPanel({
  icon,
  title,
  detail,
}: {
  icon: React.ReactNode;
  title: string;
  detail: string;
}) {
  return (
    <div className="w-full max-w-md text-center">
      <span className="mx-auto grid size-12 place-items-center rounded-2xl bg-surface-muted text-foreground">{icon}</span>
      <h1 className="mt-5 text-2xl font-semibold tracking-[-0.03em]">{title}</h1>
      <p className="mx-auto mt-3 max-w-sm text-sm leading-6 text-muted">{detail}</p>
      <Link className="mt-5 inline-block text-sm font-semibold text-brand hover:underline" href="/login">Back to sign in</Link>
    </div>
  );
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}
