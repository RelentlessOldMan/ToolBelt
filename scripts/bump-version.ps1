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
    # Spaces or a newline around the value (an indented <Version>) once failed the match and reset N to 0, reusing a version.
    $current = $current.Trim()
    if ($current -match '^(\d{1,18})\.(\d{1,18})\.(\d{1,18})\.(\d{1,18})$') {
        $y, $m, $d, $n = [long]$Matches[1], [long]$Matches[2], [long]$Matches[3], [long]$Matches[4]
        # An impossible date (2026.13.1) counted as later than today, so N went on bumping on it until the next year.
        if ($y -lt 1 -or $y -gt 9999 -or $m -lt 1 -or $m -gt 12 -or $d -lt 1 -or $d -gt [DateTime]::DaysInMonth($y, $m)) {
            throw "Unrecognised <Version> '$current': $y-$m-$d is not a date"
        }
        if ($y * 10000 + $m * 100 + $d -ge $todayKey) {      # same day (or a later date): bump N
            # A version part above 65534 fails the build (CS7034), and the bad version was already written back.
            if ($n -ge 65534) { throw "<Version> '$current' has used up the build numbers for its date (65534 at most)" }
            return "$y.$m.$d.$($n + 1)"
        }
    }
    elseif ($current -notmatch '^0\.\d+\.0$') {             # neither a date version nor an old-style 0.N.0
        throw "Unrecognised <Version> '$current'; expected YYYY.M.D.N"
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
