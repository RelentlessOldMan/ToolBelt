# Build wrapper for Windows. Disables the persistent build server (a lingering server can make a
# wrapping shell or CI step appear to hang). Build ONE project at a time — overlapping builds deadlock
# on intermediate-output locks and present as a mysteriously slow build.
[CmdletBinding()]
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_USE_MSBUILD_SERVER = "0"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Building ToolBelt.sln ($Configuration)..." -ForegroundColor Cyan
dotnet build "$repoRoot\ToolBelt.sln" -c $Configuration --nologo -p:UseSharedCompilation=false
exit $LASTEXITCODE
