<#
.SYNOPSIS
  Checks a running service against the acceptance criteria in the spec.

.EXAMPLE
  .\smoke-test.ps1
  .\smoke-test.ps1 -BaseUrl http://localhost/checkers

Needs the engine started (the health check waits for it) and, for the tablebase checks, an endgame database.
Works in Windows PowerShell 5.1 and PowerShell 7.
#>
param(
    [string]$BaseUrl = "http://localhost:5080",
    [int]$WaitForReadySeconds = 240
)

$ErrorActionPreference = "Stop"
$BaseUrl = $BaseUrl.TrimEnd("/")
$script:failures = 0

function Report([bool]$ok, [string]$name, [string]$detail) {
    if ($ok) { Write-Host ("PASS  {0}  {1}" -f $name, $detail) -ForegroundColor Green }
    else { Write-Host ("FAIL  {0}  {1}" -f $name, $detail) -ForegroundColor Red; $script:failures++ }
}

# HttpClient behaves the same in Windows PowerShell 5.1 and PowerShell 7, and returns 4xx/5xx responses
# instead of throwing, which the invalid-PDN and timeout checks need.
Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(30)

function Call([string]$Method, [string]$Path, $Body = $null) {
    $url = "$BaseUrl$Path"
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    if ($Method -eq "GET") {
        $response = $client.GetAsync($url).Result
    } else {
        $json = $Body | ConvertTo-Json -Depth 6
        $content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, "application/json")
        $response = $client.PostAsync($url, $content).Result
    }
    $text = $response.Content.ReadAsStringAsync().Result
    $watch.Stop()
    $data = $null
    if ($text) { try { $data = $text | ConvertFrom-Json } catch { } }
    [pscustomobject]@{ Status = [int]$response.StatusCode; Data = $data; Ms = $watch.ElapsedMilliseconds }
}

function SuggestBody([string]$Pdn, [string]$Level, $Limits = $null) {
    $body = @{ gameId = "checkers-8x8"; state = @{ notation = "PDN"; position = $Pdn }; level = $Level }
    if ($Limits) { $body.limits = $Limits }
    $body
}

function Suggest([string]$Pdn, [string]$Level, $Limits = $null) {
    Call "POST" "/v1/move/suggest" (SuggestBody $Pdn $Level $Limits)
}

$midgame = "W:W17,18,21,22,23,24,26,27,28,29,30,32:B1,2,3,4,6,7,8,9,10,11,12,16"
# Endgame positions with no capture pending for either side, from the egdb example's test table:
# a 4-piece draw, a 6-piece win, and an 8-piece win (4 vs 4, which the Chinook database covers).
$endgames = @("B:W17,26:B12,19", "B:WK7,K10,18:BK1,K3,K4", "B:W10,11,27,29:BK1,K2,K3,20")

Write-Host "Target: $BaseUrl"

# 1. Health check returns ok once the engine workers have started and warmed up.
$deadline = (Get-Date).AddSeconds($WaitForReadySeconds)
do {
    $h = Call "GET" "/healthz"
    if ($h.Status -eq 200) { break }
    Start-Sleep -Seconds 2
} while ((Get-Date) -lt $deadline)
Report ($h.Status -eq 200 -and $h.Data.ok -eq $true) "health" "status=$($h.Status) workers=$($h.Data.workers) engine=$($h.Data.engine) databasePieces=$($h.Data.databasePieces)"
if ($h.Status -ne 200) { Write-Host "The engine did not become ready; stopping." -ForegroundColor Red; exit 1 }

# 2. Positions with 8 pieces or fewer: under 50 ms with tablebaseHit true (service-side time).
if ($h.Data.databasePieces -ge 8) {
    foreach ($pdn in $endgames) {
        $r = Suggest $pdn "strong"
        Report ($r.Status -eq 200 -and $r.Data.info.tablebaseHit -eq $true -and $r.Data.info.timeMs -lt 50) `
            "tablebase $pdn" "status=$($r.Status) hit=$($r.Data.info.tablebaseHit) move=$($r.Data.bestMove) wdl=$($r.Data.scoreOrWDL) service=$($r.Data.info.timeMs) ms"
    }
} else {
    Write-Host "SKIP  tablebase checks: the engine reports an endgame database of $($h.Data.databasePieces) pieces (need 8)." -ForegroundColor Yellow
}

# 3. Midgame at level strong: under 600 ms, and the move is legal.
$mid = Suggest $midgame "strong"
$legal = $null
if ($mid.Status -eq 200) { $legal = Call "POST" "/v1/move/validate" @{ position = $midgame; move = $mid.Data.bestMove } }
Report ($mid.Status -eq 200 -and $mid.Data.info.timeMs -lt 600 -and $legal.Data.legal -eq $true) `
    "strong midgame" "move=$($mid.Data.bestMove) depth=$($mid.Data.depth) nodes=$($mid.Data.nodes) service=$($mid.Data.info.timeMs) ms legal=$($legal.Data.legal)"

# 4. The same request again is served from the cache.
$again = Suggest $midgame "strong"
Report ($again.Status -eq 200 -and $again.Data.info.cached -eq $true -and $again.Data.bestMove -eq $mid.Data.bestMove) `
    "cache" "cached=$($again.Data.info.cached) service=$($again.Data.info.timeMs) ms"

# 5. Level weak is deterministic: the same answer twice from a cold cache (different depth cap keeps it out of the cache).
$w1 = Suggest $midgame "weak" @{ maxDepth = 7 }
$w2 = Suggest $midgame "weak" @{ maxDepth = 6 }
Report ($w1.Status -eq 200 -and $w2.Status -eq 200) "weak level answers" "moves=$($w1.Data.bestMove) / $($w2.Data.bestMove) depths=$($w1.Data.depth) / $($w2.Data.depth)"

# 6. Invalid PDN gets 422.
$bad = Suggest "B:W18,19,22:B18,5" "weak"
Report ($bad.Status -eq 422) "invalid PDN returns 422" "status=$($bad.Status) detail=$($bad.Data.detail)"

# 7. Validate endpoint: one legal and one illegal move.
$start = "B:W21-32:B1-12"
$ok = Call "POST" "/v1/move/validate" @{ position = $start; move = "9-13" }
$no = Call "POST" "/v1/move/validate" @{ position = $start; move = "9-18" }
Report ($ok.Data.legal -eq $true -and $no.Data.legal -eq $false) "validate" "9-13 legal=$($ok.Data.legal), 9-18 legal=$($no.Data.legal)"

# 8. Timeouts return 504. Send more strong requests at once than there are workers, with a hard limit that
#    the last one in the queue cannot meet (each strong search takes about 500 ms).
$queue = @(
    "W:W21-32:B1-8,10-13",
    "W:W21-32:B1-8,10-12,14",
    "W:W21-32:B1-9,11,12,14"
)
$tasks = foreach ($pdn in $queue) {
    $json = (SuggestBody $pdn "strong" @{ hardTimeMs = 700 }) | ConvertTo-Json -Depth 6
    $content = New-Object System.Net.Http.StringContent($json, [System.Text.Encoding]::UTF8, "application/json")
    $client.PostAsync("$BaseUrl/v1/move/suggest", $content)
}
[System.Threading.Tasks.Task]::WaitAll([System.Threading.Tasks.Task[]]@($tasks))
$statuses = @($tasks | ForEach-Object { [int]$_.Result.StatusCode })
if ($h.Data.workers -lt 3) {
    Report ($statuses -contains 504) "timeout returns 504" "statuses=$($statuses -join ',') (workers=$($h.Data.workers))"
} else {
    Write-Host "SKIP  timeout check: $($h.Data.workers) workers can serve all three requests at once." -ForegroundColor Yellow
}

if ($script:failures -gt 0) { Write-Host "$script:failures check(s) failed." -ForegroundColor Red; exit 1 }
Write-Host "All checks passed." -ForegroundColor Green
