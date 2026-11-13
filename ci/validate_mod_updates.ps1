param([Parameter(Mandatory)][string]$ModsDir)
$ErrorActionPreference = 'Stop'
function Assert-PackagePath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path -match '\\|[<>:"|?*\x00-\x1f]' -or
        @($Path.Split('/') | Where-Object { $_ -in @('', '.', '..') -or $_ -match '[. ]$|^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)' }).Count) { throw "Unsafe package path: $Path" }
}
$catalog = Get-Content -LiteralPath (Join-Path $ModsDir 'catalog-v2.json') -Raw | ConvertFrom-Json
if ($catalog.schema -ne 2 -or $null -eq $catalog.packages) { throw 'Expected schema 2 catalog with packages' }
$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$modIds = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$owners = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
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
    if ($p.archiveFormat -ne 'zip' -or $p.sha256 -cnotmatch '^[a-f0-9]{64}$' -or $p.asset -notmatch ('^[A-Za-z0-9_.-]+-' + $p.sha256 + '\.zip$')) { throw "Expected immutable hash-named ZIP: $($p.id)" }
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
    $shared = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($s in $p.sharedFiles) {
        Assert-PackagePath $s.path
        if ($p.kind -ne 'assetpack' -or -not $s.path.StartsWith("Assetpacks/$($p.group)/About/", [StringComparison]::Ordinal) -or $shared.ContainsKey($s.path) -or $s.sha256 -cnotmatch '^[a-f0-9]{64}$') { throw "Invalid shared declaration: $($s.path)" }
        $shared.Add($s.path, $s)
    }
    $zip = [IO.Compression.ZipFile]::OpenRead($file)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $about = $null
    try {
        foreach ($entry in $zip.Entries) {
            $path = $entry.FullName
            Assert-PackagePath $path
            if (-not $seen.Add($path) -or $path.EndsWith('/') -or (($entry.ExternalAttributes -shr 16) -band 0xf000) -eq 0xa000) { throw "Duplicate/directory/link ZIP entry: $path" }
            if ((-not $shared.ContainsKey($path) -and -not @($p.roots | Where-Object { $path.StartsWith($_ + '/', [StringComparison]::Ordinal) }).Count) -or $path -match '(^|/)(_generated|\.git|backups?)(/|$)|\.(texcache|meta|bak|tmp)$') { throw "File outside roots or forbidden: $path" }
            $input = $entry.Open()
            try { $sha = [Security.Cryptography.SHA256]::Create(); try { $hash = [Convert]::ToHexString($sha.ComputeHash($input)).ToLowerInvariant() } finally { $sha.Dispose() } } finally { $input.Dispose() }
            if ($shared.ContainsKey($path) -and ($shared[$path].sha256 -cne $hash -or $shared[$path].size -ne $entry.Length)) { throw "Shared hash/size mismatch: $path" }
            if ($owners.ContainsKey($path)) {
                $prior = $owners[$path]
                if (-not $shared.ContainsKey($path) -or -not $prior.shared -or $prior.hash -cne $hash -or $prior.size -ne $entry.Length -or $prior.group -cne $p.group) { throw "Conflicting package ownership: $path" }
            } else { $owners.Add($path, @{ hash = $hash; size = $entry.Length; shared = $shared.ContainsKey($path); group = $p.group }) }
            $aboutPath = if ($p.kind -eq 'mod') { "$($p.roots[0])/About/About.json" } else { "Assetpacks/$($p.group)/About/About.json" }
            if ($path -ceq $aboutPath) { $input = [IO.StreamReader]::new($entry.Open()); try { $about = $input.ReadToEnd() | ConvertFrom-Json } finally { $input.Dispose() } }
        }
        foreach ($s in $shared.Keys) { if (-not $seen.Contains($s)) { throw "Declared shared file missing: $s" } }
        foreach ($root in $p.roots) { if (-not @($seen | Where-Object { $_.StartsWith($root + '/', [StringComparison]::Ordinal) }).Count) { throw "Empty payload root: $root" } }
        if ($p.kind -eq 'mod' -and ($null -eq $about -or $about.ModID -cne $p.modId)) { throw "Staged ModID mismatch: $($p.id)" }
        $dependencySet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($dependency in $p.dependencies) {
            if ([string]::IsNullOrWhiteSpace($dependency) -or $dependency -ceq $p.modId -or -not $dependencySet.Add($dependency)) { throw "Invalid/duplicate/self dependency: $($p.id)" }
        }
        if ($about) {
            if ([long]$p.minimumBuild -ne [long]$about.minimumBuild -or [long]$p.maximumBuild -ne [long]$about.maximumBuild -or [string]$p.channel -cne [string]$about.channel) { throw "About compatibility mismatch: $($p.id)" }
            foreach ($field in @('name','description','author','version')) {
                $expected = [string]$about.$field
                if ($field -eq 'name' -and $null -eq $about.$field) { $expected = $p.group }
                if ([string]$p.$field -cne $expected) { throw "About metadata mismatch: $($p.id)/$field" }
            }
            $deps = @($about.dependency | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -cne $p.modId } | Sort-Object -Unique -CaseSensitive)
            if (($deps -join "`n") -cne (@($p.dependencies | Sort-Object -Unique -CaseSensitive) -join "`n")) { throw "About dependencies mismatch: $($p.id)" }
        }
    } finally { $zip.Dispose() }
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
Write-Host "Validated $($catalog.packages.Count) schema-2 packages and their ZIP contents"
