import { toNextJsHandler } from "better-auth/next-js";
import { getBetterAuth } from "@/lib/better-auth";

export const dynamic = "force-dynamic";
export const runtime = "nodejs";

export async function GET(request: Request) {
  return toNextJsHandler(getBetterAuth()).GET(request);
}

export async function POST(request: Request) {
  return toNextJsHandler(getBetterAuth()).POST(request);
}
