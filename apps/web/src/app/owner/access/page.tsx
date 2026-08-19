import type { Metadata } from "next";
import { AccountRecoveryLinks } from "@/components/account-recovery-links";

export const metadata: Metadata = { title: "Account recovery" };

export default function OwnerAccountRecoveryPage() {
  return <AccountRecoveryLinks />;
}
