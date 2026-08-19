import { NextResponse } from "next/server";

export async function GET(request: Request) {
  const source = new URL(request.url);
  const destination = new URL("/reset-password", source.origin);
  const token = source.searchParams.get("token");

  destination.hash = token
    ? new URLSearchParams({ token }).toString()
    : new URLSearchParams({ error: "INVALID_TOKEN" }).toString();

  return NextResponse.redirect(destination, 303);
}
