export const SOCIAL_PROVIDER_IDS = ["google", "github"] as const;

export type SocialProviderId = (typeof SOCIAL_PROVIDER_IDS)[number];

type EnvironmentValues = Record<string, string | undefined>;

type SocialProviderOptions = {
  clientId: string;
  clientSecret: string;
  scope?: string[];
};

export type SocialProviderConfiguration = Partial<
  Record<SocialProviderId, SocialProviderOptions>
>;

export function readSocialProviderConfiguration(
  environment: EnvironmentValues = process.env,
): SocialProviderConfiguration {
  const google = readProvider(environment, "google", {
    clientId: "BETTER_AUTH_GOOGLE_CLIENT_ID",
    clientSecret: "BETTER_AUTH_GOOGLE_CLIENT_SECRET",
  });
  const github = readProvider(environment, "github", {
    clientId: "BETTER_AUTH_GITHUB_CLIENT_ID",
    clientSecret: "BETTER_AUTH_GITHUB_CLIENT_SECRET",
  });

  return {
    ...(google ? { google } : {}),
    ...(github
      ? { github: { ...github, scope: ["read:user", "user:email"] } }
      : {}),
  };
}

export function enabledSocialProviderIds(
  configuration: SocialProviderConfiguration,
): SocialProviderId[] {
  return SOCIAL_PROVIDER_IDS.filter((provider) => configuration[provider]);
}

function readProvider(
  environment: EnvironmentValues,
  provider: SocialProviderId,
  keys: { clientId: string; clientSecret: string },
): SocialProviderOptions | null {
  const clientId = readOptional(environment[keys.clientId]);
  const clientSecret = readOptional(environment[keys.clientSecret]);
  if ((clientId === null) !== (clientSecret === null)) {
    throw new Error(
      `${keys.clientId} and ${keys.clientSecret} must be configured together for ${provider}.`,
    );
  }

  return clientId && clientSecret
    ? { clientId, clientSecret }
    : null;
}

function readOptional(value: string | undefined) {
  const normalized = value?.trim();
  return normalized ? normalized : null;
}
