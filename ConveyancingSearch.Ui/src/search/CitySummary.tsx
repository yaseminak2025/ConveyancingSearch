import type { CityState } from "./useCitySearch";

const formatTime = (value: string) =>
  new Intl.DateTimeFormat("en-GB", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));

const statusLabels = {
  cached: "Saved results",
  refreshed: "Updated results",
  fallback: "Older saved results",
  unavailable: "Unavailable",
};

export function CitySummary({
  state,
  selected,
  onSelect,
}: {
  state: CityState;
  selected: boolean;
  onSelect: () => void;
}) {
  return (
    <article className="city-summary">
      <h3>{state.city}</h3>
      {state.phase === "loading" || state.phase === "queued" ? (
        <>
          <p className="summary-number">—</p>
          <p>
            {state.phase === "loading" ? "Searching…" : "Waiting to search…"}
          </p>
        </>
      ) : state.phase === "error" ? (
        <p role="alert" className="warning">
          {state.message}
        </p>
      ) : (
        <>
          <p className="summary-number">
            {state.result.status === "unavailable"
              ? "—"
              : state.result.listings.length}
          </p>
          <p>{statusLabels[state.result.status]}</p>
          {state.result.status !== "unavailable" && (
            <button
              type="button"
              className="summary-filter"
              aria-pressed={selected}
              onClick={onSelect}
            >
              View {state.city} listings
            </button>
          )}
          {state.result.lastSuccessAtUtc && (
            <small>
              Source checked {formatTime(state.result.lastSuccessAtUtc)}
            </small>
          )}
          {state.result.warning && (
            <p className="warning">{state.result.warning}</p>
          )}
        </>
      )}
    </article>
  );
}
