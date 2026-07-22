import type { Metadata } from "next";
import { AccountPage } from "@/components/account-page";

export const metadata: Metadata = { title: "Account" };

export default function CustomerAccountPage() {
  return <AccountPage />;
}
