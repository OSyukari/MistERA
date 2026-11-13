param(
    [Parameter(Mandatory = $true)][string]$BuildDir,
    [string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '../Tools/MistEra.Updater/MistEra.Updater.csproj'
$sdkDirectory = Split-Path $project
if (-not (Test-Path -LiteralPath $BuildDir -PathType Container)) {
    throw 'BuildDir must be an existing Unity build directory.'
}
$output = Join-Path (Resolve-Path -LiteralPath $BuildDir).Path 'UpdateTools'
# Running in the project directory makes the pinned global.json authoritative.
Push-Location $sdkDirectory
try {
    & dotnet publish $project --configuration $Configuration --runtime win-x64 --self-contained true --output $output -p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) { throw "Updater publish failed ($LASTEXITCODE)." }
    if (-not (Test-Path -LiteralPath (Join-Path $output 'MistEra.Updater.exe'))) {
        throw 'Published updater executable is missing.'
    }
    $runtime = Get-Content -LiteralPath (Join-Path $output 'MistEra.Updater.runtimeconfig.json') -Raw | ConvertFrom-Json
    if (-not $runtime.runtimeOptions.includedFrameworks -or $runtime.runtimeOptions.framework -or
        -not (Test-Path -LiteralPath (Join-Path $output 'coreclr.dll'))) {
        throw 'Updater publish is not self-contained.'
    }
} finally {
    Pop-Location
}
