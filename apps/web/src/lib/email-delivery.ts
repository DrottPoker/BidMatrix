import nodemailer, { type Transporter } from "nodemailer";

export type EmailDeliveryConfiguration = {
  fromAddress: string;
  fromName: string;
  host: string;
  password: string | null;
  port: number;
  requireTls: boolean;
  secure: boolean;
  user: string | null;
};

type EnvironmentValues = Record<string, string | undefined>;

export type IdentityEmailDelivery = {
  sendPasswordResetEmail(input: { email: string; name: string; url: string }): Promise<void>;
  sendVerificationEmail(input: { email: string; name: string; url: string }): Promise<void>;
};

export function createIdentityEmailDelivery(
  configuration: EmailDeliveryConfiguration,
  transporter: Transporter = createTransporter(configuration),
): IdentityEmailDelivery {
  const from = {
    address: configuration.fromAddress,
    name: configuration.fromName,
  };

  return {
    async sendVerificationEmail({ email, name, url }) {
      await transporter.sendMail({
        from,
        to: email,
        subject: "Verify your BidMatrix email",
        text: verificationText(name, url),
        html: verificationHtml(name, url),
      });
    },
    async sendPasswordResetEmail({ email, name, url }) {
      await transporter.sendMail({
        from,
        to: email,
        subject: "Reset your BidMatrix password",
        text: passwordResetText(name, url),
        html: passwordResetHtml(name, url),
      });
    },
  };
}

export function readEmailDeliveryConfiguration(
  environment: EnvironmentValues,
): EmailDeliveryConfiguration {
  const host = readRequired(environment, "BETTER_AUTH_SMTP_HOST");
  const port = parsePort(environment.BETTER_AUTH_SMTP_PORT);
  const secure = parseBoolean(environment, "BETTER_AUTH_SMTP_SECURE");
  const requireTls = parseBoolean(environment, "BETTER_AUTH_SMTP_REQUIRE_TLS");
  const fromAddress = readRequired(environment, "BETTER_AUTH_EMAIL_FROM_ADDRESS");
  const fromName = readOptional(environment.BETTER_AUTH_EMAIL_FROM_NAME) ?? "BidMatrix";
  const user = readOptional(environment.BETTER_AUTH_SMTP_USER);
  const password = readOptional(environment.BETTER_AUTH_SMTP_PASSWORD);

  if ((user === null) !== (password === null)) {
    throw new Error(
      "BETTER_AUTH_SMTP_USER and BETTER_AUTH_SMTP_PASSWORD must be configured together.",
    );
  }
  if (!isValidEmailAddress(fromAddress)) {
    throw new Error("BETTER_AUTH_EMAIL_FROM_ADDRESS must contain a valid email address.");
  }
  if (fromName.length > 100 || [...fromName].some((character) => isControl(character))) {
    throw new Error("BETTER_AUTH_EMAIL_FROM_NAME must contain at most 100 safe characters.");
  }

  return {
    fromAddress,
    fromName,
    host,
    password,
    port,
    requireTls,
    secure,
    user,
  };
}

function createTransporter(configuration: EmailDeliveryConfiguration) {
  return nodemailer.createTransport({
    host: configuration.host,
    port: configuration.port,
    secure: configuration.secure,
    requireTLS: configuration.requireTls,
    auth:
      configuration.user && configuration.password
        ? { user: configuration.user, pass: configuration.password }
        : undefined,
    pool: true,
    maxConnections: 3,
    disableFileAccess: true,
    disableUrlAccess: true,
  });
}

function verificationText(name: string, url: string) {
  return [
    `Hello ${normalizeName(name)},`,
    "",
    "Verify your email to activate your BidMatrix account:",
    url,
    "",
    "This link expires in one hour. If you did not create an account, you can ignore this email.",
  ].join("\n");
}

function verificationHtml(name: string, url: string) {
  return emailHtml({
    action: "Verify email",
    heading: "Verify your email",
    introduction: `Hello ${normalizeName(name)}, verify your email to activate your BidMatrix account.`,
    url,
    warning:
      "This link expires in one hour. If you did not create an account, you can ignore this email.",
  });
}

function passwordResetText(name: string, url: string) {
  return [
    `Hello ${normalizeName(name)},`,
    "",
    "Use this link to choose a new BidMatrix password:",
    url,
    "",
    "This link expires in 30 minutes and can be used once. If you did not request it, you can ignore this email.",
  ].join("\n");
}

function passwordResetHtml(name: string, url: string) {
  return emailHtml({
    action: "Reset password",
    heading: "Reset your password",
    introduction: `Hello ${normalizeName(name)}, use this link to choose a new BidMatrix password.`,
    url,
    warning:
      "This link expires in 30 minutes and can be used once. If you did not request it, you can ignore this email.",
  });
}

function emailHtml(input: {
  action: string;
  heading: string;
  introduction: string;
  url: string;
  warning: string;
}) {
  const safeUrl = escapeHtml(input.url);
  return `<!doctype html>
<html lang="en">
  <body style="margin:0;background:#f5f3ed;color:#17231d;font-family:Arial,sans-serif">
    <div style="margin:0 auto;max-width:560px;padding:48px 24px">
      <p style="font-size:14px;font-weight:700;letter-spacing:.08em">BIDMATRIX</p>
      <div style="border-radius:18px;background:#ffffff;padding:32px">
        <h1 style="font-size:26px;margin:0 0 16px">${escapeHtml(input.heading)}</h1>
        <p style="font-size:16px;line-height:1.6;margin:0 0 24px">${escapeHtml(input.introduction)}</p>
        <p style="margin:0 0 24px"><a href="${safeUrl}" style="display:inline-block;border-radius:10px;background:#176b4d;color:#ffffff;font-weight:700;padding:13px 20px;text-decoration:none">${escapeHtml(input.action)}</a></p>
        <p style="font-size:13px;line-height:1.6;color:#66736c;margin:0">${escapeHtml(input.warning)}</p>
      </div>
    </div>
  </body>
</html>`;
}

function normalizeName(value: string) {
  const normalized = value.trim();
  return normalized && ![...normalized].some((character) => isControl(character))
    ? normalized
    : "there";
}

function escapeHtml(value: string) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function parsePort(value: string | undefined) {
  const normalized = readOptional(value);
  if (normalized === null) throw new Error("BETTER_AUTH_SMTP_PORT is required.");
  const port = Number.parseInt(normalized, 10);
  if (!Number.isInteger(port) || `${port}` !== normalized || port < 1 || port > 65_535) {
    throw new Error("BETTER_AUTH_SMTP_PORT must be a valid TCP port.");
  }
  return port;
}

function parseBoolean(environment: EnvironmentValues, key: string) {
  const value = readRequired(environment, key).toLowerCase();
  if (value === "true") return true;
  if (value === "false") return false;
  throw new Error(`${key} must be true or false.`);
}

function readRequired(environment: EnvironmentValues, key: string) {
  const value = readOptional(environment[key]);
  if (value === null) throw new Error(`${key} is required.`);
  return value;
}

function readOptional(value: string | undefined) {
  const normalized = value?.trim();
  return normalized ? normalized : null;
}

function isValidEmailAddress(value: string) {
  return value.length <= 320 && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value);
}

function isControl(character: string) {
  const codePoint = character.codePointAt(0) ?? 0;
  return codePoint <= 31 || codePoint === 127;
}
