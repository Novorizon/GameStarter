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
import xml.etree.ElementTree as ET
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
    sdk_overrides = []
    for package_file in sorted((root / 'Packages').glob('*/package.json')):
        package = json.loads(package_file.read_text(encoding='utf-8-sig'))
        name = package.get('name', '')
        if name.startswith('com.novorizon.') and name in manifest['dependencies']:
            sdk_overrides.append(package_file.parent.relative_to(root).as_posix())
    assert not sdk_overrides, 'Embedded SDK packages override the pinned Git sources; remove these local copies: ' + ', '.join(sdk_overrides)
    assert manifest['dependencies']['com.unity.inputsystem'] == '1.11.2'
    assert re.search(r'^  activeInputHandler: 2$', (root / 'ProjectSettings/ProjectSettings.asset').read_text(), re.M)
    input_asset = json.loads((root / 'Assets/Resources/InputActions.inputactions').read_text())
    assert input_asset['maps'][0]['name'] == 'Gameplay'
    assert {action['name'] for action in input_asset['maps'][0]['actions']} == {'Move', 'Confirm'}
    assert {scheme['name'] for scheme in input_asset['controlSchemes']} == {'KeyboardMouse', 'Gamepad'}
    for spec in catalog:
        assert re.fullmatch(r'https://github.com/[^/]+/[^#]+#(?:v[0-9][A-Za-z0-9.\-]*|[a-f0-9]{40})', spec['source']), spec['id']
        for dependency in spec['dependencies']:
            assert dependency in modules, dependency
    assert modules['com.code-philosophy.hybridclr']['embed']
    assert modules['com.novorizon.framework.hybridclr']['setupMethod'] == 'Configure'
    runtime = json.loads((root / 'Assets/Game/Runtime/GameStarter.Runtime.asmdef').read_text())
    assert set(runtime['references']) == {'Game.Startup', 'Game.Framework.Core', 'Game.Framework.Unity', 'Game.Framework.HotUpdate', 'Game.Framework.Resources.YooAsset', 'Unity.InputSystem', 'YooAsset'}
    assert 'Editor' not in runtime.get('includePlatforms', [])
    generated = json.loads((root / 'Assets/HotUpdate/Generated/Data/Game.Data.Generated.asmdef').read_text())
    assert generated['name'] == 'Game.Data.Generated' and generated['references'] == ['Luban.Runtime'] and not generated['autoReferenced']
    editor = json.loads((root / 'Assets/Game/Editor/GameStarter.Editor.asmdef').read_text())
    assert set(editor['references']) == {'GameStarter.Runtime', 'Game.Startup', 'Game.Framework.Data.Editor', 'Game.Framework.HotUpdate.Editor', 'YooAsset', 'YooAsset.Editor', 'Game.Framework.HotUpdate', 'Unity.TextMeshPro', 'Game.Framework.Core', 'Game.Framework.Unity'}
    data_root = root / 'Assets/GameAssets/Data/Luban'
    data_manifest = json.loads((data_root / 'manifest.json').read_text())
    assert data_manifest['version'] == 1 and data_manifest['format'] == 'bin'
    for entry in data_manifest['files']:
        assert entry['location'] == 'Data/Luban/' + entry['name']
        assert (data_root / (entry['name'] + '.bytes')).is_file()
    collector = (root / 'Assets/Settings/AssetBundleCollectorSetting.asset').read_text()
    assert 'CollectorType: 0' in collector, 'YooAsset MainAssetCollector is enum value 0'
    assert 'CollectPath: Assets/GameAssets/Data/Luban' in collector
    assert 'AddressRuleName: GameDataAddressRule' in collector and 'FilterRuleName: GameDataFilterRule' in collector
    assert not (root / 'Assets/Resources/Config/Luban').exists(), 'Runtime tables use YooAsset'
    source = root / 'Config/Luban'
    source_text = ''.join(file.relative_to(source).as_posix() + '\n' + hashlib.sha256(file.read_bytes()).hexdigest() + '\n'
                          for file in sorted(source.rglob('*')) if file.is_file() and not file.name.startswith('~$'))
    assert hashlib.sha256(source_text.encode()).hexdigest() == (root / 'ProjectSettings/LubanSource.sha256').read_text()
    assert not (root / 'Assets/Resources/NovorizonHotUpdateSettings.json').exists()
    hot = json.loads((root / 'Assets/HotUpdate/Game.HotUpdate.asmdef').read_text())
    assert hot['name'] == 'Game.HotUpdate'
    assert set(hot['references']) == {'ECS', 'Game.Startup', 'Game.Data.Generated', 'Luban.Runtime', 'Game.Framework.Data', 'Game.Framework.Resources.YooAsset', 'Game.Framework.Network', 'Game.Framework.Story', 'GpuAnimator', 'Game.Framework.Core', 'Game.Framework.Unity', 'Game.Framework.Audio', 'Game.Framework.Input', 'Game.Framework.UI', 'Game.Framework.Network.Unity', 'Unity.InputSystem', 'Game.Framework.HotUpdate', 'Unity.ugui', 'Unity.TextMeshPro'}
    hot_project = root / 'Verification~/HotUpdate/HotUpdate.csproj'
    model = ET.parse(hot_project).getroot()
    assert model.findtext('.//DisableTransitiveProjectReferences', '').lower() == 'true'
    model_references = set()
    for reference in model.findall('.//ProjectReference'):
        project = (hot_project.parent / reference.attrib['Include']).resolve()
        # Unity automatically supplies engine references unless noEngineReferences is enabled.
        if project == (root / 'Verification~/Unity/Unity.csproj').resolve():
            continue
        model_references.add(ET.parse(project).getroot().findtext('.//AssemblyName'))
    assert model_references == set(hot['references']), 'Verification must use the same direct references as the Unity asmdef'
    assert not hot['autoReferenced']
    assert hot['name'] not in runtime['references'], 'AOT cannot reference hot update assemblies'
    guids = set()
    for asset in (root / 'Assets').rglob('*'):
        if asset.suffix == '.meta' or any(part.startswith('.') for part in asset.relative_to(root / 'Assets').parts):
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
            subprocess.run(['git', 'init', '-q', str(checkout)], env=git_env, check=True)
            subprocess.run(['git', '-C', str(checkout), 'remote', 'add', 'origin', origin], env=git_env, check=True)
            subprocess.run(['git', '-C', str(checkout), 'fetch', '--depth', '1', '--filter=blob:none', 'origin', source.fragment], env=git_env, check=True)
            subprocess.run(['git', '-C', str(checkout), 'sparse-checkout', 'set', subfolder, 'Packages/com.novorizon.framework.hybridclr', 'Packages/com.novorizon.data', 'Packages/com.novorizon.framework.yooasset', 'Packages/com.novorizon.story'], env=git_env, check=True)
            subprocess.run(['git', '-C', str(checkout), 'checkout', '--detach', '-q', 'FETCH_HEAD'], env=git_env, check=True)
            framework = checkout / subfolder
        framework = framework.resolve()
        package = json.loads((framework / 'package.json').read_text())
        assert package['name'] == 'com.novorizon.framework'
        assert package['version'] == next(m['version'] for m in catalog if m['id'] == package['name'])
        assert (framework / 'Runtime/Unity/MonoSingleton.cs').is_file()
        hot_package = framework.parent / 'com.novorizon.framework.hybridclr'
        assert json.loads((hot_package / 'package.json').read_text())['name'] == 'com.novorizon.framework.hybridclr'
        data_package = framework.parent / 'com.novorizon.data'
        assert json.loads((data_package / 'package.json').read_text())['name'] == 'com.novorizon.data'
        luban_commit = '061314200e2f03307c4d90b448e1de7b85431905'
        luban = Path(os.environ.get('NOVORIZON_VERIFY_CACHE', Path(tempfile.gettempdir()) / 'novorizon-interaction-dependencies')) / ('runtime-' + luban_commit)
        if not luban.exists():
            luban.parent.mkdir(parents=True, exist_ok=True)
            subprocess.run(['git', 'clone', '--depth', '1', '--branch', 'v1.2.0', 'https://github.com/focus-creative-games/luban_unity.git', str(luban)], check=True)
        assert subprocess.check_output(['git', '-C', str(luban), 'rev-parse', 'HEAD'], text=True).strip() == luban_commit
        yoo_commit = '4b6a8ca4064d7a59ba129acec8fecf134e86236e'
        yoo = luban.parent / ('yooasset-' + yoo_commit)
        if not yoo.exists():
            yoo.mkdir()
            subprocess.run(['git', 'init', '-q', str(yoo)], check=True)
            subprocess.run(['git', '-C', str(yoo), 'remote', 'add', 'origin', 'https://github.com/tuyoogame/YooAsset.git'], check=True)
            subprocess.run(['git', '-C', str(yoo), 'fetch', '--depth', '1', 'origin', yoo_commit], check=True)
            subprocess.run(['git', '-C', str(yoo), 'checkout', '--detach', '-q', 'FETCH_HEAD'], check=True)
        assert subprocess.check_output(['git', '-C', str(yoo), 'rev-parse', 'HEAD'], text=True).strip() == yoo_commit
        env = os.environ.copy()
        env.setdefault('DOTNET_PROCESSOR_COUNT', '2')
        env.setdefault('DOTNET_CLI_HOME', str(Path(temp) / 'dotnet-home'))
        env.setdefault('DOTNET_SKIP_FIRST_TIME_EXPERIENCE', '1')
        subprocess.run([args.dotnet, 'build', 'Verification~/GameStarter.Verification.csproj', '-c', 'Release', '-m:1', '--disable-build-servers', '--nologo', '-p:FrameworkPackageRoot=' + str(framework), '-p:HotUpdatePackageRoot=' + str(hot_package), '-p:DataPackageRoot=' + str(data_package), '-p:LubanPackageRoot=' + str(luban), '-p:YooAssetPackageRoot=' + str(yoo / 'Assets/YooAsset'), '-p:ResourcesPackageRoot=' + str(framework.parent / 'com.novorizon.framework.yooasset')], cwd=ROOT, env=env, check=True)
        subprocess.run([args.dotnet, 'Verification~/bin/Release/net8.0/GameStarter.Verification.dll'], cwd=ROOT, env=env, check=True)
        for suite in ['TowerDefenseDomain', 'TowerDefenseOnline', 'TowerDefenseNavigation']:
            subprocess.run([args.dotnet, 'run', '--project', 'Verification~/' + suite + '/' + suite + '.csproj', '-c', 'Release'], cwd=ROOT, env=env, check=True)


if __name__ == '__main__':
    main()
