import type { Metadata } from "next";
import { IdentityPageShell } from "@/components/identity-page-shell";
import { ResetPasswordForm } from "@/components/reset-password-form";

export const metadata: Metadata = { title: "Choose a new password" };

export default function ResetPasswordPage() {
  return (
    <IdentityPageShell
      title="Choose a new password"
      description="Use at least eight characters. Completing the reset signs out existing sessions."
    >
      <ResetPasswordForm />
    </IdentityPageShell>
  );
}
