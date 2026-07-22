"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { FilePlus2, Files, Gauge, LogOut, Settings2, ShieldCheck } from "lucide-react";
import { apiGet, apiMutation, CurrentUser } from "@/lib/bidmatrix-api";

const navigation = [
  { href: "/app", label: "Dashboard", icon: Gauge, exact: true },
  { href: "/app/analyses", label: "Analyses", icon: Files },
  { href: "/app/analyses/new", label: "New analysis", icon: FilePlus2 },
  { href: "/app/account", label: "Account", icon: Settings2 },
];

export function CustomerShell({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const [user, setUser] = useState<CurrentUser | null>(null);

  useEffect(() => {
    let active = true;
    apiGet<CurrentUser>("/v1/me")
      .then((result) => {
        if (active) setUser(result);
      })
      .catch(() => undefined);
    return () => {
      active = false;
    };
  }, []);

  async function logout() {
    await apiMutation<void>("/v1/auth/logout", { method: "POST" });
    router.push("/login");
    router.refresh();
  }

  return (
    <div className="min-h-screen bg-background lg:grid lg:grid-cols-[17rem_minmax(0,1fr)]">
      <aside className="hidden border-r border-ink/8 bg-ink text-white lg:sticky lg:top-0 lg:flex lg:h-screen lg:flex-col">
        <div className="flex h-20 items-center gap-3 border-b border-white/10 px-6">
          <span className="grid size-10 place-items-center rounded-[0.9rem] bg-accent text-sm font-black tracking-tight text-ink">BM</span>
          <div>
            <p className="font-semibold tracking-tight">BidMatrix</p>
            <p className="text-[10px] font-bold uppercase tracking-[0.2em] text-white/45">RFP intelligence</p>
          </div>
        </div>

        <nav aria-label="Customer workspace" className="flex-1 space-y-1 p-4">
          {navigation.map((item) => {
            const active = navigationItemIsActive(pathname, item.href, item.exact);
            const Icon = item.icon;
            return (
              <Link
                className={`group flex items-center gap-3 rounded-xl px-3.5 py-3 text-sm font-medium transition ${active ? "bg-white text-ink shadow-sm" : "text-white/65 hover:bg-white/7 hover:text-white"}`}
                href={item.href}
                key={item.href}
              >
                <Icon className={active ? "text-brand" : "text-white/45 group-hover:text-accent"} size={18} />
                {item.label}
              </Link>
            );
          })}
        </nav>

        <div className="m-4 rounded-2xl border border-white/10 bg-white/5 p-4">
          <div className="flex items-start gap-3">
            <span className="grid size-9 shrink-0 place-items-center rounded-full bg-accent/15 text-accent"><ShieldCheck size={17} /></span>
            <div className="min-w-0">
              <p className="truncate text-sm font-semibold">{user?.displayName ?? "Customer workspace"}</p>
              <p className="mt-0.5 truncate text-xs text-white/45">{user?.email ?? "Secure session"}</p>
            </div>
          </div>
          <button className="mt-4 flex items-center gap-2 text-xs font-semibold text-white/55 transition hover:text-white" onClick={() => void logout()} type="button">
            <LogOut size={14} /> Sign out
          </button>
        </div>
      </aside>

      <div className="min-w-0">
        <header className="sticky top-0 z-30 border-b border-ink/8 bg-background/90 backdrop-blur-xl lg:hidden">
          <div className="flex h-16 items-center justify-between px-5">
            <Link className="flex items-center gap-2.5 font-semibold" href="/app">
              <span className="grid size-9 place-items-center rounded-xl bg-ink text-xs font-black text-accent">BM</span>
              BidMatrix
            </Link>
            <Link className="rounded-full border border-ink/10 bg-white px-3 py-1.5 text-xs font-semibold" href="/app/account">Account</Link>
          </div>
        </header>
        <main className="min-h-screen pb-24 lg:pb-0">{children}</main>
      </div>

      <nav aria-label="Mobile customer workspace" className="fixed inset-x-3 bottom-3 z-40 grid grid-cols-4 rounded-2xl border border-ink/10 bg-white/95 p-1.5 shadow-[0_18px_60px_rgba(22,38,31,0.18)] backdrop-blur-xl lg:hidden">
        {navigation.map((item) => {
          const active = navigationItemIsActive(pathname, item.href, item.exact);
          const Icon = item.icon;
          return (
            <Link className={`flex flex-col items-center gap-1 rounded-xl px-1 py-2 text-[10px] font-semibold ${active ? "bg-ink text-white" : "text-muted"}`} href={item.href} key={item.href}>
              <Icon size={17} />
              {item.label.replace(" analysis", "")}
            </Link>
          );
        })}
      </nav>
    </div>
  );
}

function navigationItemIsActive(pathname: string, href: string, exact?: boolean) {
  if (exact) return pathname === href;
  if (href === "/app/analyses") return pathname.startsWith(href) && pathname !== "/app/analyses/new";
  return pathname.startsWith(href);
}
