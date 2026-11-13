#!/usr/bin/env bash
# Overflow-checked build: runs the core test suite against a library built with CheckForOverflowUnderflow=true, the
# way a drop-in user with checked arithmetic would compile it. Test code stays unchecked (the property goes into
# src/ToolBelt/ToolBelt.csproj only).
#
# Works in a scratch git worktree (default: $TMPDIR/toolbelt-checked-wt, or pass a path). Each run resets it to HEAD
# and copies in the uncommitted changes under src/ and tests/, so it checks the working tree as it stands. Building
# in place doesn't work (duplicate assembly attributes), and --no-incremental is needed or MSBuild skips the
# recompile. Prints every FAIL line; exits non-zero if there is one. The expected result is no failures.
set -o pipefail
cd "$(dirname "$0")/.."
REPO=$(pwd)
WT=${1:-${TMPDIR:-/tmp}/toolbelt-checked-wt}
export DOTNET_CLI_USE_MSBUILD_SERVER=0

[ -d "$WT/.git" ] || [ -f "$WT/.git" ] || git worktree add -q --detach "$WT" HEAD || exit 1
git -C "$WT" checkout -q -f --detach "$(git rev-parse HEAD)" || exit 1
git -C "$WT" clean -fdq -- src tests
for f in $(git diff --name-only HEAD -- src tests) $(git ls-files --others --exclude-standard -- src tests); do
  if [ -f "$f" ]; then mkdir -p "$(dirname "$WT/$f")"; cp "$f" "$WT/$f"; else rm -f "$WT/$f"; fi
done

cd "$WT"
sed -i 's#<LangVersion>latest</LangVersion>#<LangVersion>latest</LangVersion>\n    <CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>#' src/ToolBelt/ToolBelt.csproj
grep -q CheckForOverflowUnderflow src/ToolBelt/ToolBelt.csproj || { echo "could not add CheckForOverflowUnderflow"; exit 1; }

dotnet build tests/ToolBelt.Tests -c Release -f net8.0 --no-incremental --nologo -p:UseSharedCompilation=false \
  > "$WT/checked-build.log" 2>&1 || { grep -E " error " "$WT/checked-build.log" | sort -u | head; echo "build failed"; exit 1; }
timeout 1200 tests/ToolBelt.Tests/bin/Release/net8.0/ToolBelt.Tests.exe < /dev/null > "$WT/checked-run.log" 2>&1
tail -1 "$WT/checked-run.log"
if grep "^FAIL" "$WT/checked-run.log" | sort -u; then exit 1; fi
cd "$REPO"
exit 0
