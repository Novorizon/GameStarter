#!/usr/bin/env bash
set -euo pipefail
: "${UNITY_EDITOR:?Set UNITY_EDITOR to the licensed Unity Editor executable.}"
phase="${1:-compile}"
project="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/.." && pwd)"
report="$project/Artifacts/NativeValidation"
mkdir -p "$report"
rm -f -- "$report/$phase.json"
if [[ "$phase" == import-samples ]]; then
  rm -f -- "$report/imported-samples.txt"
fi
if [[ "$phase" == compile ]]; then
  rm -f -- "$report/sample-assemblies.json"
fi
arguments=(-batchmode -nographics -projectPath "$project" -logFile "$report/$phase.log")
case "$phase" in
  import-samples)
    method=Game.Editor.SampleValidation.ImportSamples
    arguments+=(-quit)
    ;;
  compile)
    method=Game.Editor.NativeValidation.CompileScripts
    arguments+=(-quit)
    ;;
  editor)
    method=Game.Editor.NativeValidation.EditorSmoke
    ;;
  *)
    printf 'Usage: UNITY_EDITOR=/path/to/Unity bash Tools~/unity-validate.sh {import-samples|compile|editor}\n' >&2
    exit 2
    ;;
esac
"$UNITY_EDITOR" "${arguments[@]}" -executeMethod "$method"
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
