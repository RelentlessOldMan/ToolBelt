#!/usr/bin/env bash
# Full verify: builds ToolBelt.sln (Release), runs the four test suites, then the Math.NET / mpmath cross-checks
# (tests/ToolBelt.MathChecks, outside the solution). Prints one summary line per suite; each suite's full output goes
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
echo "build: $(tail -3 "$LOG/verify-build.log" | grep -E 'Warn|Error|Time' | tr '\n' ' ')"
[ $rc -eq 0 ] || { echo "build failed; see $LOG/verify-build.log"; exit 1; }

for p in ToolBelt.Tests ToolBelt.Windows.Tests ToolBelt.Wpf.Tests ToolBelt.WinForms.Tests; do
  exe=$(ls tests/$p/bin/Release/*/$p.exe 2>/dev/null | head -1)
  if [ -z "$exe" ]; then echo "$p: no test exe"; rc=1; continue; fi
  timeout 900 "$exe" < /dev/null > "$LOG/verify-$p.log" 2>&1 || rc=1
  echo "$p: $(tail -1 "$LOG/verify-$p.log")"
done

timeout 900 dotnet run --project tests/ToolBelt.MathChecks -c Release -p:UseSharedCompilation=false < /dev/null > "$LOG/verify-MathChecks.log" 2>&1 || rc=1
echo "MathChecks: $(tail -1 "$LOG/verify-MathChecks.log")"
exit $rc
