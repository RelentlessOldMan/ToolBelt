#!/usr/bin/env bash
# Runs chosen core test folders on .NET Framework 4.8 against the netstandard2.0 build of the library, to catch
# runtime differences (invariant casing, Uri limits, Random seeding, "R" formatting) that the net8.0 suite can't see.
# Usage: bash scripts/net48.sh <test folder>... [-- <runner args>]
#   e.g. bash scripts/net48.sh Text Identifiers Enums Guards
# Folders are under tests/ToolBelt.Tests. A test file that uses net8-only APIs won't compile here; leave its folder
# out or run the others. The test project lives in a scratch folder ($TMPDIR/toolbelt-net48).
set -o pipefail
cd "$(dirname "$0")/.."
REPO=$(cygpath -m "$(pwd)" 2>/dev/null || pwd)
# The path goes into the csproj: escape MSBuild's special characters (% first), then XML's.
REPO=$(printf '%s' "$REPO" | sed -e 's/%/%25/g' -e 's/;/%3B/g' -e 's/\$/%24/g' -e 's/@/%40/g' -e "s/'/%27/g"   -e 's/\*/%2A/g' -e 's/?/%3F/g' -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' -e 's/"/\&quot;/g')
DIR=${TMPDIR:-/tmp}/toolbelt-net48
mkdir -p "$DIR"
export DOTNET_CLI_USE_MSBUILD_SERVER=0

folders=()
while [ $# -gt 0 ] && [ "$1" != "--" ]; do folders+=("$1"); shift; done
[ "$1" = "--" ] && shift
[ ${#folders[@]} -gt 0 ] || { echo "usage: $0 <test folder>... [-- <runner args>]"; exit 2; }
for f in "${folders[@]}"; do
  [ -d "tests/ToolBelt.Tests/$f" ] || { echo "no test folder tests/ToolBelt.Tests/$f"; exit 2; }
done

{
  echo '<Project Sdk="Microsoft.NET.Sdk">'
  echo '  <PropertyGroup>'
  echo '    <OutputType>Exe</OutputType><TargetFramework>net48</TargetFramework><LangVersion>latest</LangVersion>'
  echo '    <Nullable>enable</Nullable><ImplicitUsings>disable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
  echo '    <DefineConstants>$(DefineConstants);TOOLBELT_INVARIANTS</DefineConstants>'
  echo '    <NoWarn>$(NoWarn);CS8600;CS8602;CS8604;CS8625;CS8618;CS8603</NoWarn>'
  echo '  </PropertyGroup>'
  echo '  <ItemGroup>'
  echo "    <Compile Include=\"$REPO/tests/ToolBelt.Tests/Program.cs\" />"
  echo "    <Compile Include=\"$REPO/tests/ToolBelt.Tests/Framework/*.cs\" />"
  for f in "${folders[@]}"; do echo "    <Compile Include=\"$REPO/tests/ToolBelt.Tests/$f/**/*.cs\" />"; done
  echo "    <ProjectReference Include=\"$REPO/src/ToolBelt/ToolBelt.csproj\" />"
  # Framework assemblies the tests use as references (zip readers, HttpClient, dynamic for the Word automation test); built into net8.0.
  echo '    <Reference Include="System.IO.Compression" /><Reference Include="System.IO.Compression.FileSystem" /><Reference Include="System.Net.Http" /><Reference Include="Microsoft.CSharp" />'
  echo '  </ItemGroup>'
  echo '</Project>'
} > "$DIR/net48.csproj"

# The library itself builds in place (src/ToolBelt/bin), so don't run this beside another build of the repo.
dotnet build "$DIR/net48.csproj" -c Release --nologo -p:UseSharedCompilation=false > "$DIR/build.log" 2>&1 \
  || { grep -E " error " "$DIR/build.log" | sort -u | head -20; echo "build failed; see $DIR/build.log"; exit 1; }
timeout 1200 "$DIR/bin/Release/net48/net48.exe" "$@" < /dev/null
