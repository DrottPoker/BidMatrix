import {
  enabledSocialProviderIds,
  readSocialProviderConfiguration,
} from "@/lib/social-providers";

export const dynamic = "force-dynamic";

export function GET() {
  try {
    return Response.json(
      {
        providers: enabledSocialProviderIds(readSocialProviderConfiguration()),
      },
      {
        headers: { "Cache-Control": "no-store" },
      },
    );
  } catch {
    return Response.json(
      { error: "Social sign-in configuration is invalid." },
      { status: 503, headers: { "Cache-Control": "no-store" } },
    );
  }
}
