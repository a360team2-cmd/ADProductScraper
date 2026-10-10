# AD Product Scraper

A local product collection application built with Angular and ASP.NET Core. Collect product information from public ecommerce pages, review results, and export structured Excel workbooks.

## Features

- Extract product titles, prices, currencies, materials, seller names, product URLs, and image links.
- Parse structured product data and HTML cards with marketplace presets and custom CSS selectors.
- Follow discoverable pagination, remove duplicate products, and optionally visit product detail pages.
- Render JavaScript pages through an optional browser collection mode.
- Monitor collection progress, cancel jobs, and resume interrupted collections from local checkpoints.
- Export `.xlsx` workbooks with product data, image links, filters, and frozen headers.
- Try the workflow with synthetic local demo data before collecting live pages.

## Technology stack

| Layer | Technologies |
| --- | --- |
| Frontend | Angular 21, TypeScript, CSS |
| Backend | .NET 9, ASP.NET Core Minimal APIs |
| HTML parsing | AngleSharp |
| Browser automation | Microsoft Playwright |
| Excel export | ClosedXML |
| API documentation | Swagger / OpenAPI |
| Persistence | Local JSON checkpoints |

## Prerequisites

- .NET 9 SDK.
- Node.js 22.12 or later in the 22.x release line, with npm.
- Visual Studio with the ASP.NET and web development workload and .NET 9 support.
- Visual Studio Code for frontend development.
- Microsoft Edge or Google Chrome for browser collection mode.
- Internet access for the initial dependency installation and live collection.

## Local development

Run the backend in Visual Studio and the frontend in VS Code. Keep both processes running.

### 1. Start the backend in Visual Studio

1. Open `backend/AD Product Scraper.sln`.
2. Set `ADProductScraper.Api` as the startup project.
3. Select the `ADProductScraper.Api` launch profile.
4. Press **F5** to restore packages, build, and start the API.

Swagger: <http://localhost:5080/swagger>

Health check: <http://localhost:5080/api/health>

To start the same backend from the project root in a terminal:

```powershell
dotnet run --project backend/ADProductScraper.Api.csproj --launch-profile ADProductScraper.Api
```

### 2. Start the frontend in VS Code

Open the `frontend` folder in VS Code, then run:

```powershell
npm install
npx ng serve
```

Open <http://localhost:4200>.

Install dependencies on first setup and after dependency changes. On subsequent runs, use `npx ng serve`. The Angular development server proxies `/api` requests to the backend on port `5080`.

If PowerShell blocks npm or npx scripts, use `npm.cmd install` and `npx.cmd ng serve`. For a clean installation using the exact lockfile versions, use `npm ci`.

### Local addresses

| Service | Address |
| --- | --- |
| Angular application | <http://localhost:4200> |
| Backend health check | <http://localhost:5080/api/health> |
| Swagger UI | <http://localhost:5080/swagger> |
| OpenAPI document | <http://localhost:5080/swagger/v1/swagger.json> |

Swagger is enabled by the Development launch profile. Stop any server started by `Run.cmd` or `Start.cmd` before starting the backend in Visual Studio, since they use the same port.

## Collecting products

1. Open the frontend and paste a public product or collection URL.
2. Set a page limit and choose whether to visit product detail pages.
3. Enable browser mode when the website requires JavaScript rendering. Select a supported currency option if needed; the application does not perform exchange-rate conversion.
4. Start collection and review progress, warnings, and preview rows.
5. Download the Excel workbook when products are available. If collection stops, review the error and resume when appropriate.

Browser mode may open a separate browser window. Complete any supported manual verification yourself and keep that window open during collection.

## Project structure

```text
ADProductScraper/
|-- frontend/                  Angular source, configuration, and frontend tests
|-- backend/
|   |-- AD Product Scraper.sln Visual Studio solution
|   |-- *.cs                   API, extraction, collection, and export logic
|   |-- Properties/            Development launch profiles
|   |-- tests/                 Regression checks, fixtures, and diagnostics
|   |-- docs/                  Detailed setup and feature notes
|   |-- runtime/               Packaged application used by Run.cmd
|   `-- data/                  Local checkpoints created at runtime
|-- README.md
|-- Run.cmd                    Start the packaged application
|-- Start.cmd                  Build and run the current source
`-- Start.ps1                  Build and startup implementation
```

Build outputs, installed dependencies, and local job checkpoints are excluded from Git where configured in `.gitignore`.

## Build and alternate startup

From the project root on Windows:

```powershell
.\Start.cmd
```

This installs frontend dependencies using `npm ci`, builds Angular, copies the generated frontend into `backend/wwwroot`, builds the backend, and starts the application at <http://127.0.0.1:5080>.

To run the included packaged application:

```powershell
.\Run.cmd
```

The packaged application requires the .NET 9 ASP.NET Core Runtime or SDK. It runs the bundle in `backend/runtime`; source changes are picked up by the development workflow or a source build, rather than automatically updating that bundle.

## Verification

Run these commands from the project root.

```powershell
# Backend regression suite
dotnet run --project backend/tests/ADProductScraper.Tests.csproj

# API smoke checks, with the backend already running
powershell -ExecutionPolicy Bypass -File backend/tests/Smoke.ps1

# Frontend error-handling checks, after installing frontend dependencies
node frontend/tests/api-errors.mjs

# Frontend production build
cd frontend
npm run build
```

The regression suite covers extraction, pagination, deduplication, network address restrictions, and Excel output. Additional scenarios are documented in [verification notes](backend/docs/VERIFICATION.md).

## API overview

| Method | Endpoint | Purpose |
| --- | --- | --- |
| GET | `/api/health` | Check API availability |
| GET | `/api/jobs` | List collection jobs |
| POST | `/api/jobs` | Create a collection |
| GET | `/api/jobs/{id}` | Read job status |
| GET | `/api/jobs/{id}/products` | Read collected products; supports `skip` and `take` |
| POST | `/api/jobs/{id}/cancel` | Stop a collection |
| POST | `/api/jobs/{id}/resume` | Resume a collection |
| GET | `/api/jobs/{id}/excel` | Download an Excel workbook |

Use Swagger in Development for request schemas and interactive examples.

## Data and operational limits

- Source-mode checkpoints are stored in `backend/data`; packaged-mode checkpoints are stored in `backend/runtime/data`.
- Retain checkpoints if you need saved history or resume support. Keep an independent copy of important Excel exports.
- Missing product fields remain blank. Prices retain source information; selecting a website currency does not convert values locally.
- Excel exports protect collected text from being interpreted as formulas.
- Public HTTP and HTTPS collection is supported; private network targets are blocked.
- The application is intended for local use and does not provide user accounts or multi-user deployment controls.

## Collection limitations

Collection depends on accessible page content and supported pagination patterns. Marketplace presets and browser rendering do not guarantee compatibility with every website. Layout changes, access restrictions, login requirements, and unsupported infinite-scroll behavior can limit results.

The application does not automatically solve CAPTCHA or guarantee access to protected pages. Robots handling is configurable; access denials and collection warnings should be reviewed. Demo results are synthetic fixtures rather than live marketplace data. Collect only content you are authorized to access.

## Troubleshooting

| Problem | Action |
| --- | --- |
| Port `5080` is already in use | Stop another backend or a server started by the root scripts. |
| Frontend cannot reach the API | Confirm the backend is running and its health endpoint responds. |
| `ng` is not recognized | Run `npm install`, then use `npx ng serve` in `frontend`. |
| PowerShell blocks npm scripts | Use the `.cmd` commands shown in the development instructions. |
| Swagger does not appear | Select the `ADProductScraper.Api` Development launch profile. |
| A website returns no products or stops collection | Review job warnings, try browser mode or custom selectors, and confirm the page is accessible. |
| Changes do not appear when using `Run.cmd` | Run the current source through Visual Studio and VS Code, or use `Start.cmd`. |

## Additional documentation

- [Development setup](backend/docs/DEVELOPMENT-SETUP.md)
- [Separate frontend and backend workflow](backend/docs/NORMAL-RUN.md)
- [Browser collection](backend/docs/BROWSER-COLLECTION.md)
- [Error handling](backend/docs/ERROR-HANDLING.md)
- [Pagination verification](backend/docs/EBAY-PAGINATION-VERIFICATION.md)
