#!/usr/bin/env python3
"""Verify archived exact source/compiler inputs/DLL bytes without rewriting original manifests."""
from pathlib import Path
import hashlib
import json
import zipfile

root = Path(__file__).resolve().parent

def sha(data):
    return hashlib.sha256(data).hexdigest()

for label in ('B0', 'B1'):
    directory = root / label
    raw_manifest = (directory / 'build-manifest.json').read_bytes()
    manifest = json.loads(raw_manifest)
    index = json.loads((directory / 'package-index.json').read_bytes())
    assert sha(raw_manifest) == index['originalBuildManifestSha256'], label + ' manifest mismatch'
    archive = directory / 'source-and-binaries.zip'
    assert sha(archive.read_bytes()) == index['archiveSha256'], label + ' archive mismatch'
    by_original = {value['originalPath']: value for value in index['files'].values()}
    with zipfile.ZipFile(archive) as source:
        assert set(source.namelist()) == set(index['files']), label + ' archive member mismatch'
        for name, value in index['files'].items():
            data = source.read(name)
            assert len(data) == value['bytes'] and sha(data) == value['sha256'], name
    for path, expected in manifest['sourceFiles'].items():
        assert by_original[path]['sha256'] == expected, path
    for project in manifest['compileItems']:
        for path, expected in project['compileFiles'].items():
            assert by_original[path]['sha256'] == expected, path
    for role in ('candidate', 'core', 'oracle', 'runner'):
        assert by_original[manifest[role + 'AssemblyPath']]['sha256'] == manifest[role + 'AssemblySha256'], role
    print(label, index['factory'], manifest['candidateAssemblySha256'], 'source/Compile/DLL archive OK')

print('Archived exact source/Compile/DLL bytes verified; no simulation or full trace validation is claimed by this check.')
