#!/usr/bin/env python3
"""Compile actual Framework foundation sources and run deterministic API-model scenarios."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tarfile
import tempfile
from urllib.parse import urlsplit, urlunsplit, parse_qs

ROOT = Path(__file__).resolve().parents[1]


def validate_project(root=ROOT):
    manifest = json.loads((root / 'Packages/manifest.json').read_text())
    catalog = json.loads((root / 'Assets/Settings/OptionalModules.json').read_text())['modules']
    modules = {m['id']: m for m in catalog}
    assert len(modules) == len(catalog), 'Duplicate package ID'
    assert manifest['dependencies']['com.novorizon.framework'] == modules['com.novorizon.framework']['source']
    framework_source = modules['com.novorizon.framework']['source']
    assert manifest['dependencies']['com.novorizon.framework.all'] == framework_source.replace('/Packages/com.novorizon.framework#', '/Packages/com.novorizon.framework.all#')
    for spec in catalog:
        assert manifest['dependencies'][spec['id']] == spec['source'], spec['id']
    assert not any(value.startswith('file:') for value in manifest['dependencies'].values())
    for spec in catalog:
        assert re.fullmatch(r'https://github.com/[^/]+/[^#]+#(?:v[0-9][A-Za-z0-9.\-]*|[a-f0-9]{40})', spec['source']), spec['id']
        for dependency in spec['dependencies']:
            assert dependency in modules, dependency
    assert modules['com.code-philosophy.hybridclr']['embed']
    assert modules['com.novorizon.framework.hybridclr']['setupMethod'] == 'Configure'
    runtime = json.loads((root / 'Assets/Game/Runtime/GameStarter.Runtime.asmdef').read_text())
    assert set(runtime['references']) == {'Game.Framework.Core', 'Game.Framework.Unity', 'Game.Framework.Network', 'Game.Framework.Network.Unity', 'Game.Framework.Resources.YooAsset', 'Game.Framework.UI', 'ECS'}
    assert 'Editor' not in runtime.get('includePlatforms', [])
    guids = set()
    for asset in (root / 'Assets').rglob('*'):
        if asset.suffix == '.meta':
            continue
        meta = Path(str(asset) + '.meta')
        assert meta.is_file(), f'Missing meta: {asset}'
        match = re.search(r'^guid: ([a-f0-9]{32})$', meta.read_text(), re.M)
        assert match and match[1] not in guids, f'Invalid or duplicate GUID: {meta}'
        guids.add(match[1])
    entry_guid = re.search(r'^guid: (.+)$', (root / 'Assets/Game/Runtime/GameEntry.cs.meta').read_text(), re.M)[1]
    scene_guid = re.search(r'^guid: (.+)$', (root / 'Assets/Scenes/Bootstrap.unity.meta').read_text(), re.M)[1]
    assert 'guid: ' + entry_guid in (root / 'Assets/Scenes/Bootstrap.unity').read_text()
    assert 'guid: ' + scene_guid in (root / 'ProjectSettings/EditorBuildSettings.asset').read_text()
    return catalog


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--framework', type=Path, help='Actual com.novorizon.framework package directory')
    parser.add_argument('--structure-only', action='store_true')
    args = parser.parse_args()
    catalog = validate_project()
    print('PASS project structure, GUIDs and pinned Git dependency references', flush=True)
    if args.structure_only:
        return
    with tempfile.TemporaryDirectory(prefix='game-starter-verify-') as temp:
        framework = args.framework
        if framework is None:
            # Test tooling obtains the same published package that Unity installs.
            spec = next(m for m in catalog if m['id'] == 'com.novorizon.framework')
            source = urlsplit(spec['source'])
            origin = urlunsplit((source.scheme, source.netloc, source.path, '', ''))
            subfolder = parse_qs(source.query)['path'][0].lstrip('/')
            checkout = Path(temp) / 'FrameworkSource'
            git_env = os.environ.copy()
            git_env['GIT_TERMINAL_PROMPT'] = '0'
            subprocess.run(['git', 'clone', '--depth', '1', '--filter=blob:none', '--sparse', '--branch', source.fragment, origin, str(checkout)], env=git_env, check=True)
            subprocess.run(['git', '-C', str(checkout), 'sparse-checkout', 'set', subfolder], env=git_env, check=True)
            framework = checkout / subfolder
        framework = framework.resolve()
        package = json.loads((framework / 'package.json').read_text())
        assert package['name'] == 'com.novorizon.framework'
        assert package['version'] == next(m['version'] for m in catalog if m['id'] == package['name'])
        assert (framework / 'Runtime/Unity/MonoSingleton.cs').is_file()
        env = os.environ.copy()
        env.setdefault('DOTNET_PROCESSOR_COUNT', '2')
        env.setdefault('DOTNET_CLI_HOME', str(Path(temp) / 'dotnet-home'))
        env.setdefault('DOTNET_SKIP_FIRST_TIME_EXPERIENCE', '1')
        subprocess.run([args.dotnet, 'build', 'Verification~/GameStarter.Verification.csproj', '-c', 'Release', '-m:1', '--disable-build-servers', '--nologo', '-p:FrameworkPackageRoot=' + str(framework)], cwd=ROOT, env=env, check=True)
        subprocess.run([args.dotnet, 'Verification~/bin/Release/net8.0/GameStarter.Verification.dll'], cwd=ROOT, env=env, check=True)


if __name__ == '__main__':
    main()
