# Zips the demo build for release (used by .github/workflows/main.yml and .gitlab-ci.yml).
# The archive root is the build folder itself (e.g. MistEra_Demo/), so players extract it as before.
#
# Left out of the archive:
# - <build>/Save/   (local saves from test runs)
# - <build>/Mods/   (mod archives, extracted mods and asset packs). Only Mods/Assetpacks/Image_Generation_Config.json
#                   is shipped, if it exists.
#
# Usage: pwsh -NoProfile -File ci/zip_demo.ps1 <buildDir> <archive.7z> [password]

param(
    [Parameter(Mandatory = $true)][string]$BuildDir,
    [Parameter(Mandatory = $true)][string]$Archive,
    [string]$Password = ''
)

$ErrorActionPreference = 'Stop'

$dir = (Resolve-Path -LiteralPath $BuildDir).Path.TrimEnd('\', '/')
$name = Split-Path $dir -Leaf
$parent = Split-Path $dir -Parent
# resolve the archive path before changing directory
$archivePath = [IO.Path]::GetFullPath((Join-Path (Get-Location) $Archive))
if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }

$pw = @()
if ($Password) { $pw = @("-p$Password") }

function Invoke-7z([string[]]$arguments) {
    & 7z @arguments
    if ($LASTEXITCODE -ne 0) { throw "7z failed with exit code $LASTEXITCODE" }
}

Push-Location $parent
try {
    # exclusions are paths relative to the archive root, so only the top-level Save/Mods are dropped
    Invoke-7z (@('a', '-t7z', $archivePath, $name, "-x!$name\Save", "-x!$name\Mods") + $pw)

    $config = "$name\Mods\Assetpacks\Image_Generation_Config.json"
    if (Test-Path -LiteralPath $config) {
        Invoke-7z (@('a', '-t7z', $archivePath, $config) + $pw)
        Write-Host "Included $config"
    }
    else { Write-Host "No $config, Mods folder left out entirely" }
}
finally { Pop-Location }

Write-Host "Created $archivePath"
