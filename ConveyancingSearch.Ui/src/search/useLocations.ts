import { useEffect, useState } from "react";
import type { SupportedLocation, loadLocations } from "./api";

/**
 * Loads the supported-city catalogue, with a retry after failure.
 * `onLoaded` runs in the same update as the catalogue state, so callers can
 * derive state from it without a render in between. Pass a stable callback.
 */
export function useLocations(
  load: typeof loadLocations,
  onLoaded: (locations: SupportedLocation[]) => void,
) {
  const [locations, setLocations] = useState<SupportedLocation[]>([]);
  const [error, setError] = useState("");
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setError("");
    void load(controller.signal)
      .then((items) => {
        if (controller.signal.aborted) return;
        setLocations(items);
        onLoaded(items);
      })
      .catch(() => {
        if (!controller.signal.aborted)
          setError("Supported cities could not be loaded. Please retry.");
      });
    return () => controller.abort();
  }, [load, onLoaded, attempt]);
  return { locations, error, retry: () => setAttempt((x) => x + 1) };
}
