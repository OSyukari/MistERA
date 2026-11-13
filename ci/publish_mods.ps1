# Publishes the packages built by the Unity Mod Packager (Tools/Mod Packager) to one rolling GitHub release.
# Plan: Docs/Mods/Plan_ModPackaging.md
#
# - Uploads packages that are new or whose sha256 differs from the published manifest.json.
# - Deletes release assets that are no longer in the local manifest (retired / renamed packages).
# - Replaces manifest.json and regenerates the release notes.
# Re-running is safe: anything left half done is picked up again by the next diff.
#
# Env: GH_TOKEN (contents: write), GITHUB_REPOSITORY (owner/repo), MODS_DIR (folder with manifest.json + archives),
#      MODS_TAG (optional, default "mods").

param([switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'

$token = $env:GH_TOKEN
$repo = $env:GITHUB_REPOSITORY
$modsDir = $env:MODS_DIR
$tag = if ($env:MODS_TAG) { $env:MODS_TAG } else { 'mods' }
if (-not $modsDir) { throw 'MODS_DIR is not set' }

$api = "https://api.github.com/repos/$repo"
$headers = @{
    Authorization          = "Bearer $token"
    Accept                 = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
}

function Invoke-Api([string]$Method, [string]$Uri, $Body = $null) {
    $params = @{ Method = $Method; Uri = $Uri; Headers = $headers }
    if ($null -ne $Body) {
        $params.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10))
        $params.ContentType = 'application/json; charset=utf-8'
    }
    Invoke-RestMethod @params
}

function Get-Release {
    try { return Invoke-Api GET "$api/releases/tags/$tag" }
    catch {
        if ($_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return $null }
        throw
    }
}

function Get-Assets($release) {
    # the release object lists at most 100 assets inline; page through to be safe
    $all = @()
    $page = 1
    do {
        $batch = @(Invoke-Api GET "$($release.url)/assets?per_page=100&page=$page")
        $all += $batch
        $page++
    } while ($batch.Count -eq 100)
    return $all
}

function Send-Asset($release, [string]$path, [string]$name) {
    $base = $release.upload_url -replace '\{.*\}$', ''
    $uri = "${base}?name=$([Uri]::EscapeDataString($name))"
    Invoke-RestMethod -Method POST -Uri $uri -Headers $headers -InFile $path -ContentType 'application/octet-stream' -TimeoutSec 7200
}

function Remove-Asset($asset) {
    Invoke-Api DELETE "$api/releases/assets/$($asset.id)" | Out-Null
}

function Get-Sha256([string]$path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() }

function Format-Size([long]$bytes) {
    if ($bytes -ge 1GB) { return '{0:0.00} GB' -f ($bytes / 1GB) }
    if ($bytes -ge 1MB) { return '{0:0.0} MB' -f ($bytes / 1MB) }
    return '{0:0} KB' -f ($bytes / 1KB)
}

# ------------------------------------------------------------------ local state

$manifestPath = Join-Path $modsDir 'manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "No manifest at $manifestPath (build packages with Tools/Mod Packager first)" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$local = @($manifest.packages)
if ($manifest.schema -ne 1) { throw 'Expected legacy schema 1 manifest' }
$legacyIds = @{}; $legacyAssets = @{}

foreach ($p in $local) {
    if ([string]::IsNullOrWhiteSpace($p.id) -or $legacyIds.ContainsKey($p.id) -or $legacyAssets.ContainsKey($p.asset) -or $p.asset -notmatch '^[A-Za-z0-9_.-]+\.7z$') { throw "Invalid/duplicate legacy package: $($p.id)" }
    $legacyIds[$p.id] = $true; $legacyAssets[$p.asset] = $true
    $file = Join-Path $modsDir $p.asset
    if (-not (Test-Path -LiteralPath $file)) { throw "Package [$($p.id)]: archive $($p.asset) is missing from $modsDir" }
    if ((Get-Sha256 $file) -ne $p.sha256) { throw "Package [$($p.id)]: $($p.asset) does not match its manifest sha256 (rebuild it)" }
    if ((Get-Item -LiteralPath $file).Length -ne $p.size) { throw "Legacy size mismatch: $($p.asset)" }
}

$catalogPath = Join-Path $modsDir 'catalog-v2.json'
$catalog = $null
if (Test-Path -LiteralPath $catalogPath -PathType Leaf) {
    & (Join-Path $PSScriptRoot 'validate_mod_updates.ps1') -ModsDir $modsDir
    $catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
} else {
    # Packages built before the in-game updater have no catalog yet: keep publishing the manual .7z set.
    # Rebuild every package with Tools/Mod Packager once to create catalog-v2.json and its ZIPs.
    Write-Warning "No catalog-v2.json in $modsDir; publishing legacy .7z packages only (in-game updater sees no catalog)"
}
if ($ValidateOnly) { Write-Host 'Local publication validation passed; no network requests made'; return }
if (-not $token) { throw 'GH_TOKEN is not set' }
if (-not $repo) { throw 'GITHUB_REPOSITORY is not set' }

function Get-RemoteHash($asset) {
    $tmp = New-TemporaryFile
    try {
        Invoke-WebRequest -Uri $asset.url -Headers @{ Authorization = "Bearer $token"; Accept = 'application/octet-stream' } -OutFile $tmp.FullName
        return Get-Sha256 $tmp.FullName
    } finally { Remove-Item -LiteralPath $tmp.FullName -Force }
}

function Publish-Metadata($release, [string]$path, [string]$name, $existing) {
    if ($existing -and (Get-RemoteHash $existing) -eq (Get-Sha256 $path)) { return }
    $temporaryName = "$name.new"
    if ($assetByName.ContainsKey($temporaryName)) { Remove-Asset $assetByName[$temporaryName] }
    $new = Send-Asset $release $path $temporaryName
    if ((Get-RemoteHash $new) -ne (Get-Sha256 $path)) { throw "Uploaded metadata mismatch: $name" }
    if ($existing) { Remove-Asset $existing }
    Invoke-Api PATCH "$api/releases/assets/$($new.id)" @{ name = $name } | Out-Null
}

# ------------------------------------------------------------------ release

$release = Get-Release
if (-not $release) {
    Write-Host "Creating release [$tag]"
    $release = Invoke-Api POST "$api/releases" @{
        tag_name    = $tag
        name        = 'Mods & Asset Packs'
        body        = ''
        make_latest = 'false'
    }
}

$assets = Get-Assets $release
$published = @{}
$publishedManifest = $assets | Where-Object name -eq 'manifest.json' | Select-Object -First 1
if ($publishedManifest) {
    $tmp = New-TemporaryFile
    try {
        Invoke-WebRequest -Uri $publishedManifest.url -Headers @{ Authorization = "Bearer $token"; Accept = 'application/octet-stream' } -OutFile $tmp.FullName
        foreach ($p in @((Get-Content -LiteralPath $tmp.FullName -Raw -Encoding UTF8 | ConvertFrom-Json).packages)) { $published[$p.asset] = $p }
    }
    finally { Remove-Item -LiteralPath $tmp.FullName -Force }
}

$assetByName = @{}
foreach ($a in $assets) { $assetByName[$a.name] = $a }

$toUpload = @($local | Where-Object {
        -not $assetByName.ContainsKey($_.asset) -or -not $published.ContainsKey($_.asset) -or $published[$_.asset].sha256 -ne $_.sha256
    })
$localNames = @{}
foreach ($p in $local) { $localNames[$p.asset] = $true }
$toRetire = @($assets | Where-Object { $_.name -like '*.7z' -and -not $localNames.ContainsKey($_.name) })
$unchanged = $local.Count - $toUpload.Count

Write-Host "Upload: $($toUpload.Count), retire: $($toRetire.Count), unchanged: $unchanged"

# ------------------------------------------------------------------ apply

# Immutable updater archives are uploaded and checked before either catalog is changed.
# Old ZIP revisions are never retired: cached catalogs may still reference them.
foreach ($p in @(if ($catalog) { $catalog.packages })) {
    if ($assetByName.ContainsKey($p.asset)) {
        if ((Get-RemoteHash $assetByName[$p.asset]) -cne $p.sha256) { throw "Refusing to overwrite immutable asset: $($p.asset)" }
    } else {
        $new = Send-Asset $release (Join-Path $modsDir $p.asset) $p.asset
        if ($new.size -ne $p.size -or (Get-RemoteHash $new) -cne $p.sha256) { throw "Uploaded ZIP verification failed: $($p.asset)" }
    }
}

foreach ($p in $toUpload) {
    $file = Join-Path $modsDir $p.asset
    Write-Host "Uploading $($p.asset) ($(Format-Size $p.size))"
    # upload under a temporary name first so the current download stays available until the new one is complete
    $tmpName = "$($p.asset).new"
    if ($assetByName.ContainsKey($tmpName)) { Remove-Asset $assetByName[$tmpName] }
    $new = Send-Asset $release $file $tmpName
    if ((Get-RemoteHash $new) -ne $p.sha256) { throw "Uploaded legacy archive mismatch: $($p.asset)" }
    if ($assetByName.ContainsKey($p.asset)) { Remove-Asset $assetByName[$p.asset] }
    Invoke-Api PATCH "$api/releases/assets/$($new.id)" @{ name = $p.asset } | Out-Null
}

foreach ($a in $toRetire) {
    Write-Host "Retiring $($a.name)"
    Remove-Asset $a
}

# Compare metadata independently: author/version/description changes need no archive diff.
Publish-Metadata $release $manifestPath 'manifest.json' $publishedManifest
if ($catalog) { Publish-Metadata $release $catalogPath 'catalog-v2.json' $assetByName['catalog-v2.json'] }

# ------------------------------------------------------------------ release notes

$download = "https://github.com/$repo/releases/download/$tag"
$lines = @(
    '## 安装 / Install'
    ''
    '1. 下载需要的 `.7z`，放进游戏目录的 `Mods/` 文件夹。 / Download the `.7z` you want into the game''s `Mods/` folder.'
    '2. 右键 → **解压到当前文件夹 / Extract here**。解压密码 / password: `era`'
    '3. 立绘包会解压到 `Mods/Assetpacks/<包名>/`，Mod 会解压到 `Mods/<Mod名>/`。 / Asset packs land in `Mods/Assetpacks/<pack>/`, mods in `Mods/<mod>/`.'
    ''
    '拆分的立绘包（例如 `you1069_ako`）会解压回同一个包文件夹，可以只下载需要的角色。 / Split packs (e.g. `you1069_ako`) extract into the same pack folder, so you can pick only the characters you want.'
    ''
)
$groups = @(
    @{ title = 'Mods'; kind = 'mod' },
    @{ title = 'Asset packs / 立绘包'; kind = 'assetpack' }
)
foreach ($g in $groups) {
    $rows = @($local | Where-Object kind -eq $g.kind | Sort-Object group, id)
    if ($rows.Count -eq 0) { continue }
    $lines += "## $($g.title)"
    $lines += ''
    $lines += '| Package | Files | Size | Updated (UTC) |'
    $lines += '|---|---|---|---|'
    foreach ($p in $rows) {
        $lines += "| [$($p.id)]($download/$($p.asset)) | $($p.fileCount) | $(Format-Size $p.size) | $(([datetime]$p.builtAt).ToUniversalTime().ToString('yyyy-MM-dd')) |"
    }
    $lines += ''
}
Invoke-Api PATCH "$api/releases/$($release.id)" @{ body = ($lines -join "`n") } | Out-Null

# ------------------------------------------------------------------ summary

$summary = @("### Mods release [$tag]", '', "Uploaded: $($toUpload.Count) · Retired: $($toRetire.Count) · Unchanged: $unchanged", '')
foreach ($p in $toUpload) { $summary += "- uploaded ``$($p.asset)``" }
foreach ($a in $toRetire) { $summary += "- retired ``$($a.name)``" }
$summary += ''
$summary += $release.html_url
$summary | ForEach-Object { Write-Host $_ }
if ($env:GITHUB_STEP_SUMMARY) { $summary -join "`n" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8 }
