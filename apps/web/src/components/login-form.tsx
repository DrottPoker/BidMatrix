"use client";

import { FormEvent, useEffect, useState, useSyncExternalStore } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { ArrowRight, LoaderCircle, LockKeyhole } from "lucide-react";
import {
  AuthenticationConfiguration,
  CurrentUser,
  apiBaseUrl,
  apiGet,
  apiMutation,
  formatApiError,
} from "@/lib/bidmatrix-api";
import {
  authClient,
  betterAuthErrorMessage,
  followBetterAuthRedirect,
} from "@/lib/auth-client";

export function LoginForm() {
  const router = useRouter();
  const [configuration, setConfiguration] =
    useState<AuthenticationConfiguration | null>(null);
  const [providerEmail, setProviderEmail] = useState("");
  const [providerPassword, setProviderPassword] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const locationSearch = useSyncExternalStore(
    subscribeToLocationSearch,
    readLocationSearch,
    readServerLocationSearch,
  );
  const search = new URLSearchParams(locationSearch);
  const providerAuthorization = search.has("client_id") && search.has("sig");
  const identityError = search.get("identityError");
  const displayedError = error ?? (
    identityError ? identityErrorMessage(identityError) : null
  );
  const managedLoginUrl = `${apiBaseUrl}/v1/auth/oidc/login?returnUrl=${encodeURIComponent("/app")}`;

  useEffect(() => {
    if (!identityError) return;
    const url = new URL(window.location.href);
    url.searchParams.delete("identityError");
    window.history.replaceState(null, "", `${url.pathname}${url.search}${url.hash}`);
  }, [identityError]);

  useEffect(() => {
    let active = true;
    void apiGet<AuthenticationConfiguration>("/v1/auth/configuration")
      .then((result) => {
        if (active) setConfiguration(result);
      })
      .catch((requestError: unknown) => {
        if (active) setError(formatApiError(requestError));
      });

    return () => {
      active = false;
    };
  }, []);

  useEffect(() => {
    if (
      configuration?.managedOidcEnabled &&
      !providerAuthorization &&
      !identityError
    ) {
      router.replace(managedLoginUrl);
    }
  }, [configuration, identityError, managedLoginUrl, providerAuthorization, router]);

  async function submitProviderPassword(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setError(null);
    setSubmitting(true);

    try {
      const result = await authClient.signIn.email({
        email: providerEmail,
        password: providerPassword,
        rememberMe: false,
      });
      if (result.error) {
        setError(betterAuthErrorMessage(result.error));
      } else if (!followBetterAuthRedirect(result.data)) {
        setError("The secure sign-in completed but the BidMatrix handoff did not continue.");
      }
    } catch (submissionError) {
      setError(betterAuthErrorMessage(submissionError));
    } finally {
      setSubmitting(false);
    }
  }

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
      setError(formatApiError(submissionError));
    } finally {
      setSubmitting(false);
    }
  }

  if (!configuration && !displayedError) {
    return (
      <div className="mt-8 flex items-center gap-2 text-sm text-muted" role="status">
        <LoaderCircle className="animate-spin" aria-hidden="true" size={17} />
        Loading secure sign-in
      </div>
    );
  }

  return (
    <div className="mt-8 space-y-5">
      {configuration?.managedOidcEnabled && providerAuthorization ? (
        <section className="space-y-5" aria-labelledby="provider-sign-in-heading">
          <div>
            <p className="text-sm font-semibold" id="provider-sign-in-heading">
              Secure identity sign-in
            </p>
            <p className="mt-1 text-xs leading-5 text-muted">
              Sign in with your verified email and password. Your BidMatrix roles
              are applied after sign-in.
            </p>
          </div>
          <form className="space-y-5" onSubmit={submitProviderPassword}>
            <label className="block text-sm font-semibold">
              Email
              <input
                className="field mt-2 h-12 font-normal"
                type="email"
                autoComplete="username"
                required
                value={providerEmail}
                onChange={(event) => setProviderEmail(event.target.value)}
              />
            </label>
            <label className="block text-sm font-semibold">
              Password
              <input
                className="field mt-2 h-12 font-normal"
                type="password"
                autoComplete="current-password"
                required
                value={providerPassword}
                onChange={(event) => setProviderPassword(event.target.value)}
              />
            </label>
            <div className="text-right">
              <Link
                className="text-xs font-semibold text-brand hover:underline"
                href="/forgot-password"
              >
                Forgot password?
              </Link>
            </div>
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
              Sign in with password
            </button>
          </form>
          <p className="text-center text-sm text-muted">
            New to BidMatrix?{" "}
            <Link className="font-semibold text-brand hover:underline" href="/register">
              Create account
            </Link>
          </p>
        </section>
      ) : configuration?.managedOidcEnabled ? (
        <div className="space-y-3">
          {!identityError ? (
            <p className="text-sm text-muted" role="status">
              Opening secure sign-in...
            </p>
          ) : null}
          <a className="button-primary h-12 w-full" href={managedLoginUrl}>
            <LockKeyhole aria-hidden="true" size={18} />
            Open sign in
            <ArrowRight aria-hidden="true" size={17} />
          </a>
          <p className="text-center text-sm text-muted">
            New to BidMatrix?{" "}
            <Link className="font-semibold text-brand hover:underline" href="/register">
              Create account
            </Link>
          </p>
        </div>
      ) : null}

      {configuration?.nativeLoginEnabled &&
      !configuration.managedOidcEnabled &&
      !providerAuthorization ? (
        <form className="space-y-5" onSubmit={submit}>
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
          {configuration.nativeRecoveryEnabled ? (
            <div className="text-right">
              <Link
                className="text-xs font-semibold text-brand hover:underline"
                href="/recover"
              >
                Use a recovery link
              </Link>
            </div>
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
            Sign in with password
            <ArrowRight aria-hidden="true" size={17} />
          </button>
        </form>
      ) : null}

      {displayedError ? (
        <p className="rounded-xl bg-red-50 px-4 py-3 text-sm text-red-800" role="alert">
          {displayedError}
        </p>
      ) : null}

      {configuration &&
      !configuration.managedOidcEnabled &&
      !configuration.nativeLoginEnabled ? (
        <p className="rounded-xl bg-amber-50 px-4 py-3 text-sm text-amber-900">
          No sign-in method is currently available. Contact BidMatrix support.
        </p>
      ) : null}
    </div>
  );
}

function subscribeToLocationSearch(onStoreChange: () => void) {
  window.addEventListener("popstate", onStoreChange);
  return () => window.removeEventListener("popstate", onStoreChange);
}

function readLocationSearch() {
  return window.location.search;
}

function readServerLocationSearch() {
  return "";
}

function identityErrorMessage(code: string) {
  switch (code) {
    case "identity_not_linked":
      return "This managed identity is not linked to a BidMatrix account.";
    case "authentication_too_old":
      return "The provider sign-in was not recent enough. Try again.";
    case "identity_revoked":
      return "This managed identity link has been revoked.";
    default:
      return "Managed identity sign-in could not be completed. Try again or contact support.";
  }
}
