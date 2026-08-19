import type { Metadata } from "next";
import { EmailVerificationResult } from "@/components/email-verification-result";
import { IdentityPageShell } from "@/components/identity-page-shell";

export const metadata: Metadata = { title: "Email verification" };

export default function EmailVerifiedPage() {
  return (
    <IdentityPageShell
      title="Email verification"
      description="BidMatrix confirms every email address before the first sign-in."
    >
      <EmailVerificationResult />
    </IdentityPageShell>
  );
}
