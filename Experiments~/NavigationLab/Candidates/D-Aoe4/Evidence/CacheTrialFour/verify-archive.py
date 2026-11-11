#!/usr/bin/env python3
"""Verify archived bytes and four physical comparisons; never run simulations."""
from pathlib import Path
import gzip
import hashlib
import json
import zipfile

root = Path(__file__).resolve().parent


def digest(data):
    return hashlib.sha256(data).hexdigest()


def read(path):
    return json.loads(path.read_bytes())


def compact(raw):
    output = bytearray()
    quoted = escaped = False
    for value in raw:
        if quoted:
            output.append(value)
            if escaped:
                escaped = False
            elif value == 92:
                escaped = True
            elif value == 34:
                quoted = False
        elif value == 34:
            quoted = True
            output.append(value)
        elif value not in b' \t\r\n':
            output.append(value)
    assert not quoted
    return bytes(output)


for relative, expected in read(root / 'sha256.json').items():
    assert digest((root / relative).read_bytes()) == expected, relative
for relative, record in read(root / 'execution-log-index.json').items():
    raw = gzip.decompress((root / relative).read_bytes())
    assert digest(raw) == record['uncompressedSha256'] and len(raw) == record['originalBytes']
package = read(root / 'source-package-index.json')
assert digest((root / 'source-and-four-dlls.zip').read_bytes()) == package['zipSha256']
with zipfile.ZipFile(root / 'source-and-four-dlls.zip') as source:
    assert set(source.namelist()) == set(package['files'])
    for relative, expected in package['files'].items():
        assert digest(source.read(relative)) == expected, relative
state = read(root / 'comparison.json')
assert state['stage'] == 'four-cases-verified-not-selected' and state['full49Started'] is False
assert len(state['comparisons']) == 4
old_manifest = read(root / 'original-9e-reference/build-manifest.json')
for result in state['comparisons']:
    name = result['name']
    directory = root / 'cases' / name
    summary = read(directory / 'summary.json')
    manifest = read(directory / 'build-manifest.json')
    execution = read(directory / 'frozen-execution.json')
    assert len(summary['cases']) == 1 and summary['cases'][0]['Name'] == name
    for flag in ['formalEvidenceReady', 'replayRequested', 'buildProvenanceVerified', 'sourceFilesUnchanged', 'isolationAndPauseVerified']:
        assert summary[flag] is True, flag
    assert execution['frozenSourceAndBinariesUnchanged'] and execution['freshManifestBoundReport']
    assert execution['diagnosticNoReplay'] is False
    assert summary['buildManifestSha256'] == digest((directory / 'build-manifest.json').read_bytes())
    row = summary['cases'][0]
    assert digest((directory / row['FullReport']).read_bytes()) == row['FullReportSha256']
    assert digest((directory / row['Trace']).read_bytes()) == row['TraceFileSha256']
    assert digest(compact((directory / 'inputs' / (name + '.json')).read_bytes())) == row['InputSha256']
    actual = json.loads(gzip.decompress((directory / row['FullReport']).read_bytes()))
    original = json.loads(gzip.decompress((root / 'original-9e-reference' / (name + '.json.gz')).read_bytes()))
    for key in result['physicalIdentitiesEqual']:
        assert result['physicalIdentitiesEqual'][key] and actual[key] == original[key], (name, key)
    if actual['Status'] == 'Passed':
        assert row['ReplayVerified'] is True
    for role in ['candidate', 'core', 'oracle', 'runner']:
        assert manifest[role + 'AssemblySha256'] == summary[role + 'AssemblySha256']
        path = Path(manifest[role + 'AssemblyPath']).relative_to(Path(manifest['snapshot'])).as_posix()
        assert package['files'][path] == manifest[role + 'AssemblySha256']
    for role in ['core', 'oracle']:
        assert manifest[role + 'AssemblySha256'] == old_manifest[role + 'AssemblySha256']
print('Four-case archive/source/DLL/input/full-trace hashes and original physical identities verified; no simulation run.')
