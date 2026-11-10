<#
.SYNOPSIS
  Plugin'i derler, host'ta zaten bulunan DLL'leri çıkarır ve panele yüklenecek zip'i üretir.

.EXAMPLE
  .\tools\pack-plugin.ps1 -Plugin SemihBener.Plugin.QuizTactics
  Çıktı: artifacts\SemihBener.Plugin.QuizTactics-1.0.0.zip
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Plugin,

    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$pluginDir = Join-Path $root "plugins\$Plugin"
$project = Join-Path $pluginDir "$Plugin.csproj"
if (-not (Test-Path $project)) {
    throw "Proje bulunamadı: $project"
}

$manifest = Get-Content (Join-Path $pluginDir "plugin.json") -Raw | ConvertFrom-Json
$artifacts = Join-Path $root "artifacts"
$hostOut = Join-Path $artifacts "host-reference"
$pluginOut = Join-Path $artifacts "plugin-build\$Plugin"

Write-Host "1/3 Host referans çıktısı derleniyor..."
dotnet publish (Join-Path $root "src\SemihBener.Host\SemihBener.Host.csproj") -c $Configuration -o $hostOut --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Host publish başarısız." }

Write-Host "2/3 $Plugin derleniyor..."
if (Test-Path $pluginOut) { Remove-Item $pluginOut -Recurse -Force }
dotnet publish $project -c $Configuration -o $pluginOut --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Plugin publish başarısız." }

# Host'ta bulunan DLL'ler zaten host'tan yüklenir; zip'e konmaz.
$hostDlls = Get-ChildItem $hostOut -Filter *.dll | ForEach-Object { $_.Name }

# Sadece "dotnet ef" için gereken tasarım zamanı paketleri de zip'e girmez.
$designTimePatterns = @(
    "Microsoft.EntityFrameworkCore.Design.dll",
    "Humanizer*.dll",
    "Mono.TextTemplating.dll",
    "Microsoft.CodeAnalysis*.dll",
    "Microsoft.Build*.dll",
    "System.Composition*.dll"
)

Get-ChildItem $pluginOut -Filter *.dll | Where-Object {
    $name = $_.Name
    ($hostDlls -contains $name) -or ($designTimePatterns | Where-Object { $name -like $_ })
} | Remove-Item -Force

# Dil kaynakları ve native runtime klasörleri host'ta mevcut.
Get-ChildItem $pluginOut -Directory | Remove-Item -Recurse -Force
Remove-Item (Join-Path $pluginOut "*.deps.json") -ErrorAction SilentlyContinue
Remove-Item (Join-Path $pluginOut "*.runtimeconfig.json") -ErrorAction SilentlyContinue

Write-Host "3/3 Zip hazırlanıyor..."
$zip = Join-Path $artifacts "$Plugin-$($manifest.version).zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $pluginOut "*") -DestinationPath $zip

Write-Host ""
Write-Host "Zip içeriği:"
Get-ChildItem $pluginOut | Select-Object Name, @{ Name = "KB"; Expression = { [math]::Round($_.Length / 1KB, 1) } } | Format-Table -AutoSize
Write-Host "Hazır: $zip"
