param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.runtime/dotnet'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.runtime/nuget'
$env:npm_config_cache = Join-Path $PSScriptRoot '.runtime/npm-cache'
$env:npm_config_offline = 'false'
$env:NG_CLI_ANALYTICS = 'false'
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET 9 SDK first: https://dotnet.microsoft.com/download/dotnet/9.0' }
if (-not $SkipBuild) {
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) { throw 'Install Node.js 22.12 or newer (22.x) first.' }
    Push-Location frontend
    try {
        & npm.cmd ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'npm install failed.' }
        & npm.cmd run build
        if ($LASTEXITCODE -ne 0) { throw 'Angular build failed.' }
    } finally { Pop-Location }
    New-Item -ItemType Directory -Force backend/wwwroot | Out-Null
    Copy-Item frontend/dist/ad-product-scraper/browser/* backend/wwwroot -Recurse -Force
    & dotnet build backend/ADProductScraper.Api.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw '.NET build failed.' }
}
Write-Host 'AD Product Scraper is starting. Open http://127.0.0.1:5080 in your browser.' -ForegroundColor Green
Write-Host 'Keep this window open. Press Ctrl+C to stop.'
& dotnet run --project backend/ADProductScraper.Api.csproj --configuration Release --no-build --no-launch-profile
