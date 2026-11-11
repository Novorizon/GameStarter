#!/usr/bin/env python3
"""Copy completed frozen D12 stress reports and bounded, explicitly partial trace windows.

Original JSON, input and gz reports remain byte-identical. Full trace files stay at
recorded external paths; verification here checks archived bytes, not simulations.
"""
from collections import deque
from pathlib import Path
import argparse
import gzip
import hashlib
import json
import shutil


def sha_file(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


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


def validate_summary(summary):
    for flag in ('formalEvidenceReady', 'replayRequested', 'buildProvenanceVerified', 'sourceFilesUnchanged'):
        assert summary.get(flag) is True, flag
    assert len(summary['cases']) == 49
    assert summary['sourceBefore'] == summary['sourceAfter']
    for status, counter in [('Passed', 'passed'), ('Failed', 'failed'), ('Unsupported', 'unsupported')]:
        assert sum(case['Status'] == status for case in summary['cases']) == summary[counter]
    assert all(case['ReplayVerified'] for case in summary['cases'] if case['Status'] == 'Passed')
    assert summary.get('invalidInputs', 0) == 0


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n')


def trace_windows(source, destination, case, count):
    assert sha_file(source) == case['TraceFileSha256']
    first = []
    tail = deque()
    checkpoint = {}
    unsupported = 0
    lines = 0
    with gzip.open(source, 'rb') as stream:
        for line in stream:
            lines += 1
            if len(first) < count:
                first.append(line)
            tail.append(line)
            if len(tail) > count:
                evicted = json.loads(tail.popleft())
                if 'changed' not in evicted:
                    unsupported += 1
                for unit_id in evicted.get('removed', []):
                    checkpoint.pop(unit_id, None)
                for body in evicted.get('changed', []):
                    checkpoint[body[0]] = body
    window = destination / 'trace-windows'
    window.mkdir(exist_ok=True)
    prefix = window / case['Name']
    first_path = prefix.with_name(prefix.name + '.first.jsonl.gz')
    last_path = prefix.with_name(prefix.name + '.last.jsonl.gz')
    checkpoint_path = prefix.with_name(prefix.name + '.last-checkpoint.json')
    first_path.write_bytes(gzip.compress(b''.join(first), mtime=0))
    last_path.write_bytes(gzip.compress(b''.join(tail), mtime=0))
    first_tail = json.loads(tail[0]) if tail else {}
    write_json(checkpoint_path, {
        'scope': 'Derived state immediately before the last delta window. Original raw lines are unmodified; failure rows remain diagnostic.',
        'step': first_tail.get('step', 1) - 1,
        'bodies': [checkpoint[key] for key in sorted(checkpoint)],
        'unappliedNonDeltaRowsBeforeWindow': unsupported
    })
    return {
        'originalFullTracePath': str(source), 'fullTraceNotIncluded': True,
        'compressedBytes': source.stat().st_size, 'originalFileSha256': case['TraceFileSha256'],
        'logicalPhysicalTraceSha256': case['TraceSha256'], 'totalRawLines': lines,
        'windowLines': count, 'firstWindowStep': json.loads(first[0]).get('step') if first else None,
        'lastWindowStartingStep': first_tail.get('step'),
        'derivedFiles': {str(path.relative_to(destination)): sha_file(path) for path in [first_path, last_path, checkpoint_path]}
    }


def verify(directory):
    index = json.loads((directory / 'model-archive-index.json').read_bytes())
    for relative, expected in index['files'].items():
        assert sha_file(directory / relative) == expected, relative
    summary = json.loads((directory / 'summary.json').read_bytes())
    validate_summary(summary)
    manifest = json.loads((directory / 'build-manifest.json').read_bytes())
    assert sha_file(directory / 'build-manifest.json') == summary['buildManifestSha256']
    for role in ('candidate', 'core', 'oracle', 'runner'):
        assert summary[role + 'AssemblySha256'] == manifest[role + 'AssemblySha256']
    execution = json.loads((directory / 'frozen-execution.json').read_bytes())
    assert execution['frozenSourceAndBinariesUnchanged'] is True
    assert execution['freshManifestBoundReport'] is True and execution['diagnosticNoReplay'] is False
    for case in summary['cases']:
        name = case['Name']
        assert hashlib.sha256(compact((directory / 'inputs' / (name + '.json')).read_bytes())).hexdigest() == case['InputSha256']
        assert sha_file(directory / case['FullReport']) == case['FullReportSha256']
    print(directory.name, summary['passed'], 'pass', summary['failed'], 'fail', summary['unsupported'], 'unsupported; archived bytes verified')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reports-root', type=Path, default=Path('/workspace/navigation-comparison/results'))
    parser.add_argument('--output-root', type=Path, default=Path(__file__).resolve().parent)
    parser.add_argument('--window-lines', type=int, default=128)
    parser.add_argument('--verify-only', action='store_true')
    parser.add_argument('--label', action='append', choices=['D12'], help='Archive/verify only completed named candidates; default both')
    args = parser.parse_args()
    assert args.window_lines > 0
    labels = args.label or ['D12']
    if args.verify_only:
        for label in labels:
            verify(args.output_root)
        return
    # Refuse to create an archive from partial executions, including partial success placeholders.
    for label in labels:
        source = args.reports_root / 'd-final-stress'
        validate_summary(json.loads((source / 'summary.json').read_bytes()))
        execution = json.loads((source / 'frozen-execution.json').read_bytes())
        assert execution.get('frozenSourceAndBinariesUnchanged') is True
        assert execution.get('freshManifestBoundReport') is True and execution.get('diagnosticNoReplay') is False
    for label in labels:
        source = args.reports_root / 'd-final-stress'
        destination = args.output_root
        summary = json.loads((source / 'summary.json').read_bytes())
        assert sha_file(destination / 'build-manifest.json') == sha_file(source / 'build-manifest.json')
        files = ['summary.json', 'frozen-execution.json', 'build-manifest.json']
        files += [path.name for path in source.glob('compile-items-*.json')]
        files += [path.name for path in source.glob('build-*.log')]
        for case in summary['cases']:
            assert Path(case['Name']).name == case['Name']
            files += ['inputs/' + case['Name'] + '.json', case['FullReport']]
        for relative in files:
            target = destination / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            if (source / relative).resolve() != target.resolve():
                shutil.copyfile(source / relative, target)
        windows = {}
        for case in summary['cases']:
            if case['Status'] == 'Failed' and case.get('TraceFileSha256'):
                windows[case['Name']] = trace_windows(source / case['Trace'], destination, case, args.window_lines)
            else:
                windows[case['Name']] = {
                    'fullTraceNotIncluded': True, 'originalFullTracePath': str(source / case['Trace']) if case.get('Trace') else None,
                    'originalFileSha256': case.get('TraceFileSha256'), 'logicalPhysicalTraceSha256': case.get('TraceSha256')
                }
        write_json(destination / 'trace-storage.json', windows)
        subset_file = args.output_root / 'full41-input-subset.json'
        if subset_file.exists():
            subset = json.loads(subset_file.read_bytes())['inputSha256']
            assert len(subset) == 41
            by_name = {case['Name']: case for case in summary['cases']}
            selected = []
            for name, expected in subset.items():
                case = by_name[name]
                assert case['InputSha256'] == expected
                selected.append({key: case[key] for key in ['Name', 'Status', 'InputSha256', 'ReplayVerified', 'FinalTick']})
            subset_destination = destination / 'full41-input-subset.json'
            if subset_file.resolve() != subset_destination.resolve():
                shutil.copyfile(subset_file, subset_destination)
            write_json(destination / 'full41-derived.json', {
                'scope': 'Exact input-hash subset derived from original completed stress49. No separate full41 simulation or fresh report is claimed.',
                'sourceStressSummarySha256': sha_file(source / 'summary.json'),
                'inputSubsetManifestSha256': sha_file(subset_file),
                'passed': sum(case['Status'] == 'Passed' for case in selected),
                'failed': sum(case['Status'] == 'Failed' for case in selected),
                'unsupported': sum(case['Status'] == 'Unsupported' for case in selected),
                'caseReferences': selected
            })
            files += ['full41-input-subset.json', 'full41-derived.json']
        files += ['trace-storage.json']
        files += [str(path.relative_to(destination)) for path in (destination / 'trace-windows').glob('*')]
        write_json(destination / 'model-archive-index.json', {
            'scope': 'Original frozen reports/inputs are byte-identical. Failed-case trace windows are derived partial evidence; full traces are external and not reverified by portable archive checks.',
            'files': {relative: sha_file(destination / relative) for relative in sorted(set(files))}
        })
        verify(destination)


if __name__ == '__main__':
    main()
