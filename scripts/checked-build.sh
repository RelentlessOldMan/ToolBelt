#!/usr/bin/env bash
# Overflow-checked build: runs the core test suite against a library built with CheckForOverflowUnderflow=true, the
# way a drop-in user with checked arithmetic would compile it. Test code stays unchecked (the property goes into
# src/ToolBelt/ToolBelt.csproj only).
#
# Works in a scratch git worktree (default: $TMPDIR/toolbelt-checked-wt, or pass a path, relative to where you run it).
# Each run resets it to HEAD and copies in the uncommitted changes under src/ and tests/, so it checks the working tree
# as it stands (a staged rename is a delete plus an add, so the old name goes). The reset discards everything in that folder, so the script only reuses a folder holding the
# .toolbelt-checked-wt marker it writes when it creates one; any other existing folder is refused. Building
# in place doesn't work (duplicate assembly attributes), and --no-incremental is needed or MSBuild skips the
# recompile. Prints the summary and every FAIL line; exits non-zero unless the suite ran to an ALL GREEN summary
# (a crash, a stack overflow or the 20-minute timeout fails it too).
set -o pipefail
WT=${1:-${TMPDIR:-/tmp}/toolbelt-checked-wt}
case "$WT" in /* | [A-Za-z]:*) ;; *) WT="$(pwd)/$WT" ;; esac
cd "$(dirname "$0")/.."
REPO=$(pwd)
MARK=.toolbelt-checked-wt
export DOTNET_CLI_USE_MSBUILD_SERVER=0

git worktree prune   # forget a scratch worktree whose folder a temp cleanup removed
if [ -e "$WT" ]; then
  [ -f "$WT/$MARK" ] || { echo "$WT exists and is not a checked-build scratch worktree (no $MARK); refusing to reset it"; exit 2; }
else
  git worktree add -q --detach "$WT" HEAD || exit 1
  touch "$WT/$MARK"
fi
git -C "$WT" checkout -q -f --detach "$(git rev-parse HEAD)" || exit 1
git -C "$WT" clean -fdq -- src tests
{ git diff -z --name-only --no-renames HEAD -- src tests; git ls-files -z --others --exclude-standard -- src tests; } |
while IFS= read -r -d '' f; do
  if [ -f "$f" ]; then mkdir -p "$(dirname "$WT/$f")"; cp "$f" "$WT/$f"; else rm -f "$WT/$f"; fi
done

cd "$WT"
sed -i 's#<LangVersion>latest</LangVersion>#<LangVersion>latest</LangVersion>\n    <CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>#' src/ToolBelt/ToolBelt.csproj
grep -q CheckForOverflowUnderflow src/ToolBelt/ToolBelt.csproj || { echo "could not add CheckForOverflowUnderflow"; exit 1; }

dotnet build tests/ToolBelt.Tests -c Release -f net8.0 --no-incremental --nologo -p:UseSharedCompilation=false \
  > "$WT/checked-build.log" 2>&1 || { grep -E " error " "$WT/checked-build.log" | sort -u | head; echo "build failed"; exit 1; }
timeout 1200 tests/ToolBelt.Tests/bin/Release/net8.0/ToolBelt.Tests.exe < /dev/null > "$WT/checked-run.log" 2>&1
rc=$?
tail -1 "$WT/checked-run.log"
grep "^FAIL" "$WT/checked-run.log" | sort -u
[ $rc -eq 124 ] && echo "timed out after 20 minutes"
[ $rc -eq 0 ] && tail -1 "$WT/checked-run.log" | grep -q "^ALL GREEN" || { echo "checked build: FAILED (exit $rc); see $WT/checked-run.log"; exit 1; }
exit 0
