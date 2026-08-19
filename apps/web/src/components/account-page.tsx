"use client";

import { FormEvent, useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import {
  Building2,
  Fingerprint,
  KeyRound,
  LoaderCircle,
  LogOut,
  Mail,
  MonitorSmartphone,
  RefreshCcw,
  ShieldCheck,
  UserRound,
} from "lucide-react";
import {
  CurrentUser,
  AuthenticationConfiguration,
  FederatedIdentity,
  FederatedIdentityList,
  RevokedFederatedIdentity,
  UserSession,
  UserSessionList,
  apiBaseUrl,
  apiGet,
  apiMutation,
  formatApiError,
} from "@/lib/bidmatrix-api";
import { MINIMUM_PASSWORD_LENGTH } from "@/lib/auth-contract";

export function AccountPage() {
  const router = useRouter();
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [configuration, setConfiguration] =
    useState<AuthenticationConfiguration | null>(null);
  const [identities, setIdentities] = useState<FederatedIdentity[]>([]);
  const [nativePasswordEnabled, setNativePasswordEnabled] = useState(false);
  const [sessions, setSessions] = useState<UserSession[]>([]);
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmation, setConfirmation] = useState("");
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [sessionAction, setSessionAction] = useState<string | null>(null);
  const [identityAction, setIdentityAction] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);

  const loadSessions = useCallback(async () => {
    const response = await apiGet<UserSessionList>("/v1/auth/sessions");
    setSessions(response.sessions);
  }, []);

  useEffect(() => {
    const identityError = new URLSearchParams(window.location.search).get(
      "identityError",
    );
    if (identityError) {
      const url = new URL(window.location.href);
      url.searchParams.delete("identityError");
      window.history.replaceState(null, "", `${url.pathname}${url.search}${url.hash}`);
    }

    let active = true;
    void Promise.all([
      apiGet<CurrentUser>("/v1/me"),
      apiGet<UserSessionList>("/v1/auth/sessions"),
      apiGet<AuthenticationConfiguration>("/v1/auth/configuration"),
      apiGet<FederatedIdentityList>("/v1/auth/federated-identities"),
    ])
      .then(([currentUser, sessionList, authenticationConfiguration, identityList]) => {
        if (active) {
          setUser(currentUser);
          setSessions(sessionList.sessions);
          setConfiguration(authenticationConfiguration);
          setIdentities(identityList.identities);
          setNativePasswordEnabled(identityList.nativePasswordEnabled);
          setError(identityError ? identityErrorMessage(identityError) : null);
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

  async function changePassword(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setMessage(null);

    if (newPassword !== confirmation) {
      setError("The password confirmation does not match.");
      return;
    }

    setSubmitting(true);
    try {
      await apiMutation<void>("/v1/auth/password", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ currentPassword, newPassword }),
      });
      router.replace("/login");
      router.refresh();
    } catch (requestError) {
      setError(formatApiError(requestError));
    } finally {
      setSubmitting(false);
    }
  }

  async function revokeSession(session: UserSession) {
    setSessionAction(session.id);
    setError(null);
    setMessage(null);
    try {
      await apiMutation<void>(`/v1/auth/sessions/${session.id}/revoke`, {
        method: "POST",
      });
      if (session.isCurrent) {
        router.replace("/login");
        router.refresh();
        return;
      }

      await loadSessions();
      setMessage("Session revoked.");
    } catch (requestError) {
      setError(formatApiError(requestError));
    } finally {
      setSessionAction(null);
    }
  }

  async function revokeOtherSessions() {
    setSessionAction("others");
    setError(null);
    setMessage(null);
    try {
      const response = await apiMutation<{ revokedSessionCount: number }>(
        "/v1/auth/sessions/revoke-others",
        { method: "POST" },
      );
      await loadSessions();
      setMessage(
        response.revokedSessionCount === 1
          ? "One other session was revoked."
          : `${response.revokedSessionCount} other sessions were revoked.`,
      );
    } catch (requestError) {
      setError(formatApiError(requestError));
    } finally {
      setSessionAction(null);
    }
  }

  async function revokeIdentity(identity: FederatedIdentity) {
    setIdentityAction(identity.id);
    setError(null);
    setMessage(null);
    try {
      await apiMutation<RevokedFederatedIdentity>(
        `/v1/auth/federated-identities/${identity.id}/revoke`,
        { method: "POST" },
      );
      router.replace("/login");
      router.refresh();
    } catch (requestError) {
      setError(formatApiError(requestError));
    } finally {
      setIdentityAction(null);
    }
  }

  const activeIdentityCount = identities.filter(
    (identity) => identity.status === "active",
  ).length;
  const nativeAuthenticationAvailable =
    configuration?.nativeLoginEnabled === true && nativePasswordEnabled;
  const canRevokeActiveIdentity =
    activeIdentityCount > 1 || nativeAuthenticationAvailable;

  return (
    <div className="page-shell space-y-7">
      <header>
        <p className="eyebrow">Settings</p>
        <h1 className="mt-2 text-3xl font-semibold tracking-[-0.035em] sm:text-4xl">Account security</h1>
        <p className="mt-3 text-sm leading-6 text-muted">
          Review your sign-in methods and close sessions you no longer use.
        </p>
      </header>

      {error ? (
        <div className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-900" role="alert">
          {error}
        </div>
      ) : null}
      {message ? (
        <div className="rounded-2xl border border-emerald-200 bg-emerald-50 p-4 text-sm text-emerald-900" role="status">
          {message}
        </div>
      ) : null}

      {loading ? (
        <div className="panel grid min-h-56 place-items-center">
          <LoaderCircle className="animate-spin text-brand" aria-label="Loading account" />
        </div>
      ) : !user ? (
        <div className="panel p-6 text-sm text-muted">
          Account security could not be loaded. Sign in again and retry.
        </div>
      ) : (
        <>
          <div className="grid gap-5 xl:grid-cols-[1fr_0.7fr]">
            <section className="panel p-6 sm:p-8">
              <div className="flex items-center gap-4">
                <span className="grid size-14 place-items-center rounded-2xl bg-ink text-accent">
                  <UserRound size={24} aria-hidden="true" />
                </span>
                <div>
                  <h2 className="text-xl font-semibold">{user.displayName ?? "BidMatrix user"}</h2>
                  <p className="mt-1 text-sm text-muted">Authenticated customer account</p>
                </div>
              </div>
              <dl className="mt-8 divide-y divide-ink/7 border-y border-ink/7">
                <Row icon={<Mail size={17} />} label="Email" value={user.email} />
                <Row
                  icon={<Building2 size={17} />}
                  label="Organization role"
                  value={user.organizations[0]?.role ?? "Not assigned"}
                />
                <Row
                  icon={<ShieldCheck size={17} />}
                  label="Session protection"
                  value="Server validated"
                />
              </dl>
            </section>
            <section className="rounded-[1.4rem] bg-ink p-6 text-white sm:p-8">
              <ShieldCheck className="text-accent" size={23} aria-hidden="true" />
              <h2 className="mt-5 text-lg font-semibold">Controlled by design</h2>
              <p className="mt-3 text-sm leading-7 text-white/60">
                {configuration?.managedOidcEnabled
                  ? "Better Auth verifies the person. BidMatrix remains authoritative for organizations, roles, sessions, and access."
                  : "Password changes rotate your security stamp and revoke every existing BidMatrix session. You must sign in again afterward."}
              </p>
            </section>
          </div>

          {configuration?.managedOidcEnabled ? (
            <section className="panel overflow-hidden" aria-labelledby="managed-identity-heading">
              <div className="flex flex-col gap-4 border-b px-5 py-5 sm:flex-row sm:items-center sm:justify-between sm:px-7">
                <div>
                  <div className="flex items-center gap-2">
                    <Fingerprint className="text-brand" size={20} aria-hidden="true" />
                    <h2 className="text-lg font-semibold" id="managed-identity-heading">
                      {configuration.managedOidcProviderName}
                    </h2>
                  </div>
                  <p className="mt-2 text-sm text-muted">
                    Link the Better Auth account that uses {user.email}. After
                    linking, email/password, Google, and GitHub all resolve to
                    the same BidMatrix account.
                  </p>
                </div>
                <a
                  className="button-secondary shrink-0"
                  href={`${apiBaseUrl}/v1/auth/oidc/link?returnUrl=${encodeURIComponent("/app/account")}`}
                >
                  <Fingerprint size={16} aria-hidden="true" />
                  {identities.some((identity) => identity.status === "active")
                    ? "Verify link"
                    : "Link Better Auth"}
                </a>
              </div>
              {identities.length === 0 ? (
                <p className="px-5 py-6 text-sm leading-6 text-muted sm:px-7">
                  No Better Auth identity is linked yet. Use the controlled
                  transition once, then keep the native BidMatrix login hidden.
                </p>
              ) : (
                <ul className="divide-y">
                  {identities.map((identity) => (
                    <li className="flex flex-col gap-4 px-5 py-5 sm:flex-row sm:items-center sm:justify-between sm:px-7" key={identity.id}>
                      <div className="min-w-0">
                        <div className="flex flex-wrap items-center gap-2">
                          <p className="text-sm font-semibold">{identity.emailAtLink}</p>
                          <span className={`rounded-full px-2.5 py-1 text-[0.68rem] font-bold uppercase tracking-wide ${identity.status === "active" ? "bg-emerald-100 text-emerald-900" : "bg-stone-200 text-stone-700"}`}>
                            {identity.status}
                          </span>
                        </div>
                        <p className="mt-1 break-all text-xs leading-5 text-muted">
                          Issuer: {identity.issuer}. Linked {formatDate(identity.linkedAt)}.
                        </p>
                      </div>
                      {identity.status === "active" && canRevokeActiveIdentity ? (
                        <button
                          className="button-secondary h-9 shrink-0 px-3 text-red-800"
                          disabled={identityAction !== null}
                          type="button"
                          onClick={() => void revokeIdentity(identity)}
                        >
                          {identityAction === identity.id ? (
                            <LoaderCircle className="animate-spin" size={14} aria-hidden="true" />
                          ) : null}
                          Revoke and sign out
                        </button>
                      ) : identity.status === "active" ? (
                        <p className="text-xs font-semibold text-muted">
                          Required sign-in method
                        </p>
                      ) : null}
                    </li>
                  ))}
                </ul>
              )}
            </section>
          ) : null}

          <div className={`grid gap-5 ${configuration?.nativeLoginEnabled ? "xl:grid-cols-[0.8fr_1.2fr]" : ""}`}>
            {configuration?.nativeLoginEnabled && nativePasswordEnabled ? (
              <section className="panel p-6 sm:p-8" aria-labelledby="change-password-heading">
              <KeyRound className="text-brand" size={22} aria-hidden="true" />
              <h2 className="mt-4 text-lg font-semibold" id="change-password-heading">Change password</h2>
              <p className="mt-2 text-sm leading-6 text-muted">Use at least 8 characters. All sessions will be closed.</p>
              <form className="mt-6 space-y-4" onSubmit={changePassword}>
                <PasswordField
                  autoComplete="current-password"
                  label="Current password"
                  value={currentPassword}
                  onChange={setCurrentPassword}
                />
                <PasswordField
                  autoComplete="new-password"
                  label="New password"
                  value={newPassword}
                  onChange={setNewPassword}
                  minimumLength={MINIMUM_PASSWORD_LENGTH}
                />
                <PasswordField
                  autoComplete="new-password"
                  label="Confirm new password"
                  value={confirmation}
                  onChange={setConfirmation}
                  minimumLength={MINIMUM_PASSWORD_LENGTH}
                />
                <button className="button-primary w-full" disabled={submitting} type="submit">
                  {submitting ? <LoaderCircle className="animate-spin" size={16} aria-hidden="true" /> : <RefreshCcw size={16} aria-hidden="true" />}
                  Change password
                </button>
              </form>
              </section>
            ) : configuration?.nativeLoginEnabled ? (
              <section className="panel p-6 sm:p-8" aria-labelledby="managed-only-heading">
                <KeyRound className="text-brand" size={22} aria-hidden="true" />
                <h2 className="mt-4 text-lg font-semibold" id="managed-only-heading">
                  Managed-only account
                </h2>
                <p className="mt-2 text-sm leading-6 text-muted">
                  No native password is enabled for this account. Continue using the linked managed identity.
                </p>
              </section>
            ) : null}

            <section className="panel overflow-hidden" aria-labelledby="sessions-heading">
              <div className="flex flex-col gap-4 border-b px-5 py-5 sm:flex-row sm:items-center sm:justify-between sm:px-7">
                <div>
                  <h2 className="text-lg font-semibold" id="sessions-heading">Sessions</h2>
                  <p className="mt-1 text-sm text-muted">Idle sessions close after 30 minutes.</p>
                </div>
                <button
                  className="button-secondary shrink-0"
                  disabled={sessionAction !== null}
                  type="button"
                  onClick={() => void revokeOtherSessions()}
                >
                  {sessionAction === "others" ? <LoaderCircle className="animate-spin" size={15} aria-hidden="true" /> : <LogOut size={15} aria-hidden="true" />}
                  Close other sessions
                </button>
              </div>
              <ul className="divide-y">
                {sessions.map((session) => (
                  <li className="flex flex-col gap-4 px-5 py-5 sm:flex-row sm:items-center sm:justify-between sm:px-7" key={session.id}>
                    <div className="flex min-w-0 items-start gap-3">
                      <span className="mt-0.5 grid size-9 shrink-0 place-items-center rounded-xl bg-surface-muted text-muted">
                        <MonitorSmartphone size={17} aria-hidden="true" />
                      </span>
                      <div className="min-w-0">
                        <div className="flex flex-wrap items-center gap-2">
                          <p className="text-sm font-semibold">Browser session</p>
                          <SessionStatus session={session} />
                        </div>
                        <p className="mt-1 text-xs leading-5 text-muted">
                          Started {formatDate(session.createdAt)}. Last active {formatDate(session.lastSeenAt)}.
                        </p>
                      </div>
                    </div>
                    {session.status === "active" ? (
                      <button
                        className="button-secondary h-9 shrink-0 px-3 text-red-800"
                        disabled={sessionAction !== null}
                        type="button"
                        onClick={() => void revokeSession(session)}
                      >
                        {sessionAction === session.id ? <LoaderCircle className="animate-spin" size={14} aria-hidden="true" /> : null}
                        {session.isCurrent ? "Sign out" : "Revoke"}
                      </button>
                    ) : null}
                  </li>
                ))}
              </ul>
            </section>
          </div>
        </>
      )}
    </div>
  );
}

function PasswordField({
  autoComplete,
  label,
  value,
  onChange,
  minimumLength,
}: {
  autoComplete: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  minimumLength?: number;
}) {
  return (
    <label className="block text-sm font-semibold">
      {label}
      <input
        className="field mt-2 h-11 font-normal"
        type="password"
        autoComplete={autoComplete}
        minLength={minimumLength}
        maxLength={128}
        required
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
    </label>
  );
}

function SessionStatus({ session }: { session: UserSession }) {
  const style = session.status === "active"
    ? "bg-emerald-100 text-emerald-900"
    : "bg-stone-200 text-stone-700";
  const label = session.isCurrent ? "current" : session.status;
  return <span className={`rounded-full px-2.5 py-1 text-[0.68rem] font-bold uppercase tracking-wide ${style}`}>{label}</span>;
}

function Row({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div className="grid gap-2 py-4 sm:grid-cols-[12rem_1fr] sm:items-center">
      <dt className="flex items-center gap-2 text-sm text-muted">{icon}{label}</dt>
      <dd className="text-sm font-semibold sm:text-right">{value}</dd>
    </div>
  );
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat("en", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}

function identityErrorMessage(code: string) {
  switch (code) {
    case "email_mismatch":
      return "The provider email does not match this BidMatrix account.";
    case "identity_conflict":
      return "This provider identity is already linked to another BidMatrix account.";
    case "authentication_too_old":
      return "The provider sign-in was not recent enough. Try linking again.";
    default:
      return "The managed identity link could not be completed. Try again or contact support.";
  }
}
