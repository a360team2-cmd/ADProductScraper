# AD Product Scraper

A local .NET 9 + Angular 21 product collector. Paste a search-results URL, follow discoverable pagination, enrich products from their detail pages, and download **Excel (.xlsx) only**.

## VS Code + Visual Studio (recommended development workflow)

Read **[DEVELOPMENT-SETUP.md](backend/docs/DEVELOPMENT-SETUP.md)** for complete Roman Urdu instructions.

- **Visual Studio:** open `backend/AD Product Scraper.sln`, select `ADProductScraper.Api`, press F5. API: `http://localhost:5080/api/health`.
- **VS Code:** open `frontend`, run `npm.cmd ci` once, then `npm.cmd start`. Website: `http://localhost:4200`.
- The Angular development server proxies `/api` to the backend. Both applications must remain running.
- Stop any old Run.cmd server before starting the backend to free port 5080.
- Larger 14–16px normal text, larger controls and table rows are included; use 100% browser zoom.

## Jaldi start karein

1. .NET 9 ASP.NET Core Runtime ya .NET 9 SDK install ho.
2. **`Run.cmd` double-click karein.** Ready-to-run build included hai; Node.js ki zaroorat nahin.
3. Browser mein `http://127.0.0.1:5080` kholein.
4. **Try a demo** se sample collection check karein, ya website ka search-results link paste karein.
5. **Collect products** click karein. **Download Excel** se `.xlsx` milegi.

Server window khuli rakhein; Ctrl+C se band hota hai. Source se rebuild karna ho to .NET 9 SDK + Node.js 22.12+ (22.x) ke saath `Start.cmd` chalayein; pehli build ke liye internet chahiye. `Run.cmd` packaged build chalata hai; source changes check karne ke liye `Start.cmd` use karein.

## What works

- Automatic extraction from Product/Offer/ItemList JSON-LD and common HTML cards.
- HTML selector presets for Amazon, Etsy and eBay, plus generic store cards and optional custom CSS selectors.
- Automatic `next` links, relative URL resolution, duplicate removal, and loop detection.
- Product detail visits for published material, seller, price and image URLs.
- Live counts, 50-row preview pages, queued collections, cancellation, saved history and resume.
- Atomic local checkpoints in `data` beside the running application (`backend/runtime/data` for Run.cmd, `backend/data` for source mode); interrupted jobs can resume after a restart.
- Excel has **Products**, **All image links**, and **Read me** sheets, with filters and frozen headers.
- Columns: title, material, store name, price, currency, product URL, source page, and image URL columns.
- Prices remain source text, retaining symbols/ranges. They are not normalized or currency-converted.
- Unavailable values stay blank. Brands are not silently substituted for seller names.
- Excel output keeps scraped text as text, never as spreadsheet formulas.

## Coverage and limitations — please read

This is a working **HTML-based collector**, not a guaranteed universal marketplace scraper. It does not execute page JavaScript or solve CAPTCHA, log in, bypass access challenges, or use a proxy service. Amazon, Etsy and eBay often restrict automated traffic. Their presets have been tested against representative HTML fixtures, **not certified against live marketplace pages**. Store layouts can change; use custom selectors or add an authorized API integration when required. No marketplace API credentials are included.

“All discoverable pages” means pages reachable through an actual next-page link in accessible HTML. Infinite scroll, JavaScript buttons and server-hidden results are not included. A single-product URL collects that product; it cannot enumerate an entire store. No keyword search across all marketplaces is implemented: search on the chosen site first and paste its resulting URL.

Access errors stop collection and retain partial data. Download Excel is available as soon as at least one product is collected. Resume retries the unfinished page, deduplicating existing products. Product detail visits are slower (at least 1.5 seconds between live requests) but improve material/store coverage. A user-selected page limit or 100,000-product safety limit makes the result partial. Jobs run one at a time, with up to 10 waiting. There is no login/authentication or multi-user deployment setup; this app binds to localhost only.

Robots rules are checked before page collection. Public HTTP/HTTPS addresses on standard ports are allowed; local/private network addresses and cross-site navigation are blocked. DNS checks happen at socket connection time. Pages are limited to 8 MB and requests to 35 seconds.

Demo mode uses **synthetic, local HTML fixtures**, not live Etsy/Amazon/eBay data. Sample image links are placeholders. Internal progress is stored as JSON; the user-facing download is exclusively `.xlsx`.

## Developer commands

```powershell
# Backend
dotnet run --project backend/ADProductScraper.Api.csproj --no-launch-profile

# Angular development server, separate terminal
cd frontend
npm ci
npm start
# Open http://localhost:4200 (API requests are proxied to :5080)

# Regression checks (from project root)
dotnet run --project backend/tests/ADProductScraper.Tests.csproj

# With backend running: pagination, cancellation/resume and Excel API checks
powershell -ExecutionPolicy Bypass -File backend/tests/Smoke.ps1
```

The production Angular build is copied to `backend/wwwroot` by Start.ps1 and served by .NET on port 5080. Keep `package-lock.json` for reproducible installs.

## Project map

- `backend/Extractor.cs`: structured data, marketplace selectors, detail parsing and link normalization.
- `backend/SafeWeb.cs`: bounded HTTP fetches, connection-time address validation and robots matching.
- `backend/Jobs.cs`: single-worker queue, pagination, progress, checkpoint/resume.
- `backend/ExcelExport.cs`: `.xlsx` generation using ClosedXML.
- `frontend/src/main.ts`: Angular collection dashboard.
- `backend/tests/Program.cs`: executable regression suite, including Excel round-trip verification.

## Reference documentation

- Angular compatibility: https://angular.dev/reference/versions
- AngleSharp: https://github.com/AngleSharp/AngleSharp
- ClosedXML: https://docs.closedxml.io/
- eBay Browse API (possible future authorized integration): https://developer.ebay.com/api-docs/buy/browse/overview.html

## Folder structure

- `frontend/`: Angular website source, configuration and frontend tests.
- `backend/`: .NET API source and configuration.
- `backend/tests/`: regression tests and optional diagnostics (including saved diagnostic exports).
- `backend/runtime/`: packaged application used by `Run.cmd`; its DLLs are required dependencies.
- `backend/docs/`: detailed setup, feature and verification notes.
- Root: README and startup scripts; the Visual Studio solution is inside `backend/`.

Run commands from this folder. `Run.cmd` starts the packaged app. `Start.cmd` installs frontend dependencies, builds the current source and starts it. For separate development servers, run `dotnet run --project backend/ADProductScraper.Api.csproj` and run `npm ci` then `npm start` inside `frontend`.