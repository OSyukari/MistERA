param([Parameter(Mandatory)][string]$ModsDir)
$ErrorActionPreference = 'Stop'
# These mocks reject any unexpected request. No network implementation is reachable.
$mock = @{ assets = @{}; bytes = @{}; nextId = 0; uploads = [Collections.Generic.List[string]]::new(); deleted = [Collections.Generic.List[string]]::new() }
function Add-MockAsset([string]$name, [byte[]]$content) {
    $mock.nextId++
    $id = $mock.nextId
    $asset = [pscustomobject]@{ id = $id; name = $name; size = $content.Length; url = "https://mock.invalid/assets/$id" }
    $mock.assets[$id] = $asset; $mock.bytes[$id] = $content
    return $asset
}
foreach ($file in Get-ChildItem -LiteralPath $ModsDir -File | Where-Object { $_.Extension -in @('.7z','.zip','.json') }) {
    $content = [IO.File]::ReadAllBytes($file.FullName)
    if ($file.Name -eq 'manifest.json') { $content = [Text.Encoding]::UTF8.GetBytes(([Text.Encoding]::UTF8.GetString($content).Replace('"schema": 1', '"schema": 1, "oldMetadata": true'))) }
    if ($file.Name -eq 'catalog-v2.json') { $content = [Text.Encoding]::UTF8.GetBytes(([Text.Encoding]::UTF8.GetString($content).Replace('"schema": 2', '"schema": 2, "oldMetadata": true'))) }
    Add-MockAsset $file.Name $content | Out-Null
}
Add-MockAsset ('retired-' + ('a' * 64) + '.zip') ([Text.Encoding]::UTF8.GetBytes('old immutable revision')) | Out-Null
$mock.release = [pscustomobject]@{ id = 1; url = 'https://api.github.com/repos/test/repository/releases/1'; upload_url = 'https://uploads.mock.invalid/release{?name}'; html_url = 'https://mock.invalid/release' }
function Invoke-RestMethod {
    param($Method, $Uri, $Headers, $Body, $ContentType, $InFile, $TimeoutSec)
    $uriText = [string]$Uri
    if ($Method -eq 'GET' -and $uriText -eq 'https://api.github.com/repos/test/repository/releases/tags/mods') { return $mock.release }
    if ($Method -eq 'GET' -and $uriText -eq 'https://api.github.com/repos/test/repository/releases/1/assets?per_page=100&page=1') { return @($mock.assets.Values) }
    if ($Method -eq 'POST' -and $uriText.StartsWith('https://uploads.mock.invalid/release?name=')) {
        $name = [Uri]::UnescapeDataString($uriText.Split('=')[1])
        $mock.uploads.Add($name)
        return Add-MockAsset $name ([IO.File]::ReadAllBytes($InFile))
    }
    if ($uriText -match '^https://api.github.com/repos/test/repository/releases/assets/([0-9]+)$') {
        $id = [int]$Matches[1]
        if ($Method -eq 'DELETE') { $mock.deleted.Add($mock.assets[$id].name); $mock.assets.Remove($id); $mock.bytes.Remove($id); return }
        if ($Method -eq 'PATCH') { $json = [Text.Encoding]::UTF8.GetString($Body) | ConvertFrom-Json; $mock.assets[$id].name = $json.name; return $mock.assets[$id] }
    }
    if ($Method -eq 'PATCH' -and $uriText -eq 'https://api.github.com/repos/test/repository/releases/1') { return $mock.release }
    throw "Unexpected request rejected: $Method $Uri"
}
function Invoke-WebRequest {
    param($Uri, $Headers, $OutFile)
    if ([string]$Uri -notmatch '^https://mock.invalid/assets/([0-9]+)$') { throw "Unexpected download rejected: $Uri" }
    [IO.File]::WriteAllBytes($OutFile, $mock.bytes[[int]$Matches[1]])
}
$old = @{ MODS_DIR = $env:MODS_DIR; GH_TOKEN = $env:GH_TOKEN; GITHUB_REPOSITORY = $env:GITHUB_REPOSITORY; MODS_TAG = $env:MODS_TAG; GITHUB_STEP_SUMMARY = $env:GITHUB_STEP_SUMMARY }
try {
    $env:MODS_DIR = $ModsDir; $env:GH_TOKEN = 'synthetic-test-token'; $env:GITHUB_REPOSITORY = 'test/repository'; $env:MODS_TAG = 'mods'; $env:GITHUB_STEP_SUMMARY = ''
    & "$PSScriptRoot/../publish_mods.ps1"
    if ($mock.uploads.Count -ne 2 -or -not $mock.uploads.Contains('manifest.json.new') -or -not $mock.uploads.Contains('catalog-v2.json.new')) { throw 'Metadata-only publishing did not replace both catalogs independently' }
    if (@($mock.deleted | Where-Object { $_ -like '*.zip' }).Count) { throw 'Publisher retired an immutable ZIP revision' }
    $mock.uploads.Clear(); $mock.deleted.Clear()
    & "$PSScriptRoot/../publish_mods.ps1"
    if ($mock.uploads.Count -or $mock.deleted.Count) { throw 'Unchanged publication was not idempotent' }
    $currentCatalog = Get-Content -LiteralPath (Join-Path $ModsDir 'catalog-v2.json') -Raw | ConvertFrom-Json
    $package = $currentCatalog.packages[0]
    $missing = @($mock.assets.Values | Where-Object name -eq $package.asset)[0]
    $mock.assets.Remove($missing.id); $mock.bytes.Remove($missing.id)
    & "$PSScriptRoot/../publish_mods.ps1"
    if ($mock.uploads.Count -ne 1 -or $mock.uploads[0] -ne $package.asset -or $mock.deleted.Count) { throw 'Missing immutable ZIP was not repaired safely' }
    $corrupt = @($mock.assets.Values | Where-Object name -eq $package.asset)[0]
    $mock.bytes[$corrupt.id] = [Text.Encoding]::UTF8.GetBytes('corrupt published revision')
    $mock.uploads.Clear()
    $rejected = $false
    try { & "$PSScriptRoot/../publish_mods.ps1" } catch { $rejected = $true }
    if (-not $rejected -or $mock.uploads.Count -or $mock.deleted.Count) { throw 'Immutable collision was not rejected before publication' }
    Write-Host 'Mocked metadata-only publication, immutable retention/repair/collision, and repeat publication passed'
} finally { foreach ($key in $old.Keys) { [Environment]::SetEnvironmentVariable($key, $old[$key]) } }
