# Test wrapper for Windows. Builds once, then launches the compiled test executable DIRECTLY rather than
# via `dotnet run` — the run command's lingering build server can make the shell/CI step appear to hang
# long after the tests have finished. Any extra args are forwarded to the runner (e.g. -v, --filter Guard).
[CmdletBinding()]
param(
    [string]$Configuration = "Debug",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RunnerArgs
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_USE_MSBUILD_SERVER = "0"
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProj = "$repoRoot\tests\ToolBelt.Tests\ToolBelt.Tests.csproj"

Write-Host "Building test suite ($Configuration)..." -ForegroundColor Cyan
dotnet build $testProj -c $Configuration --nologo -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = "$repoRoot\tests\ToolBelt.Tests\bin\$Configuration\net8.0\ToolBelt.Tests.exe"
Write-Host "Running $exe" -ForegroundColor Cyan
& $exe @RunnerArgs
exit $LASTEXITCODE
