<#
.SYNOPSIS
  Bir plugin için EF Core migration üretir. Migration üretmek için veritabanına bağlanmak gerekmez.

.EXAMPLE
  .\tools\add-migration.ps1 -Plugin SemihBener.Plugin.QuizTactics
  .\tools\add-migration.ps1 -Plugin SemihBener.Plugin.BidClash -Name AddAuctionImage

.NOTES
  Gerekli araç (bir kez): dotnet tool install --global dotnet-ef --version 10.*
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Plugin,

    [string]$Name = "Initial"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "plugins\$Plugin\$Plugin.csproj"
if (-not (Test-Path $project)) {
    throw "Proje bulunamadı: $project"
}

dotnet ef migrations add $Name `
    --project $project `
    --startup-project $project `
    --output-dir Data/Migrations

if ($LASTEXITCODE -ne 0) { throw "Migration üretilemedi." }
Write-Host "Migration eklendi: $Plugin / $Name"
