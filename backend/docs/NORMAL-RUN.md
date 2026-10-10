# Normal development setup

Project: `E:\Syed Naveed ul Hasan\CatalogHarbor-v2`

## Backend — Visual Studio

1. Open `backend/AD Product Scraper.sln`.
2. Select `ADProductScraper.Api` as the startup project and launch profile.
3. Press **F5** (or Ctrl+F5).
4. Swagger opens at **http://localhost:5080/swagger**.

Alternative terminal command, from the project root:

```powershell
dotnet run --project backend/ADProductScraper.Api.csproj --launch-profile ADProductScraper.Api
```

Swagger is available in the Development profile. Raw specification: `/swagger/v1/swagger.json`. Health check: `/api/health`.

## Frontend — VS Code

Open `frontend` as the VS Code folder. In its terminal:

```powershell
npm install
ng serve
```

`npm install` is needed on first setup or when dependencies change. Normal subsequent starts need only `ng serve`.

Website: **http://localhost:4200**.

If PowerShell blocks the ng.ps1 wrapper, use `ng.cmd serve` or choose a Command Prompt terminal. If ng is not installed globally, `npx ng serve` uses the project's installed Angular CLI.

## Connection

- Angular API requests go through `proxy.conf.json` to the backend at port 5080.
- Keep backend and frontend running separately.
- Do not start Run.cmd/Start.cmd at the same time: they use the same backend port.
- Stop each development process with the Visual Studio Stop button or terminal Ctrl+C.
- Source changes are used directly by these development processes; the old `backend/runtime` folder is not part of this workflow.

## Browser collection

Browser mode, configurable robots handling and manual verification waiting are described in `BROWSER-COLLECTION.md`. They do not require a different frontend/backend startup method. Start a new job after changing collection options; saved jobs preserve their original options.
