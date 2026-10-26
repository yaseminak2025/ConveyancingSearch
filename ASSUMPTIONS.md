# Assumptions and trade-offs

These are the decisions I made where the brief left room for interpretation.

## Searching

- You can search 1 to 8 cities at once. Each city is a separate request, so one slow or failing city doesn't hold up the others.
- The browser runs up to 3 city requests at a time, and the backend makes at most 4 downloads from solicitors.com at a time across all searches. These are sensible defaults, not limits set by the source site, and both can be changed.
- Only cities in `supported-locations.json` can be searched. Anything else is rejected before the database or website is touched.
- If several people search the same city at the same time, they share one scrape. Cancelling your own search only stops your wait; the scrape carries on and is saved for next time.

## When results are refreshed

- Saved results for a city are reused for 24 hours after the last successful scrape. At exactly 24 hours they count as stale and the next search scrapes again. The window is configurable (`Search:FreshnessHours`).
- Viewing cached results or a failed scrape doesn't reset the 24 hours.
- If a refresh fails, you see the last saved results with a warning. If nothing has been saved yet, the city shows as unavailable.

## Identifying a firm

- A listing is identified by its name plus address within a city. Before comparing, I normalise case and whitespace but keep punctuation. There is no fuzzy matching.
- This means that if a firm changes its name or address, the old listing is marked inactive and a new one appears.
- Addresses are stored exactly as the site shows them. Some contain marketing text; I don't try to clean that up.
- When the same firm appears more than once, I merge the entries and take the first non-empty value for each field.

## New, Returned and Existing

- On a city's first scrape, every listing is **New**.
- On later scrapes:
  - **New:** never seen before.
  - **Returned:** previously disappeared and now back.
  - **Existing:** still listed.
- "New" means new to this app, not a newly established firm.

## Listings that disappear

- A listing is only marked inactive after a complete, successful scrape no longer includes it. Nothing is ever deleted.
- The UI shows active listings only; inactive ones stay in the database.
- A returning listing keeps its original ID and first-seen date.
- There's no full history table: only the current state and the latest attempt are stored.

## Never saving a partial scrape

- If any page fails, or the results look incomplete (a count mismatch, unknown pagination, a missing name or address), the whole scrape is rejected and the previously saved data stays as it was.
- A scrape that returns zero firms is also treated as a failure, because I never saw a real "no results" page from the site and can't tell a genuinely empty city from a broken page.

## London

- London is scraped through its directory of sub-areas, and every sub-area must succeed. I don't substitute Central or Greater London.
- **London currently fails:** one directory link (`stjamess`) returns 404 after about 100 seconds. Rather than silently skip it, the app reports London as unavailable. Fixing this needs either a verified replacement link or an agreed relaxation of the completeness rule.

## Database

- The scrape happens first. The database is then locked per city, and the read, comparison and write run in one transaction. No network calls are made while the lock is held.
- Only one backend instance is supported.

## Out of scope

Authentication, hosted deployment, a "force refresh" button, scheduled scraping, background jobs, ranking by rating, and data from other websites. This is a local assignment and shouldn't be exposed publicly as it stands.
