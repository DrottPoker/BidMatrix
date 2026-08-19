import { apiBaseUrl } from "@/lib/bidmatrix-api";

export function navigateToApiPath(path: string) {
  if (
    !path.startsWith("/") ||
    path.startsWith("//") ||
    path.includes("\\") ||
    Array.from(path).some((character) => character.charCodeAt(0) < 32)
  ) {
    throw new Error("The API navigation path is invalid.");
  }

  window.location.assign(new URL(path, apiBaseUrl).toString());
}
