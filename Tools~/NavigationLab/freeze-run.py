#!/usr/bin/env python3
"""Freeze the actual shared harness/candidate inputs, build, bind DLLs, and execute one factory.

Experimental failures retain their exit code and reports. A missing feature is not a pass.
No SDK or Unity project assets are modified by this command.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys


LAB = Path('Experiments~/NavigationLab')
SOURCE_SUFFIXES = {'.cs', '.csproj', '.props', '.targets', '.json', '.md', '.py', '.sh'}
A_DOMAIN_SHA256 = '074502ae2c712a92094d4b851c451afba25ac300c063f78e28b605b767a72c2f'
FOREST_SHA256 = 'ad5dd54b722c97779ad6b0f6e22548d3288ed380c8d758b7615d1e6894165256'


def sha(data):
    return hashlib.sha256(data).hexdigest()


def digest_file(path):
    return sha(path.read_bytes())


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def metadata(repository):
    def git(*arguments):
        return subprocess.check_output(['git', '-C', str(repository), *arguments], text=True).strip()
    return {'repository': str(repository), 'commit': git('rev-parse', 'HEAD'), 'branch': git('branch', '--show-current'),
            'dirty': bool(git('status', '--porcelain'))}


def project_sources(directory):
    for path in sorted(directory.rglob('*')):
        if not path.is_file() or any(part in {'bin', 'obj', 'Evidence', 'Artifacts', '.git'} for part in path.relative_to(directory).parts):
            continue
        if path.suffix in SOURCE_SUFFIXES or path.name in {'LICENSE', 'LICENSE.txt', 'LICENSE.md'}:
            yield path


def collect_origins(harness, candidate, relative_project):
    origins = {}
    for path in project_sources(harness / LAB):
        relative = path.relative_to(harness)
        parts = path.relative_to(harness / LAB).parts
        if parts[0] == 'Candidates' and parts[1] != 'A-Sipp':
            continue
        origins[relative.as_posix()] = path
    candidate_directory = (candidate / relative_project).parent
    for path in project_sources(candidate_directory):
        origins[path.relative_to(candidate).as_posix()] = path
    for path in sorted((harness / 'Assets/HotUpdate/TowerDefense/Domain').rglob('*.cs')):
        origins[path.relative_to(harness).as_posix()] = path
    for relative in ['Verification~/TowerDefenseNavigation/Scenarios.cs', 'Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes',
                     'Config/Luban/Data/tdenemy.xlsx', 'Config/Luban/Data/tdspawn.xlsx', 'Config/Luban/Data/tdwave.xlsx']:
        origins[relative] = harness / relative
    for name in ['Directory.Build.props', 'Directory.Build.targets', 'NuGet.Config', 'nuget.config', 'global.json']:
        if (harness / name).exists():
            origins[name] = harness / name
    return dict(sorted(origins.items()))


def run_logged(command, cwd, log, environment):
    with log.open('w') as stream:
        stream.write(json.dumps(command) + '\n')
        stream.flush()
        return subprocess.run(command, cwd=cwd, env=environment, stdout=stream, stderr=subprocess.STDOUT).returncode


def compiler_items(dotnet, project, snapshot, environment, log):
    command = [dotnet, 'msbuild', str(project), '-target:Build', '-property:Configuration=Release', '-maxcpucount:1',
               '-property:PathMap=' + str(snapshot) + '=/navigation-lab', '-property:ContinuousIntegrationBuild=true',
               '-getItem:Compile', '-getProperty:TargetFramework,LangVersion', '-nologo']
    result = subprocess.run(command, cwd=snapshot, env=environment, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    log.write_text(result.stdout)
    if result.returncode:
        raise RuntimeError('Cannot record actual compile items: ' + str(log))
    start = result.stdout.find('{')
    if start < 0:
        raise RuntimeError('MSBuild did not return compile item JSON: ' + str(log))
    document, _ = json.JSONDecoder().raw_decode(result.stdout[start:])
    files = {}
    for item in document['Items']['Compile']:
        path = Path(item.get('FullPath', item['Identity']))
        if not path.is_absolute():
            path = project.parent / path
        path = path.resolve()
        files[str(path)] = digest_file(path)
    return {'project': str(project), 'properties': document.get('Properties', {}), 'compileFiles': files}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--harness-root', type=Path, default=Path(__file__).resolve().parents[2])
    parser.add_argument('--candidate-root', type=Path, required=True)
    parser.add_argument('--candidate-project', type=Path, required=True, help='Project path relative to candidate checkout')
    parser.add_argument('--factory', required=True)
    parser.add_argument('--snapshots', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--suite', choices=['smoke', 'full', 'stress'], default='full')
    parser.add_argument('--scenario')
    parser.add_argument('--build-only', action='store_true')
    parser.add_argument('--no-replay', action='store_true', help='Diagnostic execution only; not formal evidence')
    args = parser.parse_args()
    harness = args.harness_root.resolve()
    candidate = args.candidate_root.resolve()
    relative_project = args.candidate_project
    if relative_project.is_absolute() or '..' in relative_project.parts:
        parser.error('--candidate-project must remain within the candidate checkout')
    origins = collect_origins(harness, candidate, relative_project)
    captured = {name: path.read_bytes() for name, path in origins.items()}
    if any(origins[name].read_bytes() != data for name, data in captured.items()):
        raise RuntimeError('Source changed during double-read freeze; finish edits and retry.')
    source_hashes = {name: sha(data) for name, data in captured.items()}
    domain_prefix = 'Assets/HotUpdate/TowerDefense/Domain/'
    frozen_domain = {name[len(domain_prefix):]: value for name, value in source_hashes.items() if name.startswith(domain_prefix)}
    domain_sha = sha(json.dumps(frozen_domain, sort_keys=True, separators=(',', ':')).encode())
    if domain_sha != A_DOMAIN_SHA256:
        raise RuntimeError('The frozen A Domain no longer matches branch A.')
    if source_hashes['Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes'] != FOREST_SHA256:
        raise RuntimeError('The original Forest input changed; do not change map geometry to pass an algorithm.')
    identity = {'sources': source_hashes, 'factory': args.factory}
    identity_sha = sha(json.dumps(identity, sort_keys=True, separators=(',', ':')).encode())
    snapshot = args.snapshots.resolve() / ('snapshot-' + identity_sha[:24])
    snapshot.mkdir(parents=True, exist_ok=True)
    for relative, data in captured.items():
        target = snapshot / relative
        if target.exists() and target.read_bytes() != data:
            raise RuntimeError('Existing content-addressed snapshot changed: ' + str(target))
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    environment = os.environ.copy()
    environment.setdefault('DOTNET_PROCESSOR_COUNT', '2')
    environment.setdefault('DOTNET_SKIP_FIRST_TIME_EXPERIENCE', '1')
    environment.setdefault('DOTNET_CLI_TELEMETRY_OPTOUT', '1')
    projects = [snapshot / LAB / 'Core/NavigationLab.Core.csproj', snapshot / LAB / 'Oracle/NavigationLab.Oracle.csproj',
                snapshot / LAB / 'Runner/NavigationLab.Runner.csproj', snapshot / relative_project]
    for index, project in enumerate(projects):
        command = [args.dotnet, 'build', str(project), '-c', 'Release', '-m:1', '--disable-build-servers', '--nologo',
                   '-p:PathMap=' + str(snapshot) + '=/navigation-lab', '-p:ContinuousIntegrationBuild=true']
        log = output / ('build-' + str(index) + '.log')
        if run_logged(command, snapshot, log, environment):
            raise RuntimeError('Frozen build failed; inspect ' + str(log))
    compilations = [compiler_items(args.dotnet, project, snapshot, environment, output / ('compile-items-' + str(index) + '.json'))
                    for index, project in enumerate(projects)]
    source_files = {str(snapshot / name): digest for name, digest in source_hashes.items()}
    for compilation in compilations:
        source_files.update(compilation['compileFiles'])
    for name, digest in source_files.items():
        if digest_file(Path(name)) != digest:
            raise RuntimeError('Frozen source changed while building: ' + name)
    candidate_name = args.candidate_project.stem
    assemblies = {
        'candidate': snapshot / args.candidate_project.parent / 'bin/Release/netstandard2.1' / (candidate_name + '.dll'),
        'core': snapshot / LAB / 'Core/bin/Release/netstandard2.1/NavigationLab.Core.dll',
        'oracle': snapshot / LAB / 'Oracle/bin/Release/netstandard2.1/NavigationLab.Oracle.dll',
        'runner': snapshot / LAB / 'Runner/bin/Release/net8.0/NavigationLab.Runner.dll'
    }
    manifest = {'schemaVersion': 1, 'utc': datetime.now(timezone.utc).isoformat(), 'snapshot': str(snapshot),
                'snapshotIdentitySha256': identity_sha, 'sourceFiles': dict(sorted(source_files.items())),
                'frozenADomainAggregateSha256': domain_sha, 'forestMapSha256': FOREST_SHA256,
                'relativeOriginalSourceHashes': source_hashes, 'originHarness': metadata(harness), 'originCandidate': metadata(candidate),
                'factory': args.factory, 'compileItems': compilations,
                'dotnetInfo': subprocess.check_output([args.dotnet, '--info'], text=True, env=environment),
                'freezeToolSha256': digest_file(Path(__file__))}
    for role, path in assemblies.items():
        manifest[role + 'AssemblySha256'] = digest_file(path)
        manifest[role + 'AssemblyPath'] = str(path)
    manifest_path = output / 'build-manifest.json'
    write_json(manifest_path, manifest)
    print('Frozen source:', identity_sha, flush=True)
    print('Build manifest:', manifest_path, flush=True)
    if args.build_only:
        return 0
    command = [args.dotnet, str(assemblies['runner']), '--assembly', str(assemblies['candidate']), '--factory', args.factory,
               '--suite', args.suite, '--output', str(output), '--build-manifest', str(manifest_path), '--require-build-manifest']
    if args.scenario:
        command.extend(['--scenario', args.scenario])
    if args.no_replay:
        command.append('--no-replay')
    exit_code = run_logged(command, snapshot, output / 'execution.log', environment)
    unchanged = all(digest_file(Path(name)) == digest for name, digest in source_files.items())
    unchanged &= all(digest_file(path) == manifest[role + 'AssemblySha256'] for role, path in assemblies.items())
    write_json(output / 'frozen-execution.json', {'returnCode': exit_code, 'frozenSourceAndBinariesUnchanged': unchanged,
                                               'buildManifestSha256': digest_file(manifest_path), 'diagnosticNoReplay': args.no_replay})
    if not unchanged:
        raise RuntimeError('Frozen source or DLL changed during execution.')
    print('Experimental return code:', exit_code, '(0 pass, 1 failure, 2 unsupported)', flush=True)
    return exit_code


if __name__ == '__main__':
    try:
        sys.exit(main())
    except Exception as error:
        print(str(error), file=sys.stderr)
        sys.exit(3)
