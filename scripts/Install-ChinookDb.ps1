<#
.SYNOPSIS
  Downloads the Chinook 2- to 8-piece endgame databases and unpacks them into one folder.

.EXAMPLE
  .\Install-ChinookDb.ps1 -Destination D:\tb\chinook

About 2.7 GB to download and 5.6 GB unpacked. Use the folder as Engine:Databases.
All files must end up in the same folder, so anything inside a subfolder of a zip is moved up.
#>
param(
    [Parameter(Mandatory = $true)][string]$Destination,
    [switch]$KeepZips
)

$ErrorActionPreference = "Stop"

# The file list from https://webdocs.cs.ualberta.ca/~chinook/databases/
$names = @("DB6") + (0..4 | ForEach-Object { "DB7.$_" }) +
    @("DB8.00", "DB8.10", "DB8.20", "DB8.30", "DB8.40", "DB8.11", "DB8.21", "DB8.31", "DB8.41",
      "DB8.22", "DB8.32", "DB8.42", "DB8.33", "DB8.43", "DB8.44")

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
$temp = Join-Path $Destination "_unpack"

foreach ($name in $names) {
    $zip = Join-Path $Destination "$name.zip"
    if (-not (Test-Path $zip)) {
        Write-Host "Downloading $name ..."
        Invoke-WebRequest -Uri "http://www.cs.ualberta.ca/~chinook/DataBases/$name.zip" -OutFile $zip -UseBasicParsing
    }

    Write-Host "Unpacking $name ..."
    if (Test-Path $temp) { Remove-Item $temp -Recurse -Force }
    Expand-Archive -Path $zip -DestinationPath $temp -Force
    Get-ChildItem $temp -Recurse -File | Move-Item -Destination $Destination -Force
    Remove-Item $temp -Recurse -Force
    if (-not $KeepZips) { Remove-Item $zip }
}

Write-Host ""
Write-Host "Done. Set Engine:Databases to $Destination."
Write-Host "When the API starts, its log should say 'endgame database: type ..., 8 pieces'."
