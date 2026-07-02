[CmdletBinding()]
param(
    [string]$DalamudHome = $env:DALAMUD_HOME
)

$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $root 'projects\XIV-Mini-Util\XivMiniUtil.csproj'
$releaseRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $root 'projects\XIV-Mini-Util\bin\Release'))
$stagingDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $releaseRoot 'XivMiniUtil'))
$expectedPrefix = $releaseRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not $stagingDirectory.StartsWith(
        $expectedPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Release staging path escaped the release directory: $stagingDirectory"
}

if (Test-Path -LiteralPath $stagingDirectory) {
    Get-ChildItem -LiteralPath $stagingDirectory -Force -Recurse |
        ForEach-Object {
            $_.Attributes = $_.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)
        }
    $stagingItem = Get-Item -LiteralPath $stagingDirectory -Force
    $stagingItem.Attributes =
        $stagingItem.Attributes -band (-bnot [System.IO.FileAttributes]::ReadOnly)
    Remove-Item -LiteralPath $stagingDirectory -Force -Recurse
}

$previousDalamudHome = $env:DALAMUD_HOME
try {
    if (-not [string]::IsNullOrWhiteSpace($DalamudHome)) {
        $env:DALAMUD_HOME = $DalamudHome
    }

    & dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed with exit code $LASTEXITCODE"
    }
}
finally {
    $env:DALAMUD_HOME = $previousDalamudHome
}

$manifestPath = Join-Path $releaseRoot 'XivMiniUtil.json'
$latestZipPath = Join-Path $stagingDirectory 'latest.zip'
foreach ($artifact in @($manifestPath, $latestZipPath)) {
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "Release artifact was not generated: $artifact"
    }
}

Write-Host "Release package verified:"
Write-Host "  $latestZipPath"
Write-Host "  $manifestPath"
