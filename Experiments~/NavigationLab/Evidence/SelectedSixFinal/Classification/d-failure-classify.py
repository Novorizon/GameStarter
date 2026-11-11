#!/usr/bin/env python3
"""Read original case reports and classify observed unmet conditions, never causes."""
from collections import Counter
from pathlib import Path
import argparse
import gzip
import hashlib
import json
import sys


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def derive_old_waiting(case, definition, directory, reference):
    result = {'status': 'unknown', 'scope': 'First actual death boundary and final ID/state; not pass-only report defaults.'}
    commands = [row for row in case.get('Commands', []) if row.get('kind') == 'death']
    if not commands:
        result['reason'] = 'No actual death command.'
        return result
    if len(definition.get('deaths', [])) != 1 or len({row['microstep'] for row in commands}) != 1:
        result['reason'] = 'First-boundary derivation does not establish the union across multiple death events.'
        return result
    expected = reference.get('TraceFileSha256')
    trace = (directory / case.get('Trace', '')).resolve()
    if not expected or not trace.is_file() or not trace.is_relative_to(directory.resolve()):
        result['reason'] = 'No usable trace bound to a completed summary.'
        return result
    result.update({'tracePath': str(trace), 'traceCompressedSha256': sha(trace),
                   'expectedTraceCompressedSha256': expected,
                   'reportedLogicalTraceSha256': case.get('TraceSha256')})
    if result['traceCompressedSha256'] != expected:
        result['reason'] = 'Trace compressed SHA differs from the original summary.'
        return result
    death_step = commands[0]['microstep']
    victims = {row['Id'] for row in commands}
    state = {}
    old = None
    before_count = None
    boundary_removed = None
    with gzip.open(trace, 'rt', encoding='utf-8') as stream:
        for line in stream:
            row = json.loads(line)
            for identity in row.get('removed', []):
                state.pop(identity, None)
            for body in row.get('changed', []):
                state[body[0]] = body[3]
            if row.get('step') == death_step:
                old = {identity for identity, value in state.items() if value == 2}
                before_count = len(state)
            if row.get('step') == death_step + 1:
                boundary_removed = set(row.get('removed', []))
    final = {int(row.split(':')[0]): int(row.split(':')[2]) for row in case.get('FinalState', '').split('|') if row}
    if old is None or boundary_removed is None or not victims.issubset(boundary_removed) or state != final:
        result['reason'] = 'Trace death boundary/removals or final ID/state cannot be reconciled.'
        return result
    final_counts = Counter(final.get(identity, 'Absent') for identity in old)
    result.update({
        'status': 'verified',
        'deathBoundary': {'firstDeathCommandMicrostep': death_step, 'lastTraceStepBeforeDeath': death_step,
                          'firstPostDeathTraceStep': death_step + 1, 'victimIds': sorted(victims),
                          'removedIdsAtFirstPostDeathTraceStep': sorted(boundary_removed),
                          'bodiesBeforeDeath': before_count},
        'derivedOldWaitingBeforeDeaths': len(old), 'derivedOldWaitingIds': sorted(old),
        'derivedPromotedOldWaitingFinal': final_counts[1],
        'derivedOldWaitingFinalStateCounts': {
            'Attacking': final_counts[1], 'Moving': final_counts[0], 'Waiting': final_counts[2],
            'Absent': final_counts['Absent']},
        'finalTraceIdStateMatchesReport': True
    })
    return result


def classify(case, definition, old_waiting_derivation=None):
    labels = []
    facts = {key: case[key] for key in (
        'Status', 'Error', 'InitialBatchVerified', 'Admitted', 'Alive', 'Pending', 'Attacking',
        'Waiting', 'Deaths', 'FinalTick',
        'HoldTicks', 'HoldMaximumDriftMm', 'ReplayVerified', 'MetricsReadError'
    ) if key in case}
    facts['reportedPassOnlyFields'] = {key: case[key] for key in ('OldWaitingBeforeDeaths', 'PromotedOldWaiting') if key in case}
    facts['reportedPassOnlyFieldsNote'] = 'Runner only assigns these in its PASS branch; failed-report defaults are not used as measurements.'
    if old_waiting_derivation is not None:
        facts['oldWaitingDerivation'] = old_waiting_derivation
        if old_waiting_derivation.get('status') == 'verified':
            for key in ('derivedOldWaitingBeforeDeaths', 'derivedPromotedOldWaitingFinal', 'derivedOldWaitingFinalStateCounts'):
                facts[key] = old_waiting_derivation[key]
    counts = Counter()
    profiles = {row['id']: row['attackRangeMm'] for row in definition.get('requests', [])
                if 'id' in row and 'attackRangeMm' in row}
    commands = case.get('Commands')
    if isinstance(commands, list):
        for row in commands:
            if row.get('kind') == 'spawn' and 'Id' in row and 'range' in row:
                profiles[row['Id']] = row['range']
        facts['actualDeathCommandCount'] = sum(row.get('kind') == 'death' for row in commands)
        facts['actualWorldChangeCommands'] = [row for row in commands if row.get('kind') == 'world-change']
    bodies = []
    if case.get('FinalState'):
        for row in case['FinalState'].split('|'):
            identity, _, state, _, _ = row.split(':')
            identity, state = int(identity), int(state)
            bodies.append((identity, state))
            counts[state] += 1
        facts['derivedFinalStateCounts'] = {
            'Moving': counts[0], 'Attacking': counts[1], 'Waiting': counts[2],
            'Other': sum(value for key, value in counts.items() if key not in (0, 1, 2))
        }
        facts['derivedAttackersByRange'] = dict(sorted(Counter(
            str(profiles[identity]) for identity, state in bodies if state == 1 and identity in profiles
        ).items()))
        unknown = [identity for identity, _ in bodies if identity not in profiles]
        if unknown:
            facts['profileUnknownFinalIds'] = unknown
    expected = {key: definition[key] for key in (
        'expectation', 'expectedLive', 'expectedAttackers', 'initialSettledCheckTick',
        'minimumAttackersByRange', 'minimumOldWaitingPromotions', 'holdTicks'
    ) if key in definition}
    facts['inputConditions'] = expected
    failed = case.get('Status') == 'Failed'
    if not failed:
        return {'labels': [], 'facts': facts}
    error = case.get('Error') or ''
    future_original = sum(row.get('readyTick', 0) > case.get('FinalTick', -1)
                          for row in definition.get('requests', []))
    initial_tick = definition.get('initialSettledCheckTick')
    if error.startswith('Initial crowd did not physically settle'):
        labels.append('初始批次前提失败')
    elif initial_tick is not None and case.get('FinalTick', -1) >= initial_tick and case.get('InitialBatchVerified') is False:
        labels.append('初始批次检查未验证')
    if bodies and counts[0]:
        labels.append('最终仍有移动单位')
    if definition.get('expectation') == 'FullArrival' and case.get('Pending', 0) > 0:
        # A failed initial check can happen before the late batch is even due.
        # Those future requests do not demonstrate rejected or blocked admission.
        future = future_original
        facts['originalRequestsScheduledAfterFinalTick'] = future
        due_pending = max(0, case['Pending'] - future)
        facts['pendingOtherThanFutureOriginalRequests'] = due_pending
        if due_pending:
            labels.append('全量到位场景仍有已到计划时间的外部待入队请求')
        elif future:
            labels.append('后续请求尚未到计划出生时间（非准入阻塞证据）')
    if definition.get('expectation') == 'EndpointPolicyOverflow':
        facts['pendingIsAllowedByOverflowPolicy'] = True
        if 'Alive' in case and 'expectedLive' in definition and case['Alive'] < definition['expectedLive']:
            labels.append('容量场景未达到要求的活体数')
    minimum = definition.get('minimumAttackersByRange', {})
    if minimum and bodies and not facts.get('profileUnknownFinalIds') and future_original == 0:
        actual = facts['derivedAttackersByRange']
        deficits = {str(key): {'required': value, 'actual': actual.get(str(key), 0)}
                    for key, value in minimum.items() if actual.get(str(key), 0) < value}
        if deficits:
            facts['rangeAttackQuotaDeficits'] = deficits
            labels.append('攻击射程份额不足')
    if future_original == 0 and 'expectedAttackers' in definition and definition['expectedAttackers'] is not None and 'Attacking' in case:
        if case['Attacking'] != definition['expectedAttackers']:
            labels.append('攻击人数未达到输入要求')
    deaths = definition.get('deaths', [])
    if deaths and isinstance(commands, list):
        planned = sum(row.get('Count', 0) for row in deaths)
        actual = facts['actualDeathCommandCount']
        facts['plannedDeathCount'] = planned
        if actual < planned:
            if actual == 0 and all(row.get('AfterSettlement') is True for row in deaths):
                labels.append('到位条件后的死亡阶段未触发')
            else:
                labels.append('计划死亡阶段未完成')
    world = definition.get('worldEvents', [])
    if world and isinstance(commands, list):
        observed = facts['actualWorldChangeCommands']
        attempted = {row['index'] for row in observed if 'index' in row}
        missing = [index for index in range(len(world)) if index not in attempted]
        facts['untriggeredWorldEventIndices'] = missing
        if missing:
            labels.append('计划世界变化阶段未触发完整')
        if any(row.get('accepted') is False for row in observed):
            labels.append('已声明的世界变化被拒绝')
    required_promotions = definition.get('minimumOldWaitingPromotions', 0)
    if required_promotions:
        if deaths and facts.get('actualDeathCommandCount') == 0:
            labels.append('旧候场补位目标尚未验证（死亡阶段未触发）')
        elif old_waiting_derivation is not None and old_waiting_derivation.get('status') == 'verified':
            if old_waiting_derivation['derivedPromotedOldWaitingFinal'] < required_promotions:
                labels.append('旧候场单位补位数不足')
        else:
            labels.append('旧候场补位数未知（缺少可核轨迹）')
    if error.startswith('Fresh-instance replay differs:'):
        labels.append('全新实例重放不一致')
    elif error.startswith('Actual noncentre moving death trigger never became valid.'):
        labels.append('移动中死亡触发前提未达')
    elif error and not error.startswith(('Deadline:', 'Initial crowd did not physically settle')):
        labels.append('运行异常或契约断言')
    if case.get('MetricsReadError'):
        labels.append('指标读取异常')
    if bodies and counts[0] == 0:
        labels.append('最终全体静止但仍未满足全部验收条件')
    if not labels:
        labels.append('需查原始错误和事件记录')
    return {'labels': list(dict.fromkeys(labels)), 'facts': facts}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', type=Path, default=Path('/workspace/navigation-comparison/results/d-final-stress/summary.json'))
    parser.add_argument('--output', type=Path, default=Path('/workspace/navigation-comparison/audit/d-failure-classification.json'))
    parser.add_argument('--allow-partial', action='store_true', help='Diagnostic finished-case files only; never a completed suite claim.')
    args = parser.parse_args()
    directory = args.report.parent
    summary = json.loads(args.report.read_bytes())
    cases = summary.get('cases', [])
    complete = len(cases) == 49 and summary.get('formalEvidenceReady') is True
    if not complete and not args.allow_partial:
        print('D完整49项报告尚未就绪；未生成分类。', file=sys.stderr)
        return 75
    entries = [(directory / row['FullReport'], row) for row in cases] if complete else [
        (path, {}) for path in sorted(directory.glob('*.json.gz'))
    ]
    results = []
    for path, reference in entries:
        if reference:
            if sha(path) != reference['FullReportSha256']:
                raise ValueError('原完整案例SHA不一致: ' + str(path))
        case = json.loads(gzip.decompress(path.read_bytes()) if path.suffix == '.gz' else path.read_bytes())
        name = case['Name']
        input_path = directory / 'inputs' / (name + '.json')
        definition = json.loads(input_path.read_bytes())
        derivation = None
        if case.get('Status') == 'Failed' and any(row.get('kind') == 'death' for row in case.get('Commands', [])):
            derivation = derive_old_waiting(case, definition, directory, reference)
        item = classify(case, definition, derivation)
        item.update({'name': name, 'sourceReport': str(path), 'sourceReportSha256': sha(path),
                     'sourceInput': str(input_path), 'sourceInputSha256': sha(input_path)})
        results.append(item)
    output = {
        'scope': '原报告条件的诊断派生分类，可多标签；不判断根因，不增加覆盖，不代替严格验收。缺字段不猜。',
        'completeStress49Source': complete,
        'driverSha256': sha(Path(__file__)),
        'sourceSummary': str(args.report), 'sourceSummarySha256': sha(args.report),
        'caseCount': len(results), 'failures': [row for row in results if row['facts'].get('Status') == 'Failed']
    }
    output['failureLabelCounts'] = dict(Counter(label for row in output['failures'] for label in row['labels']))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(output, ensure_ascii=False, indent=2) + '\n')
    prefix = '完整49' if complete else '部分诊断'
    print(prefix + '：已读' + str(len(results)) + '例，失败' + str(len(output['failures'])) + '例；标签可重叠。')
    for row in output['failures']:
        print(row['name'] + '：' + '；'.join(row['labels']))
    return 0


if __name__ == '__main__':
    sys.exit(main())
