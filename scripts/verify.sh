#!/usr/bin/env bash
# Full verify: builds ToolBelt.sln (Release), runs the four test suites, then the Math.NET / mpmath cross-checks
# (tests/ToolBelt.MathChecks, outside the solution) and the external-consumer smoke (samples/ConsumerSmoke, outside it
# too) on net8.0, net48 and net8.0-windows with the satellites. Prints one summary line per suite; each suite's full output goes
# to <log dir>/verify-<suite>.log (default: $TMPDIR or /tmp; a relative path is from where you run it). Exits non-zero if the build or any suite fails.
# Run from Git Bash on Windows (the satellites target net8.0-windows). Each suite, MathChecks included, is capped at
# 15 minutes so a hang shows up as a failure instead of a stalled run.
set -o pipefail
LOG=${1:-${TMPDIR:-/tmp}}
case "$LOG" in /* | [A-Za-z]:*) ;; *) LOG="$(pwd)/$LOG" ;; esac
cd "$(dirname "$0")/.."
mkdir -p "$LOG"
export DOTNET_CLI_USE_MSBUILD_SERVER=0
rc=0

dotnet build ToolBelt.sln -c Release --nologo -p:UseSharedCompilation=false > "$LOG/verify-build.log" 2>&1 || rc=1
grep -E "warning|error" "$LOG/verify-build.log" | sort -u | tail -8
# The counts and the time from the whole log: tail -3 always cut the Warning(s) line off.
echo "build: $(grep -E '^ *[0-9]+ (Warning|Error)\(s\)|Time Elapsed' "$LOG/verify-build.log" | sed 's/^ *//' | tr '\n' ' ')"
[ $rc -eq 0 ] || { echo "build failed; see $LOG/verify-build.log"; exit 1; }

# Each suite's own framework: a leftover folder from another TFM would otherwise be picked up first.
for s in ToolBelt.Tests:net8.0 ToolBelt.Windows.Tests:net8.0-windows ToolBelt.Wpf.Tests:net8.0-windows ToolBelt.WinForms.Tests:net8.0-windows; do
  p=${s%%:*}; exe=tests/$p/bin/Release/${s#*:}/$p.exe
  if [ ! -f "$exe" ]; then echo "$p: no test exe at $exe"; rc=1; continue; fi
  timeout 900 "$exe" < /dev/null > "$LOG/verify-$p.log" 2>&1 || rc=1
  echo "$p: $(tail -1 "$LOG/verify-$p.log")"
done

timeout 900 dotnet run --project tests/ToolBelt.MathChecks -c Release -p:UseSharedCompilation=false < /dev/null > "$LOG/verify-MathChecks.log" 2>&1 || rc=1
echo "MathChecks: $(tail -1 "$LOG/verify-MathChecks.log")"

if dotnet build samples/ConsumerSmoke -c Release --nologo -p:UseSharedCompilation=false > "$LOG/verify-ConsumerSmoke-build.log" 2>&1; then
  for t in net8.0 net48 net8.0-windows; do
    timeout 300 samples/ConsumerSmoke/bin/Release/$t/ConsumerSmoke.exe < /dev/null > "$LOG/verify-ConsumerSmoke-$t.log" 2>&1 || rc=1
    echo "ConsumerSmoke $t: $(tail -1 "$LOG/verify-ConsumerSmoke-$t.log")"
  done
else
  rc=1; echo "ConsumerSmoke: build failed; see $LOG/verify-ConsumerSmoke-build.log"
fi
exit $rc
