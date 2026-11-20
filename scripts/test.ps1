# Test wrapper for Windows. Builds once, then launches the compiled test executable DIRECTLY rather than
# via `dotnet run` — the run command's lingering build server can make the shell/CI step appear to hang
# long after the tests have finished. `-Configuration <name>` picks the build configuration (default Debug); every
# other argument is forwarded to the runner untouched (e.g. --filter Guard --verbose, --list, -v). A `--` separator is
# accepted and ignored. Arguments are parsed by hand, not by a param() block: PowerShell would otherwise bind a runner
# argument to -Configuration by position or to its own -Verbose/-Debug switches.
# Each runner exits 3 when a filter matches none of its tests; that is fine for one suite (the filter names tests in
# another), but a filter that matches nothing in any suite fails the run, so a typo does not pass as green.
# Each runner gets 20 minutes; a hung one is killed and the run fails with exit 124, as verify.sh's timeout does.
$Configuration = "Debug"
$RunnerArgs = @()
for ($i = 0; $i -lt $args.Count; $i++) {
    $a = [string]$args[$i]
    if ($a -eq "-Configuration") {
        # With no value after it, the flag went to every runner as a filter and the build ran in Debug; a value that is
        # itself a flag ("-Configuration -Configuration Release") built into bin\-Configuration and died at the launch.
        if ($i + 1 -ge $args.Count -or ([string]$args[$i + 1]).StartsWith("-")) {
            Write-Host "-Configuration needs a configuration name after it (Debug or Release)." -ForegroundColor Red
            exit 2
        }
        $i++; $Configuration = [string]$args[$i]
    }
    elseif ($a -ne "--") { $RunnerArgs += $a }
}

$ErrorActionPreference = "Stop"
$env:DOTNET_CLI_USE_MSBUILD_SERVER = "0"
$repoRoot = Split-Path -Parent $PSScriptRoot
$testProj = "$repoRoot\tests\ToolBelt.Tests\ToolBelt.Tests.csproj"

# Runs a test exe in this console and returns its exit code, or 124 after killing it at the time limit (a hung test
# once held the shell forever). Each argument is quoted for the command line, so a filter with a space stays one.
function Invoke-Runner([string]$exe, [string[]]$runnerArgs) {
    $quoted = @($runnerArgs | ForEach-Object { '"' + (($_ -replace '(\\*)"', '$1$1\"') -replace '(\\+)$', '$1$1') + '"' })
    $psi = New-Object System.Diagnostics.ProcessStartInfo($exe, ($quoted -join ' '))
    $psi.UseShellExecute = $false
    $p = [System.Diagnostics.Process]::Start($psi)
    try {
        if (-not $p.WaitForExit(20 * 60 * 1000)) {
            try { $p.Kill() } catch { }
            Write-Host "$exe timed out after 20 minutes" -ForegroundColor Red
            return 124
        }
        $p.WaitForExit()
        return $p.ExitCode
    } finally { $p.Dispose() }
}

Write-Host "Building test suite ($Configuration)..." -ForegroundColor Cyan
dotnet build $testProj -c $Configuration --nologo -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = "$repoRoot\tests\ToolBelt.Tests\bin\$Configuration\net8.0\ToolBelt.Tests.exe"
Write-Host "Running $exe" -ForegroundColor Cyan
$mainExit = Invoke-Runner $exe $RunnerArgs
$matched = $mainExit -ne 3
if ($mainExit -eq 3) { $mainExit = 0 }

# Windows-only satellites (net8.0-windows): platform + WPF + WinForms. Only meaningful on Windows; skip elsewhere.
if ($IsWindows -or $env:OS -eq "Windows_NT") {
    $satellites = @("ToolBelt.Windows.Tests", "ToolBelt.Wpf.Tests", "ToolBelt.WinForms.Tests")
    foreach ($name in $satellites) {
        $proj = "$repoRoot\tests\$name\$name.csproj"
        Write-Host "Building $name ($Configuration)..." -ForegroundColor Cyan
        dotnet build $proj -c $Configuration --nologo -p:UseSharedCompilation=false
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        $exe = "$repoRoot\tests\$name\bin\$Configuration\net8.0-windows\$name.exe"
        Write-Host "Running $exe" -ForegroundColor Cyan
        $rc = Invoke-Runner $exe $RunnerArgs
        if ($rc -eq 3) { continue }
        $matched = $true
        if ($rc -ne 0) { exit $rc }
    }
}

if (-not $matched) {
    Write-Host "No test in any suite matches the filter." -ForegroundColor Red
    exit 1
}

exit $mainExit
