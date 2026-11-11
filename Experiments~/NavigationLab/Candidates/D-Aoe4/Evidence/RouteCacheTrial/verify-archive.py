#!/usr/bin/env python3
"""Verify archived bytes and finite-case comparisons; never run simulations."""
from pathlib import Path
import gzip
import hashlib
import json
import zipfile

ROOT = Path(__file__).resolve().parent
FIELDS = ['InputSha256', 'PhysicalInputSha256', 'GeometrySha256', 'Status',
          'TraceSha256', 'CommandsSha256', 'FinalState']
LABELS = ['open', 'dynamic', 'forest44', 'portal128']


def read(path):
    return json.loads(path.read_bytes())


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    index = read(ROOT / 'sha256.json')
    for relative, expected in index.items():
        assert sha(ROOT / relative) == expected, relative
    package = read(ROOT / 'source-package-index.json')
    assert sha(ROOT / 'source-and-binaries.zip') == package['zipSha256']
    with zipfile.ZipFile(ROOT / 'source-and-binaries.zip') as stream:
        for relative, expected in package['files'].items():
            assert hashlib.sha256(stream.read(relative)).hexdigest() == expected, relative
    contracts = read(ROOT / 'd-routecache-frozen-contracts.json')
    assert contracts['passed'] == 6
    assert contracts['candidateAssemblySha256'] == package['candidateAssemblySha256']
    result = read(ROOT / 'four-case-comparison.json')
    assert result['allFourCasesIdentical'] is True
    assert result['fullStress49Started'] is False and result['selectedAsDefault'] is False
    expected_factory = 'NavigationLab.DAoe4.Aoe4ProgressExchangeFactory'
    for label in LABELS:
        directory = ROOT / label
        summary = read(directory / 'summary.json')
        manifest = read(directory / 'build-manifest.json')
        execution = read(directory / 'frozen-execution.json')
        assert summary['candidateAssemblySha256'] == package['candidateAssemblySha256']
        assert summary['actualFactoryType'] == expected_factory
        assert summary['buildManifestSha256'] == sha(directory / 'build-manifest.json')
        assert manifest['candidateAssemblySha256'] == package['candidateAssemblySha256']
        for flag in ['formalEvidenceReady', 'replayRequested', 'buildProvenanceVerified', 'sourceFilesUnchanged', 'isolationAndPauseVerified']:
            assert summary[flag] is True, flag
        assert execution['frozenSourceAndBinariesUnchanged'] is True and execution['freshManifestBoundReport'] is True
        assert execution['diagnosticNoReplay'] is False
        assert len(summary['cases']) == 1
        case = summary['cases'][0]
        assert sha(directory / case['FullReport']) == case['FullReportSha256']
        actual = json.loads(gzip.decompress((directory / case['FullReport']).read_bytes()))
        baseline = json.loads(gzip.decompress((directory / 'baseline-report.json.gz').read_bytes()))
        assert all(actual[field] == baseline[field] for field in FIELDS), label
        if actual['Status'] == 'Passed':
            assert actual['ReplayVerified'] is True and baseline['ReplayVerified'] is True
        assert sha(directory / case['Trace']) == case['TraceFileSha256'], label
        storage = read(directory / 'trace-storage.json')
        assert storage['fullTraceIncluded'] is True
        assert storage['variantCompressedSha256'] == case['TraceFileSha256']
        if storage['compressedBytesIdenticalToBaseline']:
            assert storage['variantCompressedSha256'] == storage['baselineCompressedSha256']
    print('Archive bytes/source ZIP/contracts verified; four original cases have identical requested fields, 2PASS/2FAIL. Not49, no new simulations.')


if __name__ == '__main__':
    main()
