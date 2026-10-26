import { useCallback, useState } from "react";
import { loadLocations, searchCity } from "./api";
import type { SearchCity, SupportedLocation } from "./api";
import { MAX_CITIES, parseSelection } from "./cities";
import { CitySearchForm } from "./CitySearchForm";
import { SearchReport } from "./SearchReport";
import { useCitySearch } from "./useCitySearch";
import { useLocations } from "./useLocations";

export function SearchPage({
  search = searchCity,
  load = loadLocations,
}: {
  search?: SearchCity;
  load?: typeof loadLocations;
}) {
  const [input, setInput] = useState("");
  const [error, setError] = useState("");
  const selectDefaults = useCallback(
    (locations: SupportedLocation[]) =>
      setInput(
        locations
          .slice(0, MAX_CITIES)
          .map((x) => x.displayName)
          .join(", "),
      ),
    [],
  );
  const catalogue = useLocations(load, selectDefaults);
  const { states, run, start } = useCitySearch(search);
  function submit() {
    const selection = parseSelection(input, catalogue.locations);
    if (!selection.ok) {
      setError(selection.error);
      return;
    }
    setError("");
    void start(selection.cities);
  }
  return (
    <main>
      <header className="masthead">
        <a href="#" className="brand">
          Conveyancing<span>Search</span>
        </a>
        <span className="edition">SOLICITOR DIRECTORY · UK</span>
      </header>
      <section className="intro">
        <p className="eyebrow">YOUR NEXT CONVERSATION STARTS HERE</p>
        <h1>
          Find firms.
          <br />
          <span>Discover opportunities.</span>
        </h1>
        <p>
          Search conveyancing listings by city. Explore contact details and see
          what has changed since the last successful refresh.
        </p>
      </section>
      <section className="search-panel" aria-labelledby="search-heading">
        <div>
          <p className="eyebrow">01 / CHOOSE YOUR LOCATIONS</p>
          <h2 id="search-heading">Where would you like to look?</h2>
        </div>
        <CitySearchForm
          locations={catalogue.locations}
          catalogueError={catalogue.error}
          onRetryCatalogue={catalogue.retry}
          input={input}
          onInputChange={setInput}
          error={error}
          onError={setError}
          onSubmit={submit}
        />
      </section>
      {states.length > 0 ? (
        // A new run remounts the report, which resets its filters.
        <SearchReport key={run} states={states} />
      ) : (
        <section className="empty-start">
          <span aria-hidden="true">◎</span>
          <div>
            <h2>A clearer view of your market</h2>
            <p>
              Your city summaries and firm details will appear here as each
              search completes.
            </p>
          </div>
        </section>
      )}
      <footer>
        Source: solicitors.com · Listings reflect the source directory and may
        include firms serving an area from elsewhere.
      </footer>
    </main>
  );
}
