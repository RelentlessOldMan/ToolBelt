# Code-coverage wrapper for Windows. ToolBelt's test runner is a plain console exe (no xUnit/VSTest), so
# the usual `dotnet test --collect` path does not apply — we instrument with the coverlet.console GLOBAL
# tool (zero package references added to the BCL-only library) and drive the compiled runner directly.
# Only production code is measured (--include "[ToolBelt]*"); the test assembly is excluded.
#
# Requires two global tools (install once; this script checks and tells you if they're missing):
#   dotnet tool install --global coverlet.console
#   dotnet tool install --global dotnet-reportgenerator-globaltool
#
# Output (all under coverage/, which is gitignored):
#   coverage/coverage.cobertura.xml   machine-readable
#   coverage/html/index.html          human-readable drill-down
[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [switch]$NoHtml
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_USE_MSBUILD_SERVER = "0"
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProj = "$repoRoot\tests\ToolBelt.Tests\ToolBelt.Tests.csproj"
$testDll  = "$repoRoot\tests\ToolBelt.Tests\bin\$Configuration\net8.0\ToolBelt.Tests.dll"
$outDir   = "$repoRoot\coverage"
$cobertura = "$outDir\coverage.cobertura.xml"

if (-not (Get-Command coverlet -ErrorAction SilentlyContinue)) {
    Write-Error "coverlet.console not found. Install it: dotnet tool install --global coverlet.console"
}

Write-Host "Building test suite ($Configuration)..." -ForegroundColor Cyan
dotnet build $testProj -c $Configuration --nologo -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Force $outDir | Out-Null

Write-Host "Collecting coverage (production code only)..." -ForegroundColor Cyan
coverlet $testDll --target "dotnet" --targetargs "$testDll" `
    --format cobertura --output $cobertura --include "[ToolBelt]*"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $NoHtml) {
    if (Get-Command reportgenerator -ErrorAction SilentlyContinue) {
        reportgenerator -reports:$cobertura -targetdir:"$outDir\html" -reporttypes:"Html;MarkdownSummaryGithub" | Out-Null
        Write-Host "HTML report: $outDir\html\index.html" -ForegroundColor Green
    } else {
        Write-Host "reportgenerator not found; skipping HTML (install: dotnet tool install --global dotnet-reportgenerator-globaltool)" -ForegroundColor Yellow
    }
}
