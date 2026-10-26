# Conveyancing Search

A .NET 10 and React/TypeScript application that collects conveyancing firm listings from Solicitors.com, stores them in SQL Server, and reuses results for 24 hours.

Users can select one to eight supported cities. The UI sends an independent request for each city and displays results as they become available.

See [`SETUP.md`](SETUP.md) for database, API and UI setup. See [`ASSUMPTIONS.md`](ASSUMPTIONS.md) for scope and behavioural decisions.

## Projects

| Project | Responsibility |
|---|---|
| `ConveyancingSearch.Domain` | Listing models, location identity and pure reconciliation rules. No project dependencies. |
| `ConveyancingSearch.Application` | Search orchestration, supported-location validation, freshness checks, shared in-flight searches and fallback handling. Defines `IListingSource` and `ILocationStore`. |
| `ConveyancingSearch.Service` | Solicitors.com HTTP access, source-specific HTML parsing, directory traversal and completeness validation. Implements `IListingSource`. |
| `ConveyancingSearch.Data` | SQL Server persistence using `Microsoft.Data.SqlClient`, parameterised SQL and transactions. Implements `ILocationStore`. |
| `ConveyancingSearch.Api` | Minimal API endpoints, HTTP validation, error responses and dependency registration. |
| `ConveyancingSearch.Ui` | Standalone React/TypeScript/Vite frontend. Communicates with the API over HTTP and validates response shapes with Zod. |
| `ConveyancingSearch.Tests` | Reconciliation, orchestration, catalogue, parser, crawler and HTTP integration tests, plus opt-in SQL and live-source tests. |

`Service` and `Data` are independent infrastructure adapters behind Application interfaces. Neither depends on the other.

The store owns the transactional read–reconcile–write operation. Reconciliation rules remain pure, and website requests happen outside database transactions.

## Configuration

The API uses these settings in `ConveyancingSearch.Api/appsettings.json`:

```json
{
  "Source": {
    "ParallelDownloads": 4,
    "MaxPages": 512,
    "RequestTimeoutSeconds": 20
  },
  "Search": {
    "FreshnessHours": 24,
    "RefreshTimeoutSeconds": 300
  }
}
```

These limits are validated at startup. Options classes provide defaults; the configuration does not require every key to be explicitly supplied.

Saved results are reused for `FreshnessHours` (24 by default). The UI permits up to eight cities per submission and runs up to three city requests concurrently.

The source host is fixed to `https://www.solicitors.com`. Redirects are restricted to the expected host and permitted location paths.

The database connection-string key is:

```text
ConnectionStrings:Conveyancing
```

In Development it comes from `appsettings.Development.json`, which points at a local SQL Server Express instance (`Server=.\SQLEXPRESS`) using Windows authentication. `appsettings.json` contains no connection string, so any other environment must supply one, for example with an environment variable:

```powershell
$env:ConnectionStrings__Conveyancing = 'Server=.\SQLEXPRESS;Database=ConveyancingSearch;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
```

The SQL schema is supplied in `database/001-schema.sql`. This implementation uses `dbo.Locations` and `dbo.Listings`; it does not use EF Core migrations.

## Supported locations

`ConveyancingSearch.Api/supported-locations.json` is the authoritative catalogue. Initially it contains:

- London
- Birmingham
- Leeds
- Manchester
- Sheffield
- Bradford
- Liverpool
- Bristol

Each entry defines a stable key, display name and source path:

```json
{
  "key": "leeds",
  "displayName": "Leeds",
  "sourcePath": "/conveyancing+leeds.html"
}
```

To expand coverage, add a verified entry and restart the API. The catalogue can contain more than eight locations; the eight-city limit applies to each UI submission.

Unsupported names return HTTP `400` before database or website access. Case and surrounding whitespace are normalised.

The UI obtains available locations from:

```http
GET /api/locations
```

## Run locally

Requires the .NET 10 SDK, Node.js/npm and a configured SQL Server database.

From the directory containing `ConveyancingSearch.slnx`:

```powershell
dotnet run --project .\ConveyancingSearch.Api
```

The included launch profile listens on:

```text
http://127.0.0.1:5080
```

In a second terminal:

```powershell
Set-Location .\ConveyancingSearch.Ui
npm ci
npm run dev
```

Open the URL printed by Vite, normally:

```text
http://127.0.0.1:5173
```

Vite proxies `/api` requests to the backend on port `5080`. This development workflow does not require cross-origin requests from the browser.

Alternatively, run `npm run build` in `ConveyancingSearch.Ui`: it writes the UI into `ConveyancingSearch.Api/wwwroot`, and the API then serves it at `http://127.0.0.1:5080` without Vite. Publishing is covered in `SETUP.md`.

`GET /health` checks that the process is running. It does not verify database connectivity or schema readiness.

## Search API

Each request searches one supported location:

```http
POST http://127.0.0.1:5080/api/search
Content-Type: application/json

{
  "location": "Leeds"
}
```

The UI coordinates multiple independent requests when more than one city is selected.

An illustrative response is:

```json
{
  "location": "leeds",
  "status": "cached",
  "lastSuccessAtUtc": "2026-10-26T09:00:00Z",
  "lastAttemptAtUtc": "2026-10-26T09:00:00Z",
  "warning": null,
  "listings": [],
  "failureCode": null
}
```

| Status | Meaning | HTTP status |
|---|---|---|
| `cached` | Saved results are younger than the freshness window (24 hours by default). | `200` |
| `refreshed` | A complete collection was successfully saved. | `200` |
| `fallback` | Refresh failed; the last successfully saved results are returned. | `200` |
| `unavailable` | No usable saved results are available after a failure. | `503` |

Invalid input returns `400` with a location validation error.

Expected source and database failures have distinct failure codes and warnings. Unexpected programming errors return a controlled `500` Problem Details response with a trace identifier; internal exception details remain in server logs.

See `EndPoints.http` for example requests.

## Freshness, concurrency and reconciliation

Saved results are reused while they are younger than `Search:FreshnessHours` (24 by default). At exactly that age, a new collection is required.

Concurrent searches for the same city share one in-flight refresh within a single API instance. Cancelling one caller stops that caller’s wait without cancelling the shared refresh. The refresh has its own deadline and responds to application shutdown.

Listings are identified by normalised firm name and address within a location:

- First collection: all listings are marked `new`.
- Newly discovered listing: marked `new`.
- Previously inactive listing seen again: marked `returned`.
- Continuing listing: marked `existing`.
- Listing missing from a complete refresh: marked inactive and retained.

An office that moves may be represented as an inactive old listing and a new listing at the new address.

The API returns active listings. Inactive records remain in the database.

Database updates use a per-location SQL application lock and a transaction. Failed refreshes do not replace the last successful collection with partial or unsaved results.

## Source collection and parsing

The application uses a source-specific HTML tokenizer and parser without a third-party HTML scraping library.

Collection validates page structure, location headings, listing identities and expected result counts. Required directory pages must complete successfully before the collection is accepted.

An empty extraction alone is not treated as proof that a location has no firms. Unrecognised, incomplete or unexpectedly empty responses fail the refresh.

Limits bound individual request duration, overall refresh duration, concurrent downloads, response size, nesting depth and page count.

The source may provide a contact-form link rather than an email address. The application exposes that link as `contactUrl`; it does not manufacture an email address.

## Tests

From the solution directory:

```powershell
dotnet test .\ConveyancingSearch.slnx
```

From `ConveyancingSearch.Ui`:

```powershell
npm test
npm run build
```

Latest local run: 43 backend tests, 24 frontend tests and the Playwright browser test (real Chromium) passed, along with TypeScript checking, the Vite production build and the Prettier check.

HTTP tests use `WebApplicationFactory` with Production hosting and controlled source/store substitutes. They cover validation, catalogue responses, refresh, cache reuse, fallback, unavailable results, unexpected errors and caller cancellation.

SQL and live-source tests are opt-in (see `SETUP.md`) and were skipped in that run. HTTP tests with substitutes do not verify real SQL transaction behaviour.

## Known limitations

- London fails: a required link in the source's London directory returns `404`. Rather than save an incomplete collection, the app reports London as unavailable.
- Sharing of an in-progress refresh works within one API instance. Running several instances would need a distributed lock or queue.
- If solicitors.com changes its page structure, refreshes are rejected rather than saving partial data; the parser then needs updating.

## AI assistance
AI tools were used to support parts of this project, including code
generation, refactoring, debugging, documentation, and architectural
discussions.

All design decisions, implementation choices, and final code were
reviewed and validated by the author.

AI tool used: Claude (various models, selected based on the task).
