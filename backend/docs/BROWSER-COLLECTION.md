# Browser collection update

This update changes the **source project** used by Visual Studio + VS Code. Open the existing solution and frontend folder. Restart both applications after updating.

## Your Etsy search

Paste `https://www.etsy.com/search?q=leather%20jackets&ref=search_bar`.

- Keep **Browser mode (JavaScript pages)** checked.
- Leave **Stop on robots.txt rules** unchecked for warning-only handling. The advisory rule is still shown in collection notes.
- Start with 5 pages while checking the website; then select all discoverable pages if the site works.
- Click **Collect products**. A separate Microsoft Edge window opens (Chrome is the fallback).
- Keep that window open. If a CAPTCHA, login or consent screen appears, complete it yourself. The app waits up to 3 minutes and continues when readable products appear.
- The same browser session is reused while paging and visiting product details, so its cookies survive during the job. Your normal browser profile is not read or modified.
- **Download Excel** is available when there are collected rows, even if collection later stops.

Browser mode also renders other public ecommerce sites. HTML selectors, structured product data and discoverable next links are still used for extraction. It is not a universal integration for every site's pagination implementation. Sites using only infinite scroll or custom load-more controls may need a dedicated adapter.

## What this does and does not fix

Etsy's rule `Disallow: /search?*q=` caused the earlier tool to stop before visiting the search page. The robots handling choice is now explicit: stop, or show a warning and attempt normal website access. This is not a CAPTCHA solver or a way around an actual access denial.

JavaScript now runs in a real browser. Login/CAPTCHA handling is **manual**, not automatic. A site can still deny access, require an account or restrict available results. Paid CAPTCHA-solving/rotating-proxy services and marketplace APIs are **not configured**; no provider credentials were supplied. Do not interpret this update as guaranteed live compatibility with Etsy, Amazon or eBay.

## Development

1. Backend: open `backend/AD Product Scraper.sln` in Visual Studio, restore NuGet packages, run `ADProductScraper.Api` with F5.
2. Frontend: open `frontend` in VS Code, run `npm.cmd start`, then open `http://localhost:4200`.
3. Install Microsoft Edge or Chrome if neither is present. Microsoft.Playwright is a .NET package restored by Visual Studio; no separate Chromium installation is needed.
4. Old failed jobs retain their old options. **Start a new collection** to use the new browser/robots options.

The browser uses a local, public-address-only transport. Local/private target addresses are rejected, including HTTPS tunnel targets. TLS certificates are verified normally by the browser. This transport is not an external proxy/anti-bot service. It closes with the collection.

Existing `backend/data` history is preserved. Original edited files are backed up beside the project under a timestamped `.codex-backups` directory. The prebuilt `backend/runtime` folder is not used by your VS Code + Visual Studio workflow; rebuild that separately if needed.
