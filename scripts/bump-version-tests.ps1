# Randomized check of bump-version.ps1 against an independent model: a copy of the script runs against a scratch
# Directory.Build.props, and each result (the next version, or a refusal that leaves the file as it was) is graded.
# Prints one summary line; exits 1 on any failure. verify.sh runs it.
[CmdletBinding()]
param([string]$Script, [int]$Trials = 600, [int]$Seed = 1761)

$ErrorActionPreference = "Stop"
if (-not $Script) { $Script = Join-Path $PSScriptRoot "bump-version.ps1" }   # $PSScriptRoot is empty in a param default on 5.1
$root = Join-Path ([System.IO.Path]::GetTempPath()) ("bump-version-tests-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path (Join-Path $root "scripts") | Out-Null
$copy = Join-Path $root "scripts\bump-version.ps1"
Copy-Item $Script $copy
$props = Join-Path $root "Directory.Build.props"
$rng = New-Object System.Random $Seed
$failures = 0

function Pick($items) { $items[$rng.Next($items.Count)] }

try {
    for ($t = 0; $t -lt $Trials; $t++) {
        $today = Get-Date
        $y = Pick @($today.Year, $today.Year, ($today.Year - 1), ($today.Year + 1), 0, 9999, 10000)
        $m = Pick @($today.Month, $rng.Next(0, 14))
        $d = Pick @($today.Day, $rng.Next(0, 33))
        $n = Pick @(0, $rng.Next(0, 100), 65532, 65533, 65534, 65535, 2147483647)
        $current = "$y.$m.$d.$n"
        $text = "<Project>`n  <PropertyGroup>`n    <Version>$current</Version>`n  </PropertyGroup>`n</Project>`n"
        [System.IO.File]::WriteAllText($props, $text)

        # The model: the date must exist; a date before today restarts at today's .0; otherwise N goes up by one, and
        # no version part may pass 65534 (the compiler's limit).
        $valid = $true
        try { [void](New-Object DateTime $y, $m, $d) } catch { $valid = $false }
        $expected = $null
        if ($valid) {
            if ((New-Object DateTime $y, $m, $d) -lt $today.Date) { $expected = "$($today.Year).$($today.Month).$($today.Day).0" }
            elseif ($n + 1 -le 65534) { $expected = "$y.$m.$d.$($n + 1)" }
        }

        $got = $null; $threw = $false
        try { $got = (& $copy) | Select-Object -Last 1 } catch { $threw = $true }
        $after = [System.IO.File]::ReadAllText($props)
        if ($null -eq $expected) {
            if (-not $threw -or $after -ne $text) {
                $failures++; Write-Output "FAIL $current : expected a refusal with the file unchanged, got '$got'"
            }
        }
        elseif ($threw -or $got -ne $expected -or $after -ne $text.Replace(">$current<", ">$expected<")) {
            $failures++; Write-Output "FAIL $current : expected $expected, got '$got' (threw: $threw)"
        }
    }
}
finally { Remove-Item -Recurse -Force $root }

Write-Output "$Trials trials, $failures failed"
if ($failures -ne 0) { exit 1 }
