"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";
import {
  Check,
  Clipboard,
  KeyRound,
  LifeBuoy,
  LoaderCircle,
  RotateCcw,
  ShieldAlert,
} from "lucide-react";
import {
  AccountRecovery,
  AccountRecoveryList,
  CreatedAccountRecovery,
  apiGet,
  apiMutation,
  formatApiError,
} from "@/lib/bidmatrix-api";

export function AccountRecoveryLinks() {
  const [recoveries, setRecoveries] = useState<AccountRecovery[]>([]);
  const [email, setEmail] = useState("");
  const [created, setCreated] = useState<CreatedAccountRecovery | null>(null);
  const [copied, setCopied] = useState(false);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      const response = await apiGet<AccountRecoveryList>(
        "/owner/v1/account-recovery-links",
      );
      setRecoveries(response.recoveries);
      setError(null);
    } catch (requestError) {
      setError(formatApiError(requestError));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    let active = true;
    void apiGet<AccountRecoveryList>("/owner/v1/account-recovery-links")
      .then((response) => {
        if (active) {
          setRecoveries(response.recoveries);
          setError(null);
        }
      })
      .catch((requestError: unknown) => {
        if (active) {
          setError(formatApiError(requestError));
        }
      })
      .finally(() => {
        if (active) {
          setLoading(false);
        }
      });

    return () => {
      active = false;
    };
  }, []);

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    setCreated(null);
    setCopied(false);

    try {
      const response = await apiMutation<CreatedAccountRecovery>(
        "/owner/v1/account-recovery-links",
        {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ email }),
        },
      );
      setCreated(response);
      setEmail("");
      await load();
    } catch (requestError) {
      setError(formatApiError(requestError));
    } finally {
      setSubmitting(false);
    }
  }

  async function copyRecoveryLink() {
    if (!created) {
      return;
    }

    try {
      await navigator.clipboard.writeText(created.recoveryUrl);
      setCopied(true);
      setError(null);
    } catch {
      setError("The recovery link could not be copied. Select and copy it manually.");
    }
  }

  async function revoke(recovery: AccountRecovery) {
    setError(null);
    try {
      await apiMutation<AccountRecovery>(
        `/owner/v1/account-recovery-links/${recovery.id}/revoke`,
        { method: "POST" },
      );
      await load();
    } catch (requestError) {
      setError(formatApiError(requestError));
    }
  }

  return (
    <div className="space-y-7">
      <header className="max-w-3xl">
        <p className="eyebrow">S1.1 identity operations</p>
        <h1 className="mt-3 text-3xl font-semibold tracking-[-0.035em] sm:text-4xl">Account recovery</h1>
        <p className="mt-3 text-sm leading-7 text-muted">
          Issue a short-lived link after verifying the account owner outside BidMatrix. The link is shown once and must be delivered through a trusted channel.
        </p>
      </header>

      {error ? (
        <div className="rounded-2xl border border-red-200 bg-red-50 px-5 py-4 text-sm text-red-900" role="alert">
          {error}
        </div>
      ) : null}

      <div className="grid min-w-0 grid-cols-[minmax(0,1fr)] gap-7 xl:grid-cols-[minmax(0,0.78fr)_minmax(0,1.22fr)]">
        <section className="panel min-w-0 p-5 sm:p-7" aria-labelledby="create-recovery-heading">
          <div className="flex items-start gap-3">
            <span className="grid size-10 shrink-0 place-items-center rounded-xl bg-accent text-ink">
              <LifeBuoy size={19} aria-hidden="true" />
            </span>
            <div>
              <h2 className="text-lg font-semibold" id="create-recovery-heading">Create recovery link</h2>
              <p className="mt-1 text-sm leading-6 text-muted">Requires a recently authenticated platform owner.</p>
            </div>
          </div>
          <form className="mt-6 space-y-4" onSubmit={submit}>
            <label className="block text-sm font-semibold">
              Account email
              <input
                className="field mt-2 h-11 font-normal"
                type="email"
                autoComplete="off"
                maxLength={320}
                required
                value={email}
                onChange={(event) => setEmail(event.target.value)}
              />
            </label>
            <button className="button-primary w-full" disabled={submitting} type="submit">
              {submitting ? <LoaderCircle className="animate-spin" size={17} aria-hidden="true" /> : <KeyRound size={17} aria-hidden="true" />}
              Create recovery link
            </button>
          </form>
        </section>

        <section className="min-w-0 space-y-5" aria-labelledby="recovery-history-heading">
          {created ? (
            <div className="rounded-[1.4rem] border border-amber-300 bg-amber-50 p-5 sm:p-6">
              <div className="flex items-start gap-3">
                <ShieldAlert className="mt-0.5 shrink-0 text-amber-800" size={19} aria-hidden="true" />
                <div className="min-w-0 flex-1">
                  <h2 className="font-semibold text-amber-950">Copy this recovery link now</h2>
                  <p className="mt-1 text-sm leading-6 text-amber-900/80">
                    The token will not be shown again. Verify the recipient before delivery.
                  </p>
                  <div className="mt-4 flex flex-col gap-2 sm:flex-row">
                    <input
                      aria-label="One-time recovery link"
                      className="field h-11 min-w-0 flex-1 font-mono text-xs"
                      readOnly
                      value={created.recoveryUrl}
                      onFocus={(event) => event.currentTarget.select()}
                    />
                    <button className="button-secondary shrink-0" type="button" onClick={() => void copyRecoveryLink()}>
                      {copied ? <Check size={16} aria-hidden="true" /> : <Clipboard size={16} aria-hidden="true" />}
                      {copied ? "Copied" : "Copy link"}
                    </button>
                  </div>
                </div>
              </div>
            </div>
          ) : null}

          <div className="panel overflow-hidden">
            <div className="flex items-center justify-between gap-3 border-b px-5 py-4 sm:px-6">
              <div>
                <h2 className="font-semibold" id="recovery-history-heading">Recovery history</h2>
                <p className="mt-1 text-xs text-muted">Token values and hashes never appear here.</p>
              </div>
              <button className="button-secondary h-9 px-3" type="button" onClick={() => void load()}>
                <RotateCcw size={14} aria-hidden="true" />Refresh
              </button>
            </div>
            {loading ? (
              <div className="grid min-h-52 place-items-center text-sm text-muted">
                <LoaderCircle className="animate-spin" size={21} aria-label="Loading recovery links" />
              </div>
            ) : recoveries.length === 0 ? (
              <div className="grid min-h-52 place-items-center px-6 text-center">
                <div>
                  <KeyRound className="mx-auto text-muted" size={22} aria-hidden="true" />
                  <p className="mt-3 text-sm font-semibold">No recovery links yet</p>
                  <p className="mt-1 text-xs text-muted">Issued links will appear here without secret material.</p>
                </div>
              </div>
            ) : (
              <ul className="divide-y">
                {recoveries.map((recovery) => (
                  <li className="flex flex-col gap-4 px-5 py-5 sm:flex-row sm:items-center sm:justify-between sm:px-6" key={recovery.id}>
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="font-semibold">{recovery.displayName ?? recovery.email}</p>
                        <RecoveryStatus status={recovery.status} />
                      </div>
                      <p className="mt-1 truncate text-sm text-muted">{recovery.email}</p>
                      <p className="mt-2 text-xs text-muted">Expires {formatDate(recovery.expiresAt)}</p>
                    </div>
                    {recovery.status === "pending" ? (
                      <button className="button-secondary h-9 shrink-0 px-3 text-red-800" type="button" onClick={() => void revoke(recovery)}>
                        Revoke
                      </button>
                    ) : null}
                  </li>
                ))}
              </ul>
            )}
          </div>
        </section>
      </div>
    </div>
  );
}

function RecoveryStatus({ status }: { status: AccountRecovery["status"] }) {
  const style = status === "used"
    ? "bg-emerald-100 text-emerald-900"
    : status === "pending"
      ? "bg-amber-100 text-amber-900"
      : "bg-stone-200 text-stone-700";
  return <span className={`rounded-full px-2.5 py-1 text-[0.68rem] font-bold uppercase tracking-wide ${style}`}>{status}</span>;
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}
