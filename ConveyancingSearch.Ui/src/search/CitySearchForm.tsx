import { useState } from "react";
import type { FormEvent } from "react";
import type { SupportedLocation } from "./api";
import { MAX_CITIES, resolveCity, splitCities } from "./cities";

export function CitySearchForm({
  locations,
  catalogueError,
  onRetryCatalogue,
  input,
  onInputChange,
  error,
  onError,
  onSubmit,
}: {
  locations: SupportedLocation[];
  catalogueError: string;
  onRetryCatalogue: () => void;
  input: string;
  onInputChange: (value: string) => void;
  error: string;
  onError: (message: string) => void;
  onSubmit: () => void;
}) {
  const [choice, setChoice] = useState("");
  const loading = locations.length === 0;
  function addCity() {
    const city = resolveCity(locations, choice);
    if (!city) {
      onError("Select a supported city.");
      return;
    }
    const selected = splitCities(input);
    if (
      selected.some((x) => x.toLowerCase() === city.displayName.toLowerCase())
    ) {
      setChoice("");
      return;
    }
    if (selected.length >= MAX_CITIES) {
      onError("Select no more than eight cities.");
      return;
    }
    onInputChange([...selected, city.displayName].join(", "));
    setChoice("");
    onError("");
  }
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    onSubmit();
  }
  return (
    <form onSubmit={submit}>
      {catalogueError ? (
        <p role="alert">
          {catalogueError}{" "}
          <button type="button" onClick={onRetryCatalogue}>
            Retry cities
          </button>
        </p>
      ) : loading ? (
        <p role="status">Loading supported cities…</p>
      ) : null}
      <label htmlFor="supported-city">Find a supported city</label>
      <input
        id="supported-city"
        list="supported-cities"
        value={choice}
        onChange={(event) => setChoice(event.target.value)}
        disabled={loading}
      />
      <datalist id="supported-cities">
        {locations.map((city) => (
          <option key={city.key} value={city.displayName} />
        ))}
      </datalist>
      <button type="button" disabled={loading} onClick={addCity}>
        Add city
      </button>
      <label htmlFor="cities">Cities to search</label>
      <textarea
        id="cities"
        rows={2}
        value={input}
        onChange={(event) => onInputChange(event.target.value)}
        aria-describedby={error ? "cities-help cities-error" : "cities-help"}
        aria-invalid={Boolean(error)}
      />
      <div className="form-bottom">
        <p id="cities-help">
          Up to 8 cities, separated by commas. Results checked within 24 hours
          are reused.
        </p>
        <button className="primary" type="submit" disabled={loading}>
          Search cities <span aria-hidden="true">↗</span>
        </button>
      </div>
      {error && (
        <p id="cities-error" role="alert" className="warning">
          {error}
        </p>
      )}
    </form>
  );
}
