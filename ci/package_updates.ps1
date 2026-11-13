param(
    [Parameter(Mandatory)][string]$BuildDir,
    [Parameter(Mandatory)][string]$ReleaseTag,
    [string]$OutputDir = '.',
    [ValidateSet('full', 'safe')][string]$Channel = 'full',
    [long]$PreviousBuildNumber = 0
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $BuildDir).Path.TrimEnd('\', '/')
$output = [IO.Path]::GetFullPath($OutputDir)
if ($output.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $output -eq $root) { throw 'OutputDir must be outside BUILD_DIR' }
$infoPath = Join-Path $root 'build-info.json'
if (-not (Test-Path -LiteralPath $infoPath -PathType Leaf)) { throw 'Prebuilt BUILD_DIR has no build-info.json. Rebuild in Unity; do not mint a release identity for an old build.' }
$info = Get-Content -LiteralPath $infoPath -Raw | ConvertFrom-Json
if ($info.schema -ne 1 -or $info.buildId -notmatch '^[0-9]+-[a-f0-9]{32}$' -or $info.buildNumber -le $PreviousBuildNumber -or
    $info.platform -ne 'windows-x64' -or $info.channel -ne $Channel -or $info.minimumUpdaterProtocol -ne 1 -or [string]::IsNullOrWhiteSpace($info.version)) { throw 'Invalid, reused, non-increasing, or wrong-channel embedded build identity' }
if ($info.buildId.Split('-')[0] -ne [string]$info.buildNumber) { throw 'buildId timestamp does not match buildNumber' }
if ($info.executable -notmatch '^[^\\/:*?"<>|]+\.exe$' -or -not (Test-Path -LiteralPath (Join-Path $root $info.executable) -PathType Leaf)) { throw 'Embedded executable must name the actual Windows player at build root' }
$helper = Join-Path $root 'UpdateTools/MistEra.Updater.exe'
if (-not (Test-Path -LiteralPath $helper -PathType Leaf)) { throw 'Missing helper. Publish the self-contained win-x64 updater into BUILD_DIR/UpdateTools before packaging.' }
$runtimeConfig = Join-Path $root 'UpdateTools/MistEra.Updater.runtimeconfig.json'
if (-not (Test-Path -LiteralPath $runtimeConfig)) { throw 'Missing helper runtime configuration; publish self-contained win-x64, not a framework-dependent executable' }
$runtime = Get-Content -LiteralPath $runtimeConfig -Raw | ConvertFrom-Json
if (-not $runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.framework -or -not (Test-Path -LiteralPath (Join-Path $root 'UpdateTools/coreclr.dll'))) { throw 'Updater helper is not a self-contained .NET publish' }
$helperAssembly = Join-Path $root 'UpdateTools/MistEra.Updater.dll'
$helperDeps = Join-Path $root 'UpdateTools/MistEra.Updater.deps.json'
if (-not (Test-Path -LiteralPath $helperAssembly) -or -not (Test-Path -LiteralPath $helperDeps)) { throw 'Missing helper application assembly/dependency manifest' }
if ([Reflection.AssemblyName]::GetAssemblyName($helperAssembly).Name -cne 'MistEra.Updater') { throw 'Helper application assembly identity is not MistEra.Updater' }
$deps = Get-Content -LiteralPath $helperDeps -Raw | ConvertFrom-Json
if ($deps.runtimeTarget.name -notmatch '/win-x64$' -or -not @($deps.libraries.PSObject.Properties.Name | Where-Object { $_ -cmatch '^MistEra\.Updater/' }).Count) { throw 'Helper dependency manifest does not identify a win-x64 MistEra.Updater publish' }
$stream = [IO.File]::OpenRead($helper)
try {
    $reader = [IO.BinaryReader]::new($stream)
    if ($reader.ReadUInt16() -ne 0x5a4d) { throw 'Updater helper is not a Windows executable' }
    $stream.Position = 0x3c; $pe = $reader.ReadInt32(); $stream.Position = $pe
    if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw 'Updater helper must be Windows x64' }
} finally { $stream.Dispose() }
New-Item -ItemType Directory -Force -Path $output | Out-Null
$asset = "MistEra-$($info.buildId)-windows-x64-update.zip"
$archive = Join-Path $output $asset
$temporary = Join-Path $output ('.package-' + [guid]::NewGuid().ToString('N') + '.zip')
$files = [Collections.Generic.List[object]]::new()
$seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
function Add-Payload($directory, [string]$relative = '') {
    foreach ($item in Get-ChildItem -LiteralPath $directory -Force | Sort-Object Name) {
        $path = if ($relative) { "$relative/$($item.Name)" } else { $item.Name }
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point in build: $path" }
        if ($path -match '(^|/)(Save|\.updates|\.git|Library|Temp|Logs|obj|bin|Backups?|[^/]*_BackUpThisFolder_ButDontShipItWithYourGame|[^/]*_BurstDebugInformation_DoNotShip)(/|$)' -or
            $path -match '(?i)\.(meta|pdb|texcache|tmp|bak|old|log)$' -or $path -eq 'game-files.json') { continue }
        if ($path -like 'Mods/*' -and $path -notin @('Mods/Assetpacks', 'Mods/Assetpacks/Image_Generation_Config.json')) { continue }
        if ($item.PSIsContainer) { Add-Payload $item.FullName $path; continue }
        if ($path -match '(^|/)(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])([.]|/|$)|[<>:"|?*]|[. ](/|$)' -or -not $seen.Add($path)) { throw "Unsafe or case-colliding path: $path" }
        $files.Add([ordered]@{ path = $path; sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); size = $item.Length })
    }
}
Add-Payload $root
$inventory = [ordered]@{ schema = 1; files = @($files.ToArray()) } | ConvertTo-Json -Depth 10
# This inventory is also shipped with the manual artifact for first-install ownership tracking.
[IO.File]::WriteAllText((Join-Path $root 'game-files.json'), $inventory, [Text.UTF8Encoding]::new($false))
try {
    $zip = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $source = Join-Path $root $file.path
            $entry = $zip.CreateEntry("game/$($file.path)", [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $input = [IO.File]::OpenRead($source); $destination = $entry.Open()
            try { $input.CopyTo($destination) } finally { $destination.Dispose(); $input.Dispose() }
            if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Build changed while packaging: $($file.path)" }
        }
        $entry = $zip.CreateEntry('game/game-files.json', [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write($inventory) } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    $hash = (Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
    if (Test-Path -LiteralPath $archive) {
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { throw "Immutable artifact already exists with different bytes: $archive" }
    } else { Move-Item -LiteralPath $temporary -Destination $archive }
    $manifest = [ordered]@{}
    foreach ($field in @('schema','buildId','buildNumber','version','platform','channel','minimumUpdaterProtocol','executable')) { $manifest[$field] = $info.$field }
    $manifest.releaseTag = $ReleaseTag; $manifest.asset = $asset; $manifest.size = (Get-Item -LiteralPath $archive).Length
    $manifest.sha256 = $hash; $manifest.archiveFormat = 'zip'; $manifest.payloadRoot = 'game'
    [IO.File]::WriteAllText((Join-Path $output 'game-update.json'), ($manifest | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    Write-Host "Packaged $archive"
} finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
