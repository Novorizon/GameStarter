#!/usr/bin/env python3
"""Stage frozen netstandard candidate DLLs in a clean Unity checkout and run a Play/Mono subset.

The default scene/game backend remains A. The temporary laboratory is removed afterward.
Run this command from a Unity/keyring-enabled shell. No credentials are accepted in arguments.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def dump(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def convert_input(path):
    original = json.loads(path.read_text())
    compact = json.dumps(original, ensure_ascii=False, separators=(',', ':')).encode()
    result = dict(original)
    result['modelInputSha256'] = hashlib.sha256(compact).hexdigest()
    result['expectedAttackers'] = original['expectedAttackers'] if original['expectedAttackers'] is not None else -1
    result['initialSettledCheckTick'] = original['initialSettledCheckTick'] if original['initialSettledCheckTick'] is not None else -1
    result['minimumAttackers'] = [{'range': int(key), 'count': value} for key, value in original['minimumAttackersByRange'].items()]
    result.pop('minimumAttackersByRange')
    result['worldEvents'] = [{'tick': item['Tick'], 'blockCells': item['BlockCells'], 'openCells': item['OpenCells'],
                              'replaceTarget': item['replaceTarget'], 'afterSettlement': item.get('afterSettlement', item.get('AfterSettlement', False))} for item in original['worldEvents']]
    result['deaths'] = [{'tick': item['Tick'], 'count': item['Count'], 'afterSettlement': item['AfterSettlement'],
                        'replace': item['Replace'], 'requireMoving': item['RequireMoving'], 'range': item['Range']}
                       for item in original['deaths']]
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--project', type=Path, required=True)
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_EDITOR', '/workspace/.hybridclr-env/unity/2022.3.62f1/Editor/Unity')))
    parser.add_argument('--build-manifest', type=Path, action='append', required=True)
    parser.add_argument('--inputs', type=Path, required=True, help='Runner --export-only inputs directory')
    parser.add_argument('--scenario', action='append', required=True, help='Exact input filename stem; may repeat')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--graphics', action='store_true')
    parser.add_argument('--wall-seconds', type=int, default=1800)
    parser.add_argument('--steps-per-update', type=int, default=250)
    args = parser.parse_args()
    project = args.project.resolve()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if not (project / 'ProjectSettings/ProjectVersion.txt').exists():
        parser.error('--project is not a Unity project')
    unity_processes = subprocess.check_output(['ps', '-eo', 'args'], text=True).splitlines() if os.name != 'nt' else []
    if any('Editor/Unity -' in line and '-projectPath ' + str(project) in line and not line.lstrip().startswith(('bash ', '/bin/bash '))
           for line in unity_processes):
        raise RuntimeError('A Unity editor already owns this project; wait for it to exit.')
    status = subprocess.check_output(['git', '-C', str(project), 'status', '--porcelain', '--', 'Assets', 'Packages', 'ProjectSettings'], text=True)
    if status.strip():
        raise RuntimeError('Use a clean Unity checkout. Existing asset/settings changes must remain untouched.\n' + status)
    destination = project / 'Assets/NavigationLabValidation'
    if destination.exists() or Path(str(destination) + '.meta').exists():
        raise RuntimeError('Temporary laboratory path already exists; refusing to overwrite it.')
    manifests = [json.loads(path.read_text()) for path in args.build_manifest]
    for source in manifests:
        for role in ['core', 'oracle', 'candidate', 'runner']:
            if digest(Path(source[role + 'AssemblyPath'])) != source[role + 'AssemblySha256']:
                raise RuntimeError('Frozen DLL changed before native staging: ' + role)
        for name, expected in source['sourceFiles'].items():
            if digest(Path(name)) != expected:
                raise RuntimeError('Frozen input source changed before native staging: ' + name)
    if len({source['coreAssemblySha256'] for source in manifests}) != 1 or len({source['oracleAssemblySha256'] for source in manifests}) != 1:
        raise RuntimeError('Candidate manifests use different Core/Oracle binaries; rebuild against one frozen harness.')
    converted = {}
    original_hashes = {}
    for name in args.scenario:
        source = args.inputs.resolve() / (name + '.json')
        converted[name] = output / (name + '.unity-input.json')
        dump(converted[name], convert_input(source))
        original_hashes[name] = digest(source)
    plugin_hashes = {}
    runs = []
    fonts = ['Assets/GameAssets/Common/Fonts/GameUI.asset', 'Assets/Resources/Bootstrap/StartupFont.asset']
    known_unity_serializations = {name: (project / name).read_bytes() for name in fonts if (project / name).exists()}
    urp = project / 'Assets/Settings/NovorizonURP/UniversalRenderPipeline.asset'
    urp_bytes = urp.read_bytes() if urp.exists() else None
    exit_code = 3
    try:
        (destination / 'Plugins').mkdir(parents=True)
        (destination / 'Editor').mkdir()
        for source in manifests:
            for role in ['core', 'oracle', 'candidate']:
                dll = Path(source[role + 'AssemblyPath'])
                target = destination / 'Plugins' / dll.name
                if target.name in plugin_hashes and plugin_hashes[target.name] != digest(dll):
                    raise RuntimeError('Two candidates require different DLLs with the same assembly name.')
                shutil.copyfile(dll, target)
                plugin_hashes[target.name] = digest(target)
            for name, path in converted.items():
                runs.append({'name': source['factory'].split('.')[-1] + '-' + name, 'input': str(path),
                             'assemblyName': Path(source['candidateAssemblyPath']).stem,
                             'assemblyPath': str(destination / 'Plugins' / Path(source['candidateAssemblyPath']).name),
                             'factory': source['factory']})
        script = Path(__file__).with_name('UnityNavigationLab.cs')
        shutil.copyfile(script, destination / 'Editor/UnityNavigationLab.cs')
        dump(destination / 'Editor/NavigationLab.UnityValidation.Editor.asmdef',
             {'name': 'NavigationLab.UnityValidation.Editor', 'includePlatforms': ['Editor'], 'autoReferenced': False})
        manifest = output / 'native-manifest.json'
        dump(manifest, {'output': str(output), 'wallDeadlineSeconds': args.wall_seconds,
                        'stepsPerEditorUpdate': args.steps_per_update, 'runs': runs})
        command = [str(args.unity), '-batchmode', '-force-glcore' if args.graphics else '-nographics', '-projectPath', str(project),
                   '-executeMethod', 'NavigationLab.UnityValidation.UnityNavigationLab.Run', '--navlab-manifest', str(manifest),
                   '-logFile', str(output / 'unity.log')]
        with (output / 'console.log').open('w') as stream:
            exit_code = subprocess.run(command, stdout=stream, stderr=subprocess.STDOUT, timeout=args.wall_seconds + 240).returncode
        dump(output / 'native-provenance.json', {'exitCode': exit_code, 'originalModelInputFileSha256': original_hashes,
                                               'stagedPluginHashes': plugin_hashes, 'nativeManifestSha256': digest(manifest),
                                               'driverSourceSha256': digest(script),
                                               'buildManifests': {str(path): digest(path) for path in args.build_manifest},
                                               'scope': 'Unity Editor Play/Mono plugin experiment; no replacement of the default business backend.'})
    finally:
        if destination.exists():
            shutil.rmtree(destination)
        Path(str(destination) + '.meta').unlink(missing_ok=True)
        for name, original in known_unity_serializations.items():
            if (project / name).read_bytes() != original:
                (project / name).write_bytes(original)
        if urp_bytes is not None:
            actual = urp.read_bytes()
            if actual.replace(b'  m_EditorClassIdentifier: \n', b'  m_EditorClassIdentifier:\n') == urp_bytes:
                urp.write_bytes(urp_bytes)
        remaining = subprocess.check_output(['git', '-C', str(project), 'status', '--porcelain', '--', 'Assets', 'Packages', 'ProjectSettings'], text=True)
        if remaining.strip():
            print('Unity left unexpected tracked changes; preserved for inspection:\n' + remaining, file=sys.stderr)
    return exit_code


if __name__ == '__main__':
    sys.exit(main())
