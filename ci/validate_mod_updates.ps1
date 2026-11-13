param([Parameter(Mandatory)][string]$ModsDir)
$ErrorActionPreference = 'Stop'
# Catalog-level checks only; archives are never opened, so no 7z is needed. Everything here comes from what the Mod
# Packager stored at build time (archive size/hash, roots, shared-file hashes). Archive contents are checked by the
# in-game installer before anything is installed (entries inside roots, shared hashes, ModID/dependencies, no password).
function Assert-PackagePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '\\|[<>:"|?*\x00-\x1f]' -or
        @($Path.Split('/') | Where-Object { $_ -in @('', '.', '..') -or $_ -match '[. ]$|^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)' }).Count) { throw "Unsafe package path: $Path" }
}
$catalog = Get-Content -LiteralPath (Join-Path $ModsDir 'catalog-v2.json') -Raw | ConvertFrom-Json
if ($catalog.schema -ne 2 -or $null -eq $catalog.packages) { throw 'Expected schema 2 catalog with packages' }
# catalog-v2.json (in-game updater) and manifest.json (manual downloads) must describe the same .7z files
$manifest = Get-Content -LiteralPath (Join-Path $ModsDir 'manifest.json') -Raw | ConvertFrom-Json
$manifestByAsset = @{}
foreach ($m in @($manifest.packages)) { $manifestByAsset[$m.asset] = $m }
$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$modIds = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$sharedFiles = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
$exclusiveRoots = [Collections.Generic.List[string]]::new()
foreach ($p in $catalog.packages) {
    if ([string]::IsNullOrWhiteSpace($p.id) -or -not $ids.Add($p.id)) { throw "Missing or duplicate package id: $($p.id)" }
    Assert-PackagePath $p.id
    if ($p.id.Contains('/')) { throw "Invalid package id: $($p.id)" }
    if ($p.kind -notin @('mod', 'assetpack') -or [string]::IsNullOrWhiteSpace($p.group)) { throw "Invalid kind/group: $($p.id)" }
    Assert-PackagePath $p.group
    if ($p.group.Contains('/')) { throw "Invalid package group: $($p.group)" }
    if ($p.kind -eq 'mod') {
        if ([string]::IsNullOrWhiteSpace($p.modId) -or $modIds.ContainsKey($p.modId)) { throw "Missing or duplicate ModID: $($p.modId)" }
        $modIds.Add($p.modId, $p)
    }
    if ($p.minimumBuild -lt 0 -or $p.maximumBuild -lt 0 -or ($p.maximumBuild -gt 0 -and $p.maximumBuild -lt $p.minimumBuild) -or $p.channel -notin @('', 'full', 'safe', $null)) { throw "Invalid compatibility: $($p.id)" }
    if ($p.archiveFormat -ne '7z' -or $p.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $p.asset -notmatch '^[A-Za-z0-9_.-]+\.7z$') { throw "Expected .7z package: $($p.id)" }
    $m = $manifestByAsset[$p.asset]
    if (-not $m -or $m.id -cne $p.id -or $m.sha256 -cne $p.sha256 -or $m.size -ne $p.size) { throw "Catalog and manifest.json disagree: $($p.id)" }
    if (@($p.roots).Count -eq 0) { throw "No payload roots: $($p.id)" }
    $roots = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($root in $p.roots) {
        Assert-PackagePath $root
        if (-not $roots.Add($root) -or $root -match '(^|/)_generated(/|$)' -or
            ($p.kind -eq 'mod' -and ($root.Contains('/') -or $root -ieq 'Assetpacks')) -or
            ($p.kind -eq 'assetpack' -and $root -cne "Assetpacks/$($p.group)" -and -not $root.StartsWith("Assetpacks/$($p.group)/", [StringComparison]::Ordinal))) { throw "Invalid root: $root" }
        if (@($exclusiveRoots | Where-Object { $_.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or $_.StartsWith($root + '/', [StringComparison]::OrdinalIgnoreCase) -or $root.StartsWith($_ + '/', [StringComparison]::OrdinalIgnoreCase) }).Count) { throw "Overlapping exclusive root: $root" }
        $exclusiveRoots.Add($root)
    }
    $file = Join-Path $ModsDir $p.asset
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or (Get-Item -LiteralPath $file).Length -ne $p.size -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -cne $p.sha256) { throw "Archive size/hash mismatch: $($p.asset)" }
    $declared = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($s in $p.sharedFiles) {
        Assert-PackagePath $s.path
        if ($p.kind -ne 'assetpack' -or -not $s.path.StartsWith("Assetpacks/$($p.group)/About/", [StringComparison]::Ordinal) -or -not $declared.Add($s.path) -or $s.sha256 -cnotmatch '^[a-f0-9]{64}$') { throw "Invalid shared declaration: $($s.path)" }
        # every package declaring a shared file must declare the same bytes (one coordinated group publish)
        if ($sharedFiles.ContainsKey($s.path)) {
            $prior = $sharedFiles[$s.path]
            if ($prior.sha256 -cne $s.sha256 -or $prior.size -ne $s.size -or $prior.group -cne $p.group) { throw "Conflicting shared file: $($s.path)" }
        } else { $sharedFiles.Add($s.path, @{ sha256 = $s.sha256; size = $s.size; group = $p.group }) }
    }
    $dependencySet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($dependency in $p.dependencies) {
        if ([string]::IsNullOrWhiteSpace($dependency) -or $dependency -ceq $p.modId -or -not $dependencySet.Add($dependency)) { throw "Invalid/duplicate/self dependency: $($p.id)" }
    }
}
$visiting = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$visited = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
function Visit-Mod($p) {
    if ($visited.Contains($p.modId)) { return }
    if (-not $visiting.Add($p.modId)) { throw "Dependency cycle: $($p.modId)" }
    foreach ($dep in $p.dependencies) {
        if (-not $modIds.ContainsKey($dep)) { throw "Missing catalog dependency $dep for $($p.id)" }
        Visit-Mod $modIds[$dep]
    }
    $visiting.Remove($p.modId) | Out-Null
    $visited.Add($p.modId) | Out-Null
}
foreach ($p in $modIds.Values) { Visit-Mod $p }
Write-Host "Validated $($catalog.packages.Count) schema-2 packages"
