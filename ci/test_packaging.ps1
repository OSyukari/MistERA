param([string]$SevenZip = 'C:\Program Files\7-Zip\7z.exe')
$ErrorActionPreference = 'Stop'
$temporaryParent = Join-Path $env:LOCALAPPDATA 'Temp/kilo'
if (-not (Test-Path -LiteralPath $temporaryParent)) { throw 'Expected safe temporary parent does not exist' }
$temp = Join-Path $temporaryParent ('packaging-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
function Assert($condition, [string]$message) { if (-not $condition) { throw "Assertion failed: $message" } }
function Put([string]$path, [string]$text) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
    [IO.File]::WriteAllText($path, $text)
}
function Reject([scriptblock]$operation, [string]$message) {
    $failed = $false
    try { & $operation } catch { $failed = $true; Write-Host "Expected rejection ($message): $_" }
    Assert $failed $message
}
try {
    dotnet run --project "$PSScriptRoot/tests/PackagingHarness.csproj" -c Release -- $temp $SevenZip
    if ($LASTEXITCODE -ne 0) { throw 'Editor packaging harness failed' }
    $mods = Join-Path $temp 'Packages'
    & "$PSScriptRoot/validate_mod_updates.ps1" -ModsDir $mods
    $oldMods = $env:MODS_DIR
    try { $env:MODS_DIR = $mods; & "$PSScriptRoot/publish_mods.ps1" -ValidateOnly } finally { $env:MODS_DIR = $oldMods }

    $build = Join-Path $temp 'Game'
    Remove-Item -LiteralPath (Join-Path $build 'UpdateTools') -Recurse -Force
    $output = Join-Path $temp 'Output'
    $number = 1794530000000L
    $info = @{ schema = 1; buildId = "$number-0123456789abcdef0123456789abcdef"; buildNumber = $number; version = 'display-version-not-tag'; platform = 'windows-x64'; channel = 'full'; minimumUpdaterProtocol = 1; executable = 'Actual Game.exe' }
    Put "$build/build-info.json" ($info | ConvertTo-Json)
    Put "$build/Data/payload.json" '{}'
    Put "$build/Save/do-not-ship.txt" 'save'
    Put "$build/Mods/LocalMod/do-not-ship.txt" 'mod'
    Put "$build/Mods/Assetpacks/Image_Generation_Config.json" '{"editable":true}'
    Put "$build/Actual Game_BackUpThisFolder_ButDontShipItWithYourGame/symbols.dat" 'symbols'
    Put "$build/Actual Game_BurstDebugInformation_DoNotShip/a.txt" 'symbols'
    Put "$build/Data/cache.texcache" 'cache'
    Put "$build/.updates/installation.json" 'local'
    Put "$build/backup/old.txt" 'backup'
    $package = Join-Path $PSScriptRoot 'package_updates.ps1'
    Reject { & $package -BuildDir $build -ReleaseTag 'different-tag' -OutputDir $output } 'missing helper'
    Put "$build/UpdateTools/MistEra.Updater.runtimeconfig.json" '{"runtimeOptions":{"includedFrameworks":[{"name":"Microsoft.NETCore.App","version":"10.0.0"}]}}'
    Put "$build/UpdateTools/coreclr.dll" 'synthetic-runtime'
    Copy-Item -LiteralPath "$PSScriptRoot/tests/bin/Release/net10.0/MistEra.Updater.dll" -Destination "$build/UpdateTools/MistEra.Updater.dll"
    Put "$build/UpdateTools/MistEra.Updater.deps.json" '{"runtimeTarget":{"name":".NETCoreApp,Version=v10.0/win-x64"},"libraries":{"MistEra.Updater/1.0.0":{"type":"project"}}}'
    # Minimal PE fixture is data only and is never launched.
    $pe = [byte[]]::new(256); $pe[0] = 0x4d; $pe[1] = 0x5a; $pe[0x3c] = 0x80
    $pe[0x80] = 0x50; $pe[0x81] = 0x45; $pe[0x84] = 0x64; $pe[0x85] = 0x86
    [IO.File]::WriteAllBytes("$build/UpdateTools/MistEra.Updater.exe", $pe)
    Copy-Item -LiteralPath "$PSScriptRoot/tests/bin/Release/net10.0/Newtonsoft.Json.dll" -Destination "$build/UpdateTools/MistEra.Updater.dll" -Force
    Reject { & $package -BuildDir $build -ReleaseTag 'different-tag' -OutputDir $output } 'wrong helper assembly identity'
    Copy-Item -LiteralPath "$PSScriptRoot/tests/bin/Release/net10.0/MistEra.Updater.dll" -Destination "$build/UpdateTools/MistEra.Updater.dll" -Force
    Reject { & $package -BuildDir $build -ReleaseTag 'different-tag' -OutputDir $output -Channel safe } 'wrong channel'
    Reject { & $package -BuildDir $build -ReleaseTag 'different-tag' -OutputDir $output -PreviousBuildNumber $number } 'non-increasing build'
    & $package -BuildDir $build -ReleaseTag 'different-tag' -OutputDir $output
    & $package -BuildDir $build -ReleaseTag 'different-tag' -OutputDir $output
    $release = Get-Content -LiteralPath "$output/game-update.json" -Raw | ConvertFrom-Json
    Assert ($release.releaseTag -eq 'different-tag' -and $release.version -eq 'display-version-not-tag' -and $release.buildId -eq $info.buildId) 'preserved embedded identity, independent release tag'
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $output $release.asset))
    try {
        Assert (@($zip.Entries | Where-Object { -not $_.FullName.StartsWith('game/') }).Count -eq 0) 'canonical game root'
        Assert (@($zip.Entries | Where-Object { $_.FullName -match 'do-not-ship|symbols|texcache|\.updates|backup/' }).Count -eq 0) 'user data and caches excluded'
        Assert ($null -ne $zip.GetEntry('game/Mods/Assetpacks/Image_Generation_Config.json')) 'shipped editable default included'
        $reader = [IO.StreamReader]::new($zip.GetEntry('game/game-files.json').Open())
        try { $inventory = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
        Assert ($inventory.schema -eq 1 -and @($inventory.files | Where-Object path -eq 'game-files.json').Count -eq 0) 'inventory excludes itself'
        Assert ($inventory.files.Count -eq $zip.Entries.Count - 1) 'inventory covers every payload file'
        foreach ($file in $inventory.files) {
            $entry = $zip.GetEntry("game/$($file.path)"); $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { Assert ([Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant() -ceq $file.sha256 -and $entry.Length -eq $file.size) "inventory hash $($file.path)" } finally { $stream.Dispose(); $sha.Dispose() }
        }
    } finally { $zip.Dispose() }
    Assert ((Get-FileHash -LiteralPath (Join-Path $output $release.asset)).Hash.ToLowerInvariant() -ceq $release.sha256) 'release archive hash'
    & "$PSScriptRoot/zip_demo.ps1" -BuildDir $build -Archive (Join-Path $output 'manual.7z')
    $listing = & $SevenZip l -slt (Join-Path $output 'manual.7z')
    if ($LASTEXITCODE -ne 0) { throw 'Manual archive listing failed' }
    Assert (@($listing | Where-Object { $_ -match '^Path = .*?(do-not-ship|symbols|texcache|\.updates|backup)' }).Count -eq 0) 'manual archive excludes user data/caches'
    Put "$build/Data/payload.json" '{"changed":true}'
    Reject { & $package -BuildDir $build -ReleaseTag 'different-tag' -OutputDir $output } 'immutable game archive collision'

    $catalogPath = Join-Path $mods 'catalog-v2.json'
    $catalogText = Get-Content -LiteralPath $catalogPath -Raw
    $catalog = $catalogText | ConvertFrom-Json
    $catalog.packages += $catalog.packages[0]
    Put $catalogPath ($catalog | ConvertTo-Json -Depth 30)
    Reject { & "$PSScriptRoot/validate_mod_updates.ps1" -ModsDir $mods } 'duplicate package identity'
    Put $catalogPath $catalogText
    $catalog = $catalogText | ConvertFrom-Json
    $sharedPackage = @($catalog.packages | Where-Object kind -eq 'assetpack')[0]
    $sharedPackage.sharedFiles[0].sha256 = '0' * 64
    Put $catalogPath ($catalog | ConvertTo-Json -Depth 30)
    Reject { & "$PSScriptRoot/validate_mod_updates.ps1" -ModsDir $mods } 'shared metadata hash mismatch'
    Put $catalogPath $catalogText
    $catalog = $catalogText | ConvertFrom-Json
    $catalog.packages[0].roots = @('../escape')
    Put $catalogPath ($catalog | ConvertTo-Json -Depth 30)
    Reject { & "$PSScriptRoot/validate_mod_updates.ps1" -ModsDir $mods } 'traversal root'
    Put $catalogPath $catalogText
    $catalog = $catalogText | ConvertFrom-Json
    $catalog.packages[0].sha256 = '0' * 64
    Put $catalogPath ($catalog | ConvertTo-Json -Depth 30)
    Reject { & "$PSScriptRoot/validate_mod_updates.ps1" -ModsDir $mods } 'archive identity mismatch'
    Put $catalogPath $catalogText
    & "$PSScriptRoot/tests/TestPublishMods.ps1" -ModsDir $mods
    Write-Host 'All isolated publishing/build identity tests passed. No production build or GitHub release was touched.'
} finally { Remove-Item -LiteralPath $temp -Recurse -Force }
