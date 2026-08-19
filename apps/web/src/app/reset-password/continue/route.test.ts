import { describe, expect, it } from "vitest";
import { GET } from "./route";

describe("password reset continuation", () => {
  it("moves the bearer token from the query string into a browser fragment", async () => {
    const response = await GET(
      new Request(
        "https://app.example.test/reset-password/continue?token=single-use-token",
      ),
    );

    expect(response.status).toBe(303);
    expect(response.headers.get("location")).toBe(
      "https://app.example.test/reset-password#token=single-use-token",
    );
  });
});
