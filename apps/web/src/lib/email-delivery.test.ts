import { describe, expect, it, vi } from "vitest";
import { createIdentityEmailDelivery } from "@/lib/email-delivery";

describe("identity email delivery", () => {
  it("sends verification and reset links through the configured sender", async () => {
    const sendMail = vi.fn().mockResolvedValue({ messageId: "message-1" });
    const delivery = createIdentityEmailDelivery(
      {
        fromAddress: "security@example.test",
        fromName: "BidMatrix Security",
        host: "smtp.example.test",
        password: null,
        port: 587,
        requireTls: true,
        secure: false,
        user: null,
      },
      { sendMail } as never,
    );

    await delivery.sendVerificationEmail({
      email: "customer@example.test",
      name: "Customer",
      url: "https://auth.example.test/api/auth/verify-email?token=verification-token",
    });
    await delivery.sendPasswordResetEmail({
      email: "customer@example.test",
      name: "Customer",
      url: "https://auth.example.test/api/auth/reset-password/reset-token",
    });

    expect(sendMail).toHaveBeenCalledTimes(2);
    expect(sendMail.mock.calls[0][0]).toMatchObject({
      from: { address: "security@example.test", name: "BidMatrix Security" },
      to: "customer@example.test",
      subject: "Verify your BidMatrix email",
    });
    expect(sendMail.mock.calls[0][0].text).toContain("verification-token");
    expect(sendMail.mock.calls[1][0].subject).toBe("Reset your BidMatrix password");
    expect(sendMail.mock.calls[1][0].text).toContain("reset-token");
  });
});
