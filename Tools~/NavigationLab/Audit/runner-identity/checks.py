"""Read-only actual-manifest checks plus isolated validator unit fixtures.

Fixtures exercise source-identity rejection only. They are not executed builds,
algorithm reports, or evidence that the original DLL was built from altered text.
"""
import copy
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import tempfile
import sys

TOOL = Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else Path('/workspace/navigation-comparison/comparison/Tools~/NavigationLab/report.py')
PACKAGE = Path(__file__).parent
spec = importlib.util.spec_from_file_location('navlab_report', TOOL)
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
NAMES = ['a-v2-final-stress', 'b0-v2-stress', 'b1-v2-stress', 'c-final-v2-stress49', 'd-r9-build', 'e-v2-stress']
results = []
actual = []
for name in NAMES:
    path = Path('/workspace/navigation-comparison/results') / name / 'build-manifest.json'
    manifest = m.read_json(path)
    identity = m.runner_identity(manifest)
    actual.append({'name': name, 'manifest': str(path), 'manifestSha256': m.file_sha(path), 'identity': identity,
                   'coreSha256': manifest['coreAssemblySha256'], 'oracleSha256': manifest['oracleAssemblySha256']})
assert len({item['identity']['sha256'] for item in actual}) == 1
assert len({item['identity']['rawRunnerAssemblySha256'] for item in actual}) == 6
assert len({item['coreSha256'] for item in actual}) == len({item['oracleSha256'] for item in actual}) == 1
base_details = actual[0]['identity']['compileFileDetails']
for item in actual[1:]:
    changed = [key for key, value in item['identity']['compileFileDetails'].items()
               if value['sourceSha256'] != base_details[key]['sourceSha256']]
    assert changed == [m.RUNNER_ASSEMBLY_INFO], changed
    item['onlyRawCompileSourceDifferenceFromA'] = changed
results.append({'test': 'all_six_actual_manifests_normalize_equally', 'passed': True,
                'commonIdentity': actual[0]['identity']['sha256'], 'rawDllHashesAreDifferent': True,
                'onlyDifferingCompileSource': m.RUNNER_ASSEMBLY_INFO})
(PACKAGE / 'actual-models.json').write_text(json.dumps(actual, ensure_ascii=False, indent=2) + '\n')

# This is the only completed summary imported by these tests. In-progress49 summaries are never loaded.
a_path = Path('/workspace/navigation-comparison/results/a-v2-final-stress/summary.json')
a_report = m.load_report('A', a_path)
manifest = a_report['manifest']
original_snapshot = Path(manifest['snapshot'])
original_item = next(item for item in manifest['compileItems'] if item['project'] == str(original_snapshot / m.RUNNER_PROJECT))


def rejected(test, function):
    try:
        function()
    except m.EvidenceError as error:
        results.append({'test': test, 'rejected': True, 'reason': str(error)})
    else:
        raise AssertionError('Incorrect acceptance: ' + test)


with tempfile.TemporaryDirectory(dir=PACKAGE) as temp:
    fixture = Path(temp) / 'snapshot-fixture'
    project = fixture / m.RUNNER_PROJECT
    project.parent.mkdir(parents=True)
    project.write_bytes((original_snapshot / m.RUNNER_PROJECT).read_bytes())
    candidate = copy.deepcopy(manifest)
    candidate['snapshot'] = str(fixture)
    candidate['sourceFiles'] = {str(project): m.file_sha(project)}
    item = copy.deepcopy(original_item)
    item['project'] = str(project)
    item['compileFiles'] = {}
    for oldname in original_item['compileFiles']:
        old = Path(oldname)
        relative = old.relative_to(original_snapshot).as_posix()
        new = fixture / relative
        new.parent.mkdir(parents=True, exist_ok=True)
        raw = old.read_bytes()
        if relative == m.RUNNER_ASSEMBLY_INFO:
            normalized, _ = m.normalize_runner_metadata(raw, original_snapshot / m.RUNNER_PROJECT, original_snapshot)
            value = project.parent.as_posix() + '/../../../'
            raw = normalized.replace(b'"<FROZEN_SNAPSHOT>/Experiments~/NavigationLab/Runner/../../../"', json.dumps(value).encode())
        new.write_bytes(raw)
        item['compileFiles'][str(new)] = m.file_sha(new)
        candidate['sourceFiles'][str(new)] = m.file_sha(new)
    candidate['compileItems'] = [item]
    identity = m.runner_identity(candidate)
    assert identity['sha256'] == a_report['runnerIdentity']['sha256']
    results.append({'test': 'only_exact_repository_root_path_difference_accepted', 'passed': True})

    def cross(identity):
        other = copy.copy(a_report)
        other['runnerIdentity'] = identity
        m.validate_common_harness([a_report, other])

    cross(identity)
    source = fixture / 'Experiments~/NavigationLab/Runner/Program.cs'
    original = source.read_bytes()
    source.write_bytes(original + b'\n// isolated validator source-difference fixture\n')
    item['compileFiles'][str(source)] = m.file_sha(source)
    candidate['sourceFiles'][str(source)] = m.file_sha(source)
    rejected('different_actual_compile_source_rejected', lambda: cross(m.runner_identity(candidate)))
    source.write_bytes(original)
    item['compileFiles'][str(source)] = candidate['sourceFiles'][str(source)] = m.file_sha(source)

    changed = copy.deepcopy(candidate)
    changed['compileItems'][0]['properties']['CheckForOverflowUnderflow'] = 'true'
    rejected('different_actual_compile_property_rejected', lambda: cross(m.runner_identity(changed)))

    original_project = project.read_bytes()
    project.write_bytes(original_project + b'\n<!-- isolated project identity fixture -->\n')
    candidate['sourceFiles'][str(project)] = m.file_sha(project)
    candidate['relativeOriginalSourceHashes'][m.RUNNER_PROJECT] = m.file_sha(project)
    rejected('different_csproj_rejected', lambda: cross(m.runner_identity(candidate)))
    project.write_bytes(original_project)
    candidate['sourceFiles'][str(project)] = candidate['relativeOriginalSourceHashes'][m.RUNNER_PROJECT] = m.file_sha(project)

    metadata = fixture / m.RUNNER_ASSEMBLY_INFO
    raw = metadata.read_bytes()
    abnormal = raw.replace(json.dumps(project.parent.as_posix() + '/../../../').encode(), b'"/tmp/unrelated-snapshot"')
    rejected('metadata_wrong_snapshot_rejected', lambda: m.normalize_runner_metadata(abnormal, project, fixture))
    duplicate = raw + b'\n[assembly: System.Reflection.AssemblyMetadata("RepositoryRoot", "/tmp/other")]\n'
    rejected('metadata_duplicate_root_rejected', lambda: m.normalize_runner_metadata(duplicate, project, fixture))
    metadata.write_bytes(raw + b'\n[assembly: System.Reflection.AssemblyMetadata("Unrelated", "changed")]\n')
    item['compileFiles'][str(metadata)] = candidate['sourceFiles'][str(metadata)] = m.file_sha(metadata)
    rejected('other_assemblyinfo_change_rejected', lambda: cross(m.runner_identity(candidate)))
    metadata.write_bytes(raw)
    item['compileFiles'][str(metadata)] = candidate['sourceFiles'][str(metadata)] = m.file_sha(metadata)

    # Clone small report files and hard-link immutable traces; no original contents are modified.
    cloned = Path(temp) / 'missing-fresh'
    cloned.mkdir()
    for entry in a_path.parent.iterdir():
        if entry.is_file() and (entry.name in ('summary.json', 'build-manifest.json', 'frozen-execution.json') or entry.name.endswith('.json.gz')):
            shutil.copyfile(entry, cloned / entry.name)
    shutil.copytree(a_path.parent / 'inputs', cloned / 'inputs')
    (cloned / 'traces').mkdir()
    for entry in (a_path.parent / 'traces').iterdir():
        if entry.is_file():
            os.link(entry, cloned / 'traces' / entry.name)
    execution = m.read_json(cloned / 'frozen-execution.json')
    execution.pop('freshManifestBoundReport')
    (cloned / 'frozen-execution.json').write_text(json.dumps(execution))
    rejected('missing_explicit_fresh_flag_rejected', lambda: m.load_report('bad', cloned / 'summary.json'))

(PACKAGE / 'checks.json').write_text(json.dumps(results, ensure_ascii=False, indent=2) + '\n')
print(json.dumps(results, ensure_ascii=False, indent=2))
