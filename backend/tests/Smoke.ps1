param([string]$BaseUrl = 'http://127.0.0.1:5080')
$ErrorActionPreference = 'Stop'
function New-Demo([int]$Limit = 0) {
    $body = @{url=''; demo=$true; maxPages=$Limit; visitDetails=$true} | ConvertTo-Json
    return (Invoke-RestMethod "$BaseUrl/api/jobs" -Method Post -ContentType 'application/json' -Body $body).id
}
function Wait-JobResult([string]$Id) {
    for ($i=0; $i -lt 100; $i++) {
        $result = Invoke-RestMethod "$BaseUrl/api/jobs/$Id"
        if ($result.status -notin @('Queued','Running','Stopping')) { return $result }
        Start-Sleep -Milliseconds 200
    }
    throw 'Collection did not finish within test timeout.'
}
$id = New-Demo
$result = Wait-JobResult $id
if ($result.status -ne 'Completed' -or $result.count -ne 6 -or $result.pages -ne 2) { throw 'Pagination/deduplication integration failed.' }
Write-Host 'PASS: two-page collection produces six unique products'
$rows = Invoke-RestMethod "$BaseUrl/api/jobs/$id/products?take=50"
if ($rows[0].material -ne 'Ceramic' -or $rows[0].store -ne 'Demo Studio') { throw 'Detail enrichment failed.' }
Write-Host 'PASS: product detail enrichment'
$response = Invoke-WebRequest "$BaseUrl/api/jobs/$id/excel"
if ($response.Headers.'Content-Type' -notmatch 'spreadsheetml.sheet') { throw 'Wrong Excel content type.' }
if ($response.RawContentLength -lt 1000) { throw 'Excel download is empty.' }
Write-Host 'PASS: Excel download endpoint'
$id = New-Demo 1
$result = Wait-JobResult $id
if ($result.status -ne 'Limited' -or $result.pages -ne 1 -or $result.count -ne 3) { throw 'Page limit failed.' }
Write-Host 'PASS: page limit returns partial collection'
$id = New-Demo
Invoke-RestMethod "$BaseUrl/api/jobs/$id/cancel" -Method Post | Out-Null
$result = Wait-JobResult $id
if ($result.status -ne 'Cancelled') { throw 'Cancellation failed.' }
Invoke-RestMethod "$BaseUrl/api/jobs/$id/resume" -Method Post | Out-Null
$result = Wait-JobResult $id
if ($result.status -ne 'Completed' -or $result.count -ne 6) { throw 'Resume failed.' }
Write-Host 'PASS: cancellation and resume'
$rejected = $false
try { Invoke-RestMethod "$BaseUrl/api/jobs" -Method Post -ContentType 'application/json' -Body '{"url":"http://127.0.0.1/private"}' | Out-Null } catch { $rejected = [int]$_.Exception.Response.StatusCode -eq 400 }
if (-not $rejected) { throw 'Private target URL was not rejected.' }
Write-Host 'PASS: private target rejected at API boundary'
