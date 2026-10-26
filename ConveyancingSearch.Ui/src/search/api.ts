import { z } from "zod";
const locationsSchema = z
  .array(z.object({ key: z.string().min(1), displayName: z.string().min(1) }))
  .min(1);
export type SupportedLocation = z.infer<typeof locationsSchema>[number];
export async function loadLocations(
  signal: AbortSignal,
): Promise<SupportedLocation[]> {
  const response = await request("/api/locations", { signal });
  if (!response.ok)
    throw new Error("Supported cities could not be loaded. Please retry.");
  const parsed = locationsSchema.safeParse(await readJson(response, signal));
  if (!parsed.success)
    throw new Error(
      "The server returned an unexpected city list. Please retry.",
    );
  return parsed.data;
}
const httpUrl = z
  .string()
  .url()
  .refine((value) => ["http:", "https:"].includes(new URL(value).protocol));
const details = z.object({
  name: z.string().min(1),
  address: z.string().min(1),
  phone: z.string().nullable(),
  website: httpUrl.nullable(),
  contactUrl: httpUrl.nullable(),
  sourceUrl: httpUrl,
});
const listing = z.object({
  id: z.string().uuid(),
  details,
  isActive: z.boolean(),
  discovery: z.enum(["new", "returned", "existing"]),
  firstSeenAtUtc: z.iso.datetime({ offset: true }),
  lastSeenAtUtc: z.iso.datetime({ offset: true }),
  inactivatedAtUtc: z.iso.datetime({ offset: true }).nullable(),
});
const resultSchema = z.object({
  location: z.string(),
  status: z.enum(["cached", "refreshed", "fallback", "unavailable"]),
  lastSuccessAtUtc: z.iso.datetime({ offset: true }).nullable(),
  lastAttemptAtUtc: z.iso.datetime({ offset: true }).nullable(),
  warning: z.string().nullable(),
  listings: z.array(listing),
});
export type Listing = z.infer<typeof listing>;
export type LocationResult = z.infer<typeof resultSchema>;
export type SearchCity = (
  city: string,
  signal: AbortSignal,
) => Promise<LocationResult>;
export const searchCity: SearchCity = async (city, signal) => {
  const response = await request("/api/search", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ location: city }),
    signal,
  });
  const body = await readJson(response, signal);
  if (!response.ok && response.status !== 503) {
    const problem = z
      .object({
        detail: z.string().optional(),
        title: z.string().optional(),
        errors: z.record(z.string(), z.array(z.string())).optional(),
      })
      .safeParse(body);
    throw new Error(
      problem.success
        ? (problem.data.errors?.location?.join(" ") ??
            problem.data.detail ??
            problem.data.title ??
            "Search failed.")
        : "Search failed. Please try again.",
    );
  }
  const parsed = resultSchema.safeParse(body);
  if (!parsed.success)
    throw new Error(
      "The server returned an unexpected response. Please try again.",
    );
  return parsed.data;
};

async function request(url: string, options: RequestInit): Promise<Response> {
  try {
    return await fetch(url, options);
  } catch (cause) {
    if (options.signal?.aborted) throw cause;
    throw new Error(
      "Unable to reach the server. Check your connection and try again.",
    );
  }
}

async function readJson(
  response: Response,
  signal: AbortSignal,
): Promise<unknown> {
  try {
    return await response.json();
  } catch (cause) {
    if (signal.aborted) throw cause;
    throw new Error(
      response.ok
        ? "The server returned an unreadable response. Please try again."
        : `The server could not complete the request (HTTP ${response.status}). Please try again.`,
    );
  }
}
