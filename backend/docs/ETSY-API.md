# Etsy API integration

The source application supports public, read-only Etsy listing searches through Etsy Open API v3. This does not modify listings or access sales/private member data. App approval and the appropriate Etsy access level are required. Configured credentials do not imply approval.

## Configure credentials

In Visual Studio, right-click the backend project and select **Manage User Secrets**. Store these values locally:

```json
{
  "Etsy:Keystring": "YOUR_KEYSTRING",
  "Etsy:SharedSecret": "YOUR_SHARED_SECRET"
}
```

The project has a User Secrets ID. Secrets are automatically loaded by the Development launch profile. Do not add real values to appsettings files, frontend code, screenshots, or Git.

Alternatively, set environment variables `Etsy__Keystring` and `Etsy__SharedSecret` for the backend process. Environment variables also work outside Development. Restart the backend after configuration changes.

## Use the integration

1. Start the backend in Visual Studio and run `npx ng serve` in `frontend`.
2. Select **Etsy API** under Collection source.
3. Enter keywords, for example `leather jackets`. Optionally enter a **numeric shop ID** to limit results to that shop; a shop name or shop URL is not a numeric ID.
4. Choose a page limit (each page contains up to 100 listings), then collect.
5. Use the existing preview, Stop, Resume, history and Excel download controls.

If the page says credentials are missing, configure them and refresh the frontend. A 401/403 failure asks you to check credentials, app approval and access level; it does not fall back to website scraping.

## API behavior

- `GET /api/etsy/status` exposes only whether both credential values are configured. It does not call Etsy or verify approval.
- `POST /api/jobs` accepts `source: "etsy"`, `keywords`, optional `etsyShopId`, and `maxPages` (0 for all available pages). Existing website and demo jobs remain supported.
- Public active-listing endpoints fetch a page. A batch request adds Images and Shop associations. Prices use the API amount/divisor and provided currency; missing fields stay empty. No currency conversion or inferred discount is performed.
- The client uses the official HTTPS API host and sends `x-api-key` as `keystring:shared-secret`. Redirects are disabled.
- Requests are serialized with a 250 ms interval. HTTP 429 retries are bounded and honor Retry-After up to 60 seconds; longer quota waits fail the job with a resumable cursor.
- Each completed page checkpoints the next offset. Failed detail batches do not advance it. Duplicate listings are removed; repeated pages pause collection.
- Listings may change during pagination. Etsy API results and ordering may differ from the website. Empty pages before the reported total are marked partial, not complete. App permissions and API pagination limits can restrict coverage.
- This implementation uses public API-key endpoints. OAuth for private shop/member data is outside this integration.

Example request:

```json
{
  "url": "",
  "source": "etsy",
  "keywords": "leather jackets",
  "etsyShopId": null,
  "maxPages": 2
}
```

Run offline integration checks from the root:

```powershell
dotnet run --project backend/tests/ADProductScraper.Tests.csproj -- --etsy-only
```

These use fake API responses and credentials. Live access must be verified after Etsy approves the app. The packaged app under `backend/runtime` is not rebuilt by these source changes; use Visual Studio and VS Code.

Official references: [Request standards](https://developers.etsy.com/documentation/essentials/requests/), [API reference](https://developer.etsy.com/documentation/reference), [Access levels](https://developers.etsy.com/).
