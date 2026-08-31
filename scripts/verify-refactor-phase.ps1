param(
    [switch]$SkipRelease
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'projects\XIV-Mini-Util\XivMiniUtil.csproj'
$logicTests = Join-Path $root 'tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj'
$releaseBuild = Join-Path $root 'scripts\release-build.ps1'
$releaseRoot = Join-Path $root 'projects\XIV-Mini-Util\bin\Release'
$releaseZip = Join-Path $releaseRoot 'XivMiniUtil\latest.zip'
$releaseManifest = Join-Path $releaseRoot 'XivMiniUtil.json'

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Command
    )

    Write-Host "==> $Name"
    $global:LASTEXITCODE = 0
    & $Command
    if ($global:LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $global:LASTEXITCODE"
    }
}

Push-Location $root
try {
    Invoke-Step 'CharaSelect logic tests' {
        dotnet run --project $logicTests
    }

    Invoke-Step 'Debug build' {
        dotnet build $project -p:DevPluginOutputDir=
    }

    if (-not $SkipRelease) {
        Invoke-Step 'Windows Release package' {
            & $releaseBuild
        }

        foreach ($artifact in @($releaseZip, $releaseManifest)) {
            if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
                throw "Release artifact was not generated: $artifact"
            }
        }
    }

    Invoke-Step 'Whitespace diff check' {
        git diff --check
    }
}
finally {
    Pop-Location
}
