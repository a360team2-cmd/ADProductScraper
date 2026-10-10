# Error handling

API errors return JSON containing `error` and `traceId`. Invalid input uses HTTP 400,
invalid collection state uses 409, storage failures use 503, website connection
failures use 502, and timeouts use 504. Unexpected errors use 500 without exposing
the stack trace to the browser. The backend console records the full exception
with the same reference. Client disconnects cancel the response.

Collection failures retain their cursor and collected products for Resume and
Excel export. Full exception details are stored in `backend/data/<job-id>.json`
under `failureDetails`. Product processing failures generate warnings and allow
other products to continue. Optional browser scrolling failures retain the HTTP
listing. A failure in one collection does not stop the worker queue.

If disk saving fails, the current in-memory collection remains available and a
warning asks the user to download it before restarting. Recovery cannot persist
data while the disk is full or inaccessible. Failed initial saves do not enqueue
a new job. Invalid saved collection files are logged and skipped during startup.

The frontend handles API errors, offline connections, invalid responses,
request timeouts, Excel failures, Angular runtime errors, and unhandled promise
rejections. Normal requests time out after 30 seconds; Excel requests after two
minutes. Failed table navigation retains the previous rows and offset.

## Verification

From the project directory:

```powershell
dotnet run --project backend/tests/ADProductScraper.Tests.csproj -c Release -p:UseAppHost=false -- --errors-only
dotnet tests/bin/Release/net9.0/ADProductScraper.Tests.dll --pagination-only
```

From `frontend`:

```powershell
node tests/api-errors.mjs
npm.cmd run build
```

Validated: 22 backend error checks, 8 frontend request checks, 16 pagination
checks, and the Angular production build. The existing full extraction suite has
a separate price/currency/seller assertion failure.

Live Decrum verification on October 8, 2026 collected all 119 men's jackets and
all 134 women's jackets, including product detail enrichment and Excel export,
without collection warnings. Browser scrolling independently loaded 119 men's
products without the previous WebSocket exception. Explicit close code/reason
values avoid the Playwright 1.58 WebSocket dispatcher error. T4S rendered product
cards and actual next links replace the truncated, separately paginated JSON-LD
list as the product source. Breadcrumb URLs are excluded.

When the advertised collection total is available, a job below that total pauses
with a retryable cursor rather than reporting completion. This includes empty,
repeated, and cyclic page results. Successful completion states the advertised
product total in the job message.

Restart the backend after rebuilding. Restart or refresh the frontend to use the
updated error UI. The legacy `backend/runtime` bundle is not updated by these source
changes.
