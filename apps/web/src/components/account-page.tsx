"use client";

import { useEffect, useState } from "react";
import { Building2, LoaderCircle, Mail, ShieldCheck, UserRound } from "lucide-react";
import { CurrentUser, apiGet, formatApiError } from "@/lib/bidmatrix-api";

export function AccountPage() {
  const [user, setUser] = useState<CurrentUser | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    apiGet<CurrentUser>("/v1/me").then(setUser).catch((requestError: unknown) => setError(formatApiError(requestError)));
  }, []);

  return (
    <div className="page-shell space-y-7">
      <header><p className="eyebrow">Settings</p><h1 className="mt-2 text-3xl font-semibold tracking-[-0.035em] sm:text-4xl">Account</h1><p className="mt-3 text-sm text-muted">Your identity and current BidMatrix workspace.</p></header>
      {error ? <div className="rounded-2xl border border-red-200 bg-red-50 p-4 text-sm text-red-900" role="alert">{error}</div> : null}
      {!user ? <div className="panel grid min-h-56 place-items-center"><LoaderCircle className="animate-spin text-brand" aria-label="Loading account" /></div> : (
        <div className="grid gap-5 xl:grid-cols-[1fr_0.7fr]">
          <section className="panel p-6 sm:p-8">
            <div className="flex items-center gap-4"><span className="grid size-14 place-items-center rounded-2xl bg-ink text-accent"><UserRound size={24} /></span><div><h2 className="text-xl font-semibold">{user.displayName ?? "BidMatrix user"}</h2><p className="mt-1 text-sm text-muted">Authenticated customer account</p></div></div>
            <dl className="mt-8 divide-y divide-ink/7 border-y border-ink/7">
              <Row icon={<Mail size={17} />} label="Email" value={user.email} />
              <Row icon={<Building2 size={17} />} label="Organization role" value={user.organizations[0]?.role ?? "Not assigned"} />
              <Row icon={<ShieldCheck size={17} />} label="Session" value="Secure cookie authentication" />
            </dl>
          </section>
          <section className="rounded-[1.4rem] bg-ink p-6 text-white sm:p-8"><ShieldCheck className="text-accent" size={23} /><h2 className="mt-5 text-lg font-semibold">Controlled by design</h2><p className="mt-3 text-sm leading-7 text-white/60">Your workspace is organization-isolated. BidMatrix presents extracted information with source evidence and leaves the commercial decision with your team.</p></section>
        </div>
      )}
    </div>
  );
}

function Row({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return <div className="grid gap-2 py-4 sm:grid-cols-[12rem_1fr] sm:items-center"><dt className="flex items-center gap-2 text-sm text-muted">{icon}{label}</dt><dd className="text-sm font-semibold sm:text-right">{value}</dd></div>;
}
