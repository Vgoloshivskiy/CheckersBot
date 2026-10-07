<#
.SYNOPSIS
  Publishes the API and the KingsRow host into one folder, ready to copy to the server.

.EXAMPLE
  .\publish.ps1
  .\publish.ps1 -Output C:\inetpub\checkers-api

The host goes into the "engine-host" subfolder of the API, which is where Engine:HostPath points by default.
Both are published for win-x64: Kingsrow64.dll and egdb64.dll are 64-bit, so the host that loads them must be too.
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot "..\artifacts\api"),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")

function Publish([string]$Project, [string]$Target) {
    dotnet publish $Project -c $Configuration -r win-x64 --self-contained false -o $Target
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Project" }
}

Publish (Join-Path $root "src\Checkers.Api\Checkers.Api.csproj") $Output
Publish (Join-Path $root "src\Checkers.Engine.KingsRowHost\Checkers.Engine.KingsRowHost.csproj") (Join-Path $Output "engine-host")

Write-Host ""
Write-Host "Published to $Output"
Write-Host "Next: set Engine:Home and Engine:Databases in appsettings.json (or environment variables), then start the site."
