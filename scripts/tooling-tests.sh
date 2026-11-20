#!/usr/bin/env bash
# Regression checks for the build and reference-generator scripts that need no build: checked-build.sh's handling of
# the worktree path, test.ps1's -Configuration with no name, and gen-incomplete-references.py keeping a row whose
# reference fails. Prints one summary line; exits 1 on any failure. verify.sh runs it.
cd "$(dirname "$0")/.."
T=$(mktemp -d "${TMPDIR:-/tmp}/toolbelt-tooling-tests.XXXXXX") || exit 1
trap 'rm -rf "$T"' EXIT
fail=0; n=0
bad() { echo "FAIL: $*"; fail=$((fail + 1)); }

# A run holding the lock for "$T/wt" must stop every other spelling of that folder at the lock, before anything is
# created: "wt\" made its own lock inside the folder, created the folder, and then refused it.
mkdir "$T/wt.lock"
for spelling in "$T/wt" "$T/wt/" "$T/wt\\" "$T/wt\/" "$T/wt//"; do
  n=$((n + 1))
  out=$(bash scripts/checked-build.sh "$spelling" 2>&1 < /dev/null); rc=$?
  [ $rc -eq 2 ] && [[ $out == *"another checked build"* ]] || bad "checked-build '$spelling': exit $rc: $out"
  [ -e "$T/wt" ] && { bad "checked-build '$spelling' created the folder"; rm -rf "$T/wt"; }
done
rmdir "$T/wt.lock"

# test.ps1: -Configuration with no name after it (or a flag there) is refused before any build.
for argv in "-Configuration" "--filter x -Configuration" "-Configuration -Configuration Release"; do
  n=$((n + 1))
  out=$(timeout 60 powershell -NoProfile -File scripts/test.ps1 $argv 2>&1 < /dev/null); rc=$?
  [ $rc -eq 2 ] && [[ $out == *"needs a configuration name"* ]] || bad "test.ps1 $argv: exit $rc: $(echo "$out" | tail -2)"
done

# gen-incomplete-references.py grid: a point whose reference now fails keeps its old row, and the run exits 1.
n=$((n + 1))
if python -c "import mpmath" 2> /dev/null; then
  out=$(python - "$T" <<'PY' 2>&1
import importlib.util, os, sys
T = sys.argv[1]
spec = importlib.util.spec_from_file_location('gen', os.path.join('scripts', 'gen-incomplete-references.py'))
g = importlib.util.module_from_spec(spec); spec.loader.exec_module(g)
g.DATA = T
g.BETA, g.GAMMA = os.path.join(T, 'b.tsv'), os.path.join(T, 'g.tsv')
TAB = chr(9)
with open(g.BETA, 'w') as f: f.write('# h\n' + TAB.join(['0.5', '1.0', '2.0', 'OLD', 'K1']) + '\n' + TAB.join(['0.25', '1.0', '2.0', 'OLD', 'K2']) + '\n')
with open(g.GAMMA, 'w') as f: f.write('# h\n' + TAB.join(['1.0', '2.0', 'OP', 'OQ', 'KP', 'KQ']) + '\n')
g.beta_points = lambda: [(0.5, 1.0, 2.0), (0.25, 1.0, 2.0)]
g.gamma_points = lambda: [(1.0, 2.0), (3.0, 4.0)]
def beta_ref(x, a, b):
    if x == 0.5: raise ValueError('no convergence')
    return g.mpf(1) / 3
def gamma_ref(a, x):
    if a == 1.0: raise ValueError('no convergence')
    return g.mpf(1) / 4, g.mpf(3) / 4
g.beta_ref, g.gamma_ref = beta_ref, gamma_ref
code = 0
try: g.grid()
except SystemExit as e: code = e.code
beta, gamma = g.read(g.BETA), g.read(g.GAMMA)
ok = code == 1 and ['0.5', '1.0', '2.0', 'OLD', 'K1'] in beta and any(r[:2] == ['0.25', '1.0'] and r[4] == 'K2' for r in beta) \
     and ['1.0', '2.0', 'OP', 'OQ', 'KP', 'KQ'] in gamma and any(r[:2] == ['3.0', '4.0'] for r in gamma)
print('RESULT', 'ok' if ok else 'bad', code, beta, gamma)
PY
)
  [[ $out == *"RESULT ok"* ]] || bad "gen-incomplete grid: $out"
else
  echo "gen-incomplete grid: skipped (no mpmath)"
fi

[ $fail -eq 0 ] && { echo "tooling: $n checks, ALL GREEN"; exit 0; }
echo "tooling: $fail of $n checks failed"; exit 1
