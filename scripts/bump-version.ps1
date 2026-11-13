# Works out the next version (YYYY.M.D.N: today's date, N the build number for that day from 0) from the one in
# Directory.Build.props, writes it back and prints it. Run it once per release, before the release commit; the
# version is the counter, so it never goes backwards: a stored date later than today keeps its date and bumps N.
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$props = Join-Path (Split-Path -Parent $PSScriptRoot) "Directory.Build.props"

function Get-NextVersion([string]$current) {
    $today = Get-Date
    # Year, Month and Day are always Gregorian; ToString('yyyyMMdd') used the culture's calendar (th-TH gave 2569...).
    $todayKey = $today.Year * 10000 + $today.Month * 100 + $today.Day
    if ($current -match '^(\d+)\.(\d+)\.(\d+)\.(\d+)$') {
        $y, $m, $d, $n = [int]$Matches[1], [int]$Matches[2], [int]$Matches[3], [int]$Matches[4]
        if ($y * 10000 + $m * 100 + $d -ge $todayKey) {      # same day (or a later date): bump N
            return "$y.$m.$d.$($n + 1)"
        }
    }
    return "$($today.Year).$($today.Month).$($today.Day).0"   # first build today (or an old-style version)
}

$text = [System.IO.File]::ReadAllText($props)
$m = [regex]::Match($text, '<Version>([^<]*)</Version>')
if (-not $m.Success) { throw "No <Version> in $props" }
$next = Get-NextVersion $m.Groups[1].Value
$text = $text.Substring(0, $m.Groups[1].Index) + $next + $text.Substring($m.Groups[1].Index + $m.Groups[1].Length)
[System.IO.File]::WriteAllText($props, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Output $next
