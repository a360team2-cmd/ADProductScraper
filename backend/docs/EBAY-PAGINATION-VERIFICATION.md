# eBay pagination verification — 2026-10-09

The saved leather-jackets search stopped with 119 products after two visited
pages. A live browser reproduction returned eBay's `Error Page | eBay` document
with `Something went wrong on our end` on the following request. The crawler
previously treated an empty subsequent document as the end of results.

The collector now retries that temporary document, uses a dedicated listing tab
with same-site referrers, and keeps unconfirmed empty/repeated eBay results
paused with a retryable cursor. It follows increasing `_pgn` values, discovers
numbered pagination links, and recognizes a disabled native Next control.
The optional detail-page network-idle timeout no longer discards readable HTML.

Validation:

- 76 extraction/pagination/export/network checks passed.
- 24 error-handling checks passed, including a saved eBay job that advances to
  page 3 and pauses on repeated results without losing its cursor or products.
- Live browser check: page 1 had 61 products; page 2 added 59 new products;
  page 3 added 60 new products. Total: 180 unique products, with a page-4 URL.
- This verifies progress past page 2, not exhaustive coverage of all eBay results.

Commands, from the workspace root:

```powershell
dotnet run --project "backend/tests/ADProductScraper.Tests.csproj" -p:OutputPath=bin/Verify/
dotnet run --project "backend/tests/ADProductScraper.Tests.csproj" -p:OutputPath=bin/Verify/ -- --errors-only
dotnet run --project "backend/tests/diagnostics/Diagnostic.csproj" -p:OutputPath=bin/Verify/ -- --ebay-browser
```

Do not rebuild into `bin/Verify/` while the verified backend is running from it.
The normal development startup remains the Visual Studio Debug profile; stop
the temporary verified backend before starting that profile on port 5080.

The previous DLL and the original saved 119-product job were backed up under
`.codex-backups/ebay-pagination-20261009-154931/`. The job
`abb42dd64e5a401fa20bb2ab0a77c159` was restored to a page-3 checkpoint and resumed.
