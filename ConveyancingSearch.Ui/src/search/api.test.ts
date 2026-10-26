import { afterEach, expect, test, vi } from "vitest";
import { searchCity, loadLocations } from "./api";
afterEach(() => vi.unstubAllGlobals());
const signal = () => new AbortController().signal;
test.each([502, 503, 500])(
  "non-JSON HTTP %s errors have a useful message",
  async (status) => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          new Response("<html>proxy failure</html>", { status }),
        ),
    );
    await expect(searchCity("Leeds", signal())).rejects.toThrow(
      `HTTP ${status}`,
    );
  },
);
test.each(["", "not json", "{}"])(
  "invalid success body %s is handled",
  async (body) => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(body)));
    await expect(searchCity("Leeds", signal())).rejects.toThrow(
      /server returned/i,
    );
  },
);
test("network failures do not expose browser error text", async () => {
  vi.stubGlobal(
    "fetch",
    vi.fn().mockRejectedValue(new TypeError("raw network internals")),
  );
  await expect(searchCity("Leeds", signal())).rejects.toThrow(
    "Unable to reach the server",
  );
});
test("cancellation remains cancellation", async () => {
  const controller = new AbortController();
  controller.abort();
  const abort = new DOMException("Aborted", "AbortError");
  vi.stubGlobal("fetch", vi.fn().mockRejectedValue(abort));
  await expect(searchCity("Leeds", controller.signal)).rejects.toBe(abort);
});
test("field validation is preserved", async () => {
  vi.stubGlobal(
    "fetch",
    vi
      .fn()
      .mockResolvedValue(
        new Response(
          JSON.stringify({ errors: { location: ["Unsupported city."] } }),
          { status: 400 },
        ),
      ),
  );
  await expect(searchCity("Banana", signal())).rejects.toThrow(
    "Unsupported city.",
  );
});
test("valid unavailable result remains a report result", async () => {
  const body = {
    location: "leeds",
    status: "unavailable",
    lastSuccessAtUtc: null,
    lastAttemptAtUtc: null,
    warning: "Source unavailable",
    listings: [],
  };
  vi.stubGlobal(
    "fetch",
    vi
      .fn()
      .mockResolvedValue(new Response(JSON.stringify(body), { status: 503 })),
  );
  await expect(searchCity("Leeds", signal())).resolves.toEqual(body);
});
test("invalid catalogue response has a controlled message", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response("{}")));
  await expect(loadLocations(signal())).rejects.toThrow("unexpected city list");
});
