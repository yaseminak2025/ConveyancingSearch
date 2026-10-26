import type { SupportedLocation } from "./api";

export const MAX_CITIES = 8;
const MAX_CITY_LENGTH = 80;
const CITY_PATTERN = /^\p{L}[\p{L}\s.'-]*$/u;

/** Splits comma/newline-separated text into trimmed, de-duplicated city names. */
export function splitCities(text: string): string[] {
  const unique = new Map<string, string>();
  for (const part of text.split(/[,\n]/)) {
    const city = part.trim().replace(/\s+/g, " ");
    const key = city.toLocaleLowerCase("en-GB");
    if (city && !unique.has(key)) unique.set(key, city);
  }
  return [...unique.values()];
}

export function resolveCity(
  locations: SupportedLocation[],
  name: string,
): SupportedLocation | undefined {
  const wanted = name.trim().toLowerCase();
  return locations.find(
    (x) =>
      x.key.toLowerCase() === wanted || x.displayName.toLowerCase() === wanted,
  );
}

export type CitySelection =
  { ok: true; cities: string[] } | { ok: false; error: string };

/** Validates the typed selection and maps it to canonical display names. */
export function parseSelection(
  text: string,
  locations: SupportedLocation[],
): CitySelection {
  const cities = splitCities(text);
  if (
    cities.length < 1 ||
    cities.length > MAX_CITIES ||
    cities.some((x) => x.length > MAX_CITY_LENGTH || !CITY_PATTERN.test(x))
  )
    return {
      ok: false,
      error: "Enter one to eight different city names, separated by commas.",
    };
  const resolved = cities.map((city) => resolveCity(locations, city));
  if (resolved.some((city) => !city))
    return {
      ok: false,
      error: "Unsupported city. Select a city from the supported locations.",
    };
  return {
    ok: true,
    cities: [...new Set(resolved.map((city) => city!.displayName))],
  };
}
