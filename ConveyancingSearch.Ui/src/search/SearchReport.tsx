import { useState } from "react";
import { CitySummary } from "./CitySummary";
import { FirmCard } from "./FirmCard";
import type { CityState } from "./useCitySearch";

/** Per-city summaries plus the filterable firm cards for one search run. */
export function SearchReport({ states }: { states: CityState[] }) {
  const [locationFilter, setLocationFilter] = useState("all");
  const [newOnly, setNewOnly] = useState(false);
  const completed = states.filter(
    (x) => x.phase === "done" || x.phase === "error",
  ).length;
  const results = states.flatMap((x) =>
    x.phase === "done" ? [{ city: x.city, result: x.result }] : [],
  );
  const listings = results
    .flatMap(({ city, result }) =>
      result.listings.map((listing) => ({ city, listing })),
    )
    .filter(
      (x) =>
        (locationFilter === "all" || locationFilter === x.city) &&
        (!newOnly || x.listing.discovery === "new"),
    );
  const count = results.reduce(
    (total, x) => total + x.result.listings.length,
    0,
  );
  return (
    <section aria-labelledby="report-heading" className="report">
      <div className="report-heading">
        <div>
          <p className="eyebrow">02 / YOUR SEARCH REPORT</p>
          <h2 id="report-heading">The latest picture</h2>
        </div>
        <p role="status">
          {completed} of {states.length} cities complete · {count} active
          listings
        </p>
      </div>
      <div className="city-grid">
        {states.map((state) => (
          <CitySummary
            key={state.city}
            state={state}
            selected={locationFilter === state.city}
            onSelect={() => setLocationFilter(state.city)}
          />
        ))}
      </div>
      <div className="filters">
        <div>
          <label htmlFor="location-filter">Show location</label>
          <select
            id="location-filter"
            value={locationFilter}
            onChange={(event) => setLocationFilter(event.target.value)}
          >
            <option value="all">All selected cities</option>
            {states.map((x) => (
              <option key={x.city}>{x.city}</option>
            ))}
          </select>
        </div>
        <label className="checkbox">
          <input
            type="checkbox"
            checked={newOnly}
            onChange={(event) => setNewOnly(event.target.checked)}
          />
          New listings only
        </label>
        <p>{listings.length} listings shown</p>
      </div>
      <p className="explanation">
        “New” means first discovered in the latest successful collection, not a
        newly established firm. All listings in an initial collection are marked
        New.
      </p>
      <div className="cards">
        {listings.map(({ city, listing }) => (
          <FirmCard key={city + listing.id} city={city} listing={listing} />
        ))}
      </div>
      {completed === states.length && listings.length === 0 && (
        <p className="empty">
          No listings to display for these results and filters. Check the city
          statuses above.
        </p>
      )}
    </section>
  );
}
