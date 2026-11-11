#!/usr/bin/env python3
"""Compare closed original D cases against a separately frozen memo variant."""
from pathlib import Path
import argparse
import gzip
import hashlib
import importlib.util
import json

ROOT = Path('/workspace/navigation-comparison')
BASE = ROOT / 'results/d-final-stress'
FROZEN = 'bc6e75e003cbb4ab7aecb0953f6957d53ed61480b98a47b9b339e458fdc1e2a3'
FIELDS = ['InputSha256', 'PhysicalInputSha256', 'GeometrySha256', 'Status',
          'TraceSha256', 'CommandsSha256', 'FinalState']


def read(path):
    return json.loads(path.read_bytes())


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def full(path):
    return json.loads(gzip.decompress(path.read_bytes()) if path.suffix == '.gz' else path.read_bytes())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    summary = read(args.run / 'summary.json')
    manifest = read(args.run / 'build-manifest.json')
    execution = read(args.run / 'frozen-execution.json')
    for flag in ['formalEvidenceReady', 'replayRequested', 'buildProvenanceVerified', 'sourceFilesUnchanged', 'isolationAndPauseVerified']:
        assert summary[flag] is True, flag
    assert summary['candidateAssemblySha256'] == FROZEN == manifest['candidateAssemblySha256']
    assert execution['frozenSourceAndBinariesUnchanged'] and execution['freshManifestBoundReport']
    assert execution['diagnosticNoReplay'] is False
    assert summary['buildManifestSha256'] == sha(args.run / 'build-manifest.json')
    for row in summary['cases']:
        assert row['Status'] != 'Passed' or row['ReplayVerified'] is True
    base_manifest = read(BASE / 'build-manifest.json')
    assert base_manifest['candidateAssemblySha256'] == '9e6107cfe1dcd6d83ac137314109a145835c764b3a29a87171bde4412572b19c'
    for selected in [manifest, base_manifest]:
        for path, expected in selected['sourceFiles'].items():
            assert sha(Path(path)) == expected, path
        for role in ['candidate', 'core', 'oracle', 'runner']:
            assert sha(Path(selected[role + 'AssemblyPath'])) == selected[role + 'AssemblySha256'], role
    for role in ['core', 'oracle']:
        assert manifest[role + 'AssemblySha256'] == base_manifest[role + 'AssemblySha256']
    tool = ROOT / 'comparison/Tools~/NavigationLab/report.py'
    spec = importlib.util.spec_from_file_location('strict_report', tool)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    assert module.runner_identity(manifest)['sha256'] == module.runner_identity(base_manifest)['sha256']
    rows = []
    for reference in summary['cases']:
        name = reference['Name']
        current_path = args.run / reference['FullReport']
        assert sha(current_path) == reference['FullReportSha256']
        current = full(current_path)
        original_path = BASE / (name + '.json.gz')
        original = full(original_path)
        compared = {field: current[field] == original[field] for field in FIELDS}
        assert all(compared.values()), (name, compared)
        old_input = BASE / 'inputs' / (name + '.json')
        new_input = args.run / 'inputs' / (name + '.json')
        assert old_input.read_bytes() == new_input.read_bytes()
        old_alloc = original['TotalCandidateApiAllocatedBytes']
        new_alloc = current['TotalCandidateApiAllocatedBytes']
        rows.append({
            'name': name, 'allRequestedFieldsIdentical': True, 'fields': compared,
            'originalReport': str(original_path), 'originalReportFileSha256': sha(original_path),
            'variantReport': str(current_path), 'variantReportFileSha256': sha(current_path),
            'oldStatus': original['Status'], 'newStatus': current['Status'],
            'oldElapsedMilliseconds': original['ElapsedMilliseconds'],
            'newElapsedMilliseconds': current['ElapsedMilliseconds'],
            'elapsedRatioOriginalOverVariant': original['ElapsedMilliseconds'] / max(1, current['ElapsedMilliseconds']),
            'oldCandidateApiAllocatedBytes': old_alloc, 'newCandidateApiAllocatedBytes': new_alloc,
            'allocationRatioOriginalOverVariant': old_alloc / max(1, new_alloc),
            'RouteCacheHits': current['Metrics'].get('RouteCacheHits', 0),
            'HpaSearches': current['Metrics'].get('HpaSearches', 0)
        })
    result = {
        'scope': 'Separate frozen route-result memo variant, compared to closed original9e61 case files. Diagnostic performance only; original serial49 may still be running. Not a replacement or a mixed49 score.',
        'candidateAssemblySha256': FROZEN, 'sourceRunSummary': str(args.run / 'summary.json'),
        'sourceSummarySha256': sha(args.run / 'summary.json'), 'formalVariantSourceAndFreshReplayVerified': True,
        'originalFrozenSourcesAndFourDllsCurrentlyUnchanged': True, 'coreOracleByteIdentical': True,
        'runnerCompileIdentityEqual': True, 'cases': rows
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    for row in rows:
        print(row['name'], 'identical', 'elapsedRatio', round(row['elapsedRatioOriginalOverVariant'], 2),
              'allocationRatio', round(row['allocationRatioOriginalOverVariant'], 2))


if __name__ == '__main__':
    main()
