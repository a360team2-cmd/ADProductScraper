# Verification

## v2 — separate IDE workflow and readability

- `backend/AD Product Scraper.sln` builds successfully with zero compiler warnings/errors.
- The `ADProductScraper.Api` launch profile starts the API at `http://localhost:5080`.
- `npm.cmd start` starts the Angular development server at `http://localhost:4200` with source maps and live reload.
- Health checks succeed directly at :5080 and through the Angular :4200 API proxy.
- The six API smoke checks pass through the frontend proxy, including `.xlsx` download and cancellation/resume.
- Angular production build succeeds; initial assets approximately 207 kB.
- Browser visual check confirms enlarged typography and controls. Normal text is 14–16px, table cells 14px, and heading sizes 28–34px.
- The Visual Studio launch profile and solution were verified with the .NET CLI; Visual Studio itself was not automated.

## Original collection verification

Verified on Windows with .NET SDK 9.0.318 and Node.js 22.23.1.

- Backend Release build: succeeded, no compiler warnings/errors.
- Angular production build: succeeded, approximately 204 kB initial raw assets.
- Executable regression suite: 31 checks, covering structured extraction, three marketplace HTML fixtures, custom selectors, pagination, deduplication, robots matching, network address validation and Excel round-trip/formula safety.
- API smoke suite: six checks, covering a two-page collection, detail enrichment, Excel download, page limit, cancellation/resume and private-target rejection.
- Browser: dashboard rendered; demo produced 6 unique products across 2 pages; Download Excel action worked without a displayed error.
- Downloaded demo workbook: valid .xlsx, Products sheet has 1 header row + 6 product rows.
- Public HTTPS integration: `https://books.toscrape.com/catalogue/page-1.html`, custom selectors, 2-page limit, detail visits disabled, yielded **40 products across 2 pages**. This is an intentionally scrape-friendly test store; it is not evidence of live marketplace compatibility.

Live test selectors: card `article.product_pod`, title/link `h3 a`, price `.price_color`, image `img`, next `li.next a`.

Live Amazon, Etsy and eBay end-to-end extraction has **not** been verified. CAPTCHA, JavaScript-only content, unavailable material/store fields and changing layouts remain limitations. Their extraction presets were checked with local HTML fixtures.

The first public HTTPS attempt inside the restricted Windows execution environment failed during TLS initialization. The same test succeeded when the localhost application ran normally outside that restriction, with certificate verification left enabled.
