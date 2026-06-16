param(
    [switch]$SkipRelease
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $root 'projects\XIV-Mini-Util\XivMiniUtil.csproj'
$logicTests = Join-Path $root 'tools\CharaSelectLogicTests\CharaSelectLogicTests.csproj'

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
    Invoke-Step 'Debug build' {
        dotnet build $project -p:DevPluginOutputDir=
    }

    if (-not $SkipRelease) {
        Invoke-Step 'Release build' {
            dotnet build $project -c Release -p:DevPluginOutputDir=
        }
    }

    Invoke-Step 'CharaSelect logic tests' {
        dotnet run --project $logicTests
    }

    Invoke-Step 'Whitespace diff check' {
        git diff --check
    }
}
finally {
    Pop-Location
}
