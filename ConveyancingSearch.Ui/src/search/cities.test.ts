import { expect, test } from "vitest";
import { parseSelection, resolveCity, splitCities } from "./cities";

const locations = ["London", "Leeds", "Bristol"].map((displayName) => ({
  key: displayName.toLowerCase(),
  displayName,
}));

test("splitting trims, collapses whitespace and removes case-insensitive duplicates", () => {
  expect(splitCities(" Leeds ,leeds,\n  Bristol  , ,LONDON")).toEqual([
    "Leeds",
    "Bristol",
    "LONDON",
  ]);
});

test("cities resolve by key or display name, ignoring case", () => {
  expect(resolveCity(locations, " LEEDS ")?.displayName).toBe("Leeds");
  expect(resolveCity(locations, "bristol")?.displayName).toBe("Bristol");
  expect(resolveCity(locations, "Banana")).toBeUndefined();
});

test("a valid selection maps to canonical display names", () => {
  expect(parseSelection("leeds, LONDON", locations)).toEqual({
    ok: true,
    cities: ["Leeds", "London"],
  });
});

test.each([
  ["", "Enter one to eight"],
  ["Leeds, 123", "Enter one to eight"],
  ["A,B,C,D,E,F,G,H,I", "Enter one to eight"],
  ["Leeds, Banana", "Unsupported city"],
])("selection %j is rejected", (text, message) => {
  const selection = parseSelection(text, locations);
  expect(selection.ok).toBe(false);
  if (!selection.ok) expect(selection.error).toContain(message);
});
