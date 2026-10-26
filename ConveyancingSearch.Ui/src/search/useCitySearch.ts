import { useEffect, useRef, useState } from "react";
import type { LocationResult, SearchCity } from "./api";

export type CityState = { city: string } & (
  | { phase: "queued" }
  | { phase: "loading" }
  | { phase: "done"; result: LocationResult }
  | { phase: "error"; message: string }
);

const CONCURRENT_REQUESTS = 3;

/**
 * Runs one request per city, at most three at a time. Starting a new run
 * cancels the previous one, and late results from it are ignored.
 */
export function useCitySearch(search: SearchCity) {
  const [states, setStates] = useState<CityState[]>([]);
  const [run, setRun] = useState(0);
  const active = useRef<AbortController | null>(null);
  const generation = useRef(0);
  useEffect(
    () => () => {
      generation.current++;
      active.current?.abort();
    },
    [],
  );
  async function start(cities: string[]) {
    active.current?.abort();
    const controller = new AbortController();
    active.current = controller;
    const current = ++generation.current;
    setRun(current);
    setStates(cities.map((city) => ({ city, phase: "queued" })));
    const queue = [...cities];
    const update = (city: string, state: CityState) => {
      if (generation.current === current)
        setStates((previous) =>
          previous.map((x) => (x.city === city ? state : x)),
        );
    };
    async function worker() {
      while (!controller.signal.aborted) {
        const city = queue.shift();
        if (!city) return;
        update(city, { city, phase: "loading" });
        try {
          const result = await search(city, controller.signal);
          update(city, { city, phase: "done", result });
        } catch (cause) {
          if (!controller.signal.aborted)
            update(city, {
              city,
              phase: "error",
              message:
                cause instanceof Error
                  ? cause.message
                  : "Search could not be completed.",
            });
        }
      }
    }
    await Promise.all(
      Array.from({ length: Math.min(CONCURRENT_REQUESTS, cities.length) }, () =>
        worker(),
      ),
    );
  }
  return { states, run, start };
}
