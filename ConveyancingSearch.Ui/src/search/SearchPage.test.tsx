import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, test, vi } from "vitest";
import { SearchPage } from "./SearchPage";
import type { LocationResult } from "./api";
const load = async () =>
  ["London", "Leeds", "Bristol"].map((displayName) => ({
    key: displayName.toLowerCase(),
    displayName,
  }));
test("shows a completed city while another city is still loading", async () => {
  const result: LocationResult = {
    location: "leeds",
    status: "cached",
    lastSuccessAtUtc: "2026-10-25T12:00:00Z",
    lastAttemptAtUtc: "2026-10-25T12:00:00Z",
    warning: null,
    listings: [],
  };
  const search = vi.fn((city: string) =>
    city === "Leeds"
      ? Promise.resolve(result)
      : new Promise<LocationResult>(() => {}),
  );
  render(<SearchPage load={load} search={search} />);
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "Search cities" })).toBeEnabled(),
  );
  const user = userEvent.setup();
  await user.clear(screen.getByLabelText("Cities to search"));
  await user.type(screen.getByLabelText("Cities to search"), "Leeds, London");
  await user.click(screen.getByRole("button", { name: "Search cities" }));
  expect(await screen.findByText("Saved results")).toBeInTheDocument();
  expect(screen.getByText("Searching…")).toBeInTheDocument();
});

test("rejects more than eight cities without sending requests", async () => {
  const search = vi.fn();
  render(<SearchPage load={load} search={search} />);
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "Search cities" })).toBeEnabled(),
  );
  const user = userEvent.setup();
  await user.clear(screen.getByLabelText("Cities to search"));
  await user.type(
    screen.getByLabelText("Cities to search"),
    "One, Two, Three, Four, Five, Six, Seven, Eight, Nine",
  );
  await user.click(screen.getByRole("button", { name: "Search cities" }));
  expect(screen.getByRole("alert")).toHaveTextContent("one to eight");
  expect(search).not.toHaveBeenCalled();
});

test("a late result from a superseded search does not overwrite the current report", async () => {
  let finishOld: (value: LocationResult) => void = () => {};
  const old = new Promise<LocationResult>((resolve) => {
    finishOld = resolve;
  });
  const result: LocationResult = {
    location: "bristol",
    status: "cached",
    lastSuccessAtUtc: null,
    lastAttemptAtUtc: null,
    warning: null,
    listings: [],
  };
  const search = vi.fn((city: string) =>
    city === "Leeds" ? old : Promise.resolve(result),
  );
  render(<SearchPage load={load} search={search} />);
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "Search cities" })).toBeEnabled(),
  );
  const user = userEvent.setup();
  const field = screen.getByLabelText("Cities to search");
  await user.clear(field);
  await user.type(field, "Leeds");
  await user.click(screen.getByRole("button", { name: "Search cities" }));
  await user.clear(field);
  await user.type(field, "Bristol");
  await user.click(screen.getByRole("button", { name: "Search cities" }));
  expect(
    await screen.findByRole("heading", { name: "Bristol" }),
  ).toBeInTheDocument();
  const { act } = await import("@testing-library/react");
  await act(async () => finishOld({ ...result, location: "leeds" }));
  expect(
    screen.queryByRole("heading", { name: "Leeds" }),
  ).not.toBeInTheDocument();
});

test("cards and new-only filter preserve returned versus new classifications", async () => {
  const listing = {
    id: "10000000-0000-4000-8000-000000000001",
    details: {
      name: "Alpha",
      address: "1 High Street",
      phone: null,
      website: null,
      contactUrl: null,
      sourceUrl: "https://www.solicitors.com/alpha.html",
    },
    isActive: true,
    discovery: "new" as const,
    firstSeenAtUtc: "2026-10-25T12:00:00Z",
    lastSeenAtUtc: "2026-10-25T12:00:00Z",
    inactivatedAtUtc: null,
  };
  const result: LocationResult = {
    location: "leeds",
    status: "refreshed",
    lastSuccessAtUtc: "2026-10-25T12:00:00Z",
    lastAttemptAtUtc: "2026-10-25T12:00:00Z",
    warning: null,
    listings: [
      listing,
      {
        ...listing,
        id: "10000000-0000-4000-8000-000000000002",
        details: { ...listing.details, name: "Beta" },
        discovery: "returned",
      },
    ],
  };
  render(<SearchPage load={load} search={() => Promise.resolve(result)} />);
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "Search cities" })).toBeEnabled(),
  );
  const user = userEvent.setup();
  await user.clear(screen.getByLabelText("Cities to search"));
  await user.type(screen.getByLabelText("Cities to search"), "Leeds");
  await user.click(screen.getByRole("button", { name: "Search cities" }));
  expect(
    await screen.findByRole("heading", { name: "Beta" }),
  ).toBeInTheDocument();
  await user.click(screen.getByLabelText("New listings only"));
  expect(screen.getByRole("heading", { name: "Alpha" })).toBeInTheDocument();
  expect(
    screen.queryByRole("heading", { name: "Beta" }),
  ).not.toBeInTheDocument();
});

test("unsupported city is rejected without a search request", async () => {
  const search = vi.fn();
  render(<SearchPage load={load} search={search} />);
  await waitFor(() =>
    expect(screen.getByRole("button", { name: "Search cities" })).toBeEnabled(),
  );
  const user = userEvent.setup();
  await user.clear(screen.getByLabelText("Cities to search"));
  await user.type(screen.getByLabelText("Cities to search"), "Banana");
  await user.click(screen.getByRole("button", { name: "Search cities" }));
  expect(screen.getByRole("alert")).toHaveTextContent("Unsupported city");
  expect(search).not.toHaveBeenCalled();
});
test("catalogue load failure supports retry", async () => {
  const loader = vi
    .fn()
    .mockRejectedValueOnce(new Error("offline"))
    .mockResolvedValueOnce([{ key: "york", displayName: "York" }]);
  render(<SearchPage load={loader} />);
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "could not be loaded",
  );
  expect(screen.getByRole("button", { name: "Search cities" })).toBeDisabled();
  await userEvent.click(screen.getByRole("button", { name: "Retry cities" }));
  await waitFor(() =>
    expect(screen.getByLabelText("Cities to search")).toHaveValue("York"),
  );
});
