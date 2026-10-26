# Setup

## Prerequisites

- .NET 10 SDK
- Node.js 22 or later, with npm
- SQL Server Express running locally (the default instance name `SQLEXPRESS` is assumed)
- SQL Server Management Studio (SSMS) or `sqlcmd`
- Visual Studio with .NET 10 support, or VS Code / any terminal

All commands below start from the folder that contains `ConveyancingSearch.slnx`.

## 1. Create the database

In SSMS, connect to `.\SQLEXPRESS` and run:

```sql
CREATE DATABASE ConveyancingSearch;
```

Then select the `ConveyancingSearch` database and run `database/001-schema.sql`. It creates the two tables. It is safe to run again; it skips tables that already exist.

Or with `sqlcmd`:

```powershell
sqlcmd -S '.\SQLEXPRESS' -E -C -Q 'CREATE DATABASE ConveyancingSearch;'
sqlcmd -S '.\SQLEXPRESS' -E -C -d ConveyancingSearch -i '.\database\001-schema.sql'
```

`-C` (trust the server certificate) needs sqlcmd 18 or later. With an older sqlcmd, leave it out.

No seed data is needed: the first successful search for a city fills it in.

## 2. Start the API

The Development connection string is in `ConveyancingSearch.Api/appsettings.Development.json`:

```text
Server=.\SQLEXPRESS;Database=ConveyancingSearch;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5
```

If your instance has a different name, edit that file, or set the `ConnectionStrings__Conveyancing` environment variable, which takes priority.

**Visual Studio:** open `ConveyancingSearch.slnx`, set `ConveyancingSearch.Api` as the startup project and press F5.

**Terminal:**

```powershell
dotnet run --project .\ConveyancingSearch.Api
```

Both use the included launch profile: Development mode on `http://127.0.0.1:5080`. Check `http://127.0.0.1:5080/health` returns `{"status":"ok"}`. This only shows the process is running; it does not check the database.

## 3. Start the UI

In a second terminal:

```powershell
Push-Location .\ConveyancingSearch.Ui
npm ci
npm run dev
Pop-Location
```

Open `http://127.0.0.1:5173`. Vite forwards `/api` calls to the API on port 5080, so both must be running.

Alternatively, run `npm run build` in `ConveyancingSearch.Ui`. It writes the UI into `ConveyancingSearch.Api/wwwroot`, and the API then serves it at `http://127.0.0.1:5080` without Vite.

## 4. Check it works

The search box starts with all eight supported cities. For a quick first check, replace them with `Leeds` and search.

- The first search scrapes solicitors.com live and takes a few seconds. Leeds should show around 70 firms, all marked **New**.
- Searching Leeds again shows **Saved results** and returns immediately, because results are reused for 24 hours.

London currently fails after about 100 seconds and shows as unavailable (see Known limitations in `README.md`), so leave it out of the first check.

## 5. Run the tests

```powershell
dotnet test .\ConveyancingSearch.slnx
Push-Location .\ConveyancingSearch.Ui
npm test
npm run build
npm run format:check
Pop-Location
```

The SQL and live-website tests are skipped by default.

**SQL integration test.** Create a separate, disposable database called `ConveyancingSearchTests`, run `database/001-schema.sql` against it, then:

```powershell
$env:CONVEYANCING_TEST_SQL = 'Server=.\SQLEXPRESS;Database=ConveyancingSearchTests;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
dotnet test .\ConveyancingSearch.slnx --filter FullyQualifiedName~SqlStoreTests
Remove-Item Env:CONVEYANCING_TEST_SQL
```

It creates its own test cities and removes them afterwards. Never point it at a database you care about.

**Live website test.** This makes real requests to solicitors.com:

```powershell
$env:CONVEYANCING_LIVE_TEST = '1'
$env:CONVEYANCING_LIVE_CITY = 'Leeds'
dotnet test .\ConveyancingSearch.slnx --filter FullyQualifiedName~LiveSourceTests --logger 'console;verbosity=detailed'
Remove-Item Env:CONVEYANCING_LIVE_TEST, Env:CONVEYANCING_LIVE_CITY
```

Avoid London here: it issues hundreds of requests.

**Browser test.** This starts Vite and replaces the API with test data, so the API and database aren't needed:

```powershell
Push-Location .\ConveyancingSearch.Ui
npx playwright install chromium
npm run test:browser
Pop-Location
```

## 6. Publish as one application

```powershell
Push-Location .\ConveyancingSearch.Ui
npm ci
npm run build
Pop-Location
dotnet publish .\ConveyancingSearch.Api -c Release -o .\artifacts\publish
Push-Location .\artifacts\publish
$env:ConnectionStrings__Conveyancing = 'Server=.\SQLEXPRESS;Database=ConveyancingSearch;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5'
dotnet ConveyancingSearch.Api.dll --urls http://127.0.0.1:5080
```

Then open `http://127.0.0.1:5080`. A published build runs in Production mode, which doesn't read `appsettings.Development.json`, so the connection string must come from the environment variable. Build the UI before publishing, so that `wwwroot` is included.

`TrustServerCertificate=True` is only appropriate for a local instance.

## Troubleshooting

- **Every city shows unavailable:** the API can't reach the database. Check that SQL Express is running, the instance name matches the connection string, and the tables exist. The API log names the failure.
- **The UI shows a proxy error:** the API isn't running on `http://127.0.0.1:5080`.
- **The SDK or IDE can't target .NET 10:** run `dotnet --list-sdks` and check the IDE version.
- **The API crashes on start with `DirectoryNotFoundException ... wwwroot`:** an old build output is left over. Run **Build → Rebuild Solution** in Visual Studio, or `dotnet clean` then build again.
- **Timeouts and limits:** these are in `ConveyancingSearch.Api/appsettings.json` under `Search` and `Source`. Invalid values stop the API at startup with a message.
