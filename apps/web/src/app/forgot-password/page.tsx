import type { Metadata } from "next";
import { ForgotPasswordForm } from "@/components/forgot-password-form";
import { IdentityPageShell } from "@/components/identity-page-shell";

export const metadata: Metadata = { title: "Forgot password" };

export default function ForgotPasswordPage() {
  return (
    <IdentityPageShell
      title="Reset your password"
      description="Enter your account email. We will send a secure link if the account exists."
    >
      <ForgotPasswordForm />
    </IdentityPageShell>
  );
}
