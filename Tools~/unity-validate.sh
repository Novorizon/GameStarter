#!/usr/bin/env bash
set -euo pipefail
: "${UNITY_EDITOR:?Set UNITY_EDITOR to the licensed Unity Editor executable.}"
phase="${1:-compile}"
if (( $# > 0 )); then
  shift
fi
project="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
report="$project/Artifacts/NativeValidation"
arguments=(-batchmode -projectPath "$project" -logFile "$report/$phase.log")
case "$phase" in
  import-samples)
    method=Game.Editor.SampleValidation.ImportSamples
    arguments+=(-nographics -quit)
    ;;
  compile)
    method=Game.Editor.NativeValidation.CompileScripts
    arguments+=(-nographics -quit)
    ;;
  editor)
    method=Game.Editor.NativeValidation.EditorSmoke
    arguments+=(-nographics)
    ;;
  tower-defense)
    method=Game.Editor.TowerDefenseValidation.Run
    ;;
  startup-cycles)
    method=Game.Editor.GameEntryValidation.Run
    ;;
  *)
    printf 'Usage: UNITY_EDITOR=/path/to/Unity bash Tools~/unity-validate.sh {import-samples|compile|editor|tower-defense|startup-cycles} [Unity arguments]\n' >&2
    exit 2
    ;;
esac
fixture="$project/NativeFixtures~/Editor/NativeValidation"
staged="$project/Assets/Game/Editor/NativeValidation"
lock="$project/Temp/UnityLockfile"
if [[ -e "$lock" ]]; then
  printf 'Unity is open in %s. Use a separate, closed checkout for native validation.\n' "$project" >&2
  exit 1
fi
if [[ -e "$staged" || -e "$staged.meta" || -L "$staged" || -L "$staged.meta" ]]; then
  printf 'Validation Editor assets already exist at %s. Check the validation checkout before retrying.\n' "$staged" >&2
  exit 1
fi
mkdir -p "$report"
rm -f -- "$report/$phase.json"
if [[ "$phase" == import-samples ]]; then
  rm -f -- "$report/imported-samples.txt"
fi
if [[ "$phase" == compile ]]; then
  rm -f -- "$report/sample-assemblies.json"
fi
mkdir -- "$staged"
cleanup_validation_editor() {
  if [[ -e "$lock" ]]; then
    printf 'The validation checkout is still locked. Clean up the staged Editor assets after Unity has closed.\n' >&2
  elif [[ -d "$staged" && ! -L "$staged" ]]; then
    rm -f -- "$staged/"* "$staged.meta"
    rmdir -- "$staged"
  fi
}
trap cleanup_validation_editor EXIT
cp -p -- "$fixture/"* "$staged/"
cp -p -- "$fixture.meta" "$staged.meta"
"$UNITY_EDITOR" "${arguments[@]}" -executeMethod "$method" "$@"
case "$phase" in
  import-samples)
    test -s "$report/imported-samples.txt"
    ;;
  compile|editor)
    python3 - "$report/$phase.json" "$phase" <<'PY'
import json,sys
from pathlib import Path
report=json.loads(Path(sys.argv[1]).read_text())
if not report['success']:
    raise SystemExit(report.get('detail', 'Native validation failed'))
if sys.argv[2] == 'compile':
    samples=json.loads(Path(sys.argv[1]).with_name('sample-assemblies.json').read_text())
    if not samples['success'] or samples['sampleCount'] == 0 or not samples['assemblies']:
        raise SystemExit('Sample assembly verification did not report success')
print(report['detail'])
PY
    ;;
esac
printf 'Unity validation completed: %s\n' "$report"
