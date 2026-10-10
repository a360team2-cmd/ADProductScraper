@echo off
cd /d "%~dp0backend\runtime"
echo Open http://127.0.0.1:5080 in your browser.
echo Keep this window open. Press Ctrl+C to stop.
dotnet ADProductScraper.Api.dll
pause
