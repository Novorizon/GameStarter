#!/usr/bin/env python3
"""Strict, read-only comparison of frozen NavigationLab model reports.

Example: report.py --report A=/abs/a/summary.json --report B=/abs/b/summary.json
                   --output /abs/comparison --diagram forest-full44-large
No simulations, rebuilds, performance rankings, or trace copying are performed.
"""
import argparse
import copy
import gzip
import hashlib
import html
import json
import math
from pathlib import Path
import re
import sys


class EvidenceError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise EvidenceError(message)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def file_sha(path):
    require(path.is_file(), '缺少证据文件: ' + str(path))
    value = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            value.update(block)
    return value.hexdigest()


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'JSON 包含重复键: ' + key)
        result[key] = value
    return result


def parse_json(raw):
    def invalid_number(value):
        raise EvidenceError('JSON 包含非法数值: ' + value)
    return json.loads(raw, object_pairs_hook=unique_object, parse_constant=invalid_number)


def read_json(path):
    require(path.is_file(), '缺少证据文件: ' + str(path))
    return parse_json(path.read_bytes())


def compact_bytes(raw):
    """Remove formatting only; preserve .NET number spelling and string escaping."""
    result = bytearray()
    quoted = escaped = False
    for byte in raw:
        if quoted:
            result.append(byte)
            if escaped:
                escaped = False
            elif byte == 92:
                escaped = True
            elif byte == 34:
                quoted = False
        elif byte == 34:
            quoted = True
            result.append(byte)
        elif byte not in b' \t\r\n':
            result.append(byte)
    require(not quoted, '未闭合的 JSON 字符串')
    return bytes(result)


def array_members(raw, field):
    """Keep original JSON lexemes, including .NET floating-point formatting."""
    text = raw.decode('utf-8')
    matches = list(re.finditer(r'"' + re.escape(field) + r'"\s*:\s*\[', text))
    require(len(matches) == 1, '报告缺少唯一 ' + field + ' 数组')
    cursor = matches[0].end()
    decoder = json.JSONDecoder()
    members = []
    while True:
        while text[cursor].isspace():
            cursor += 1
        if text[cursor] == ']':
            return members
        _, end = decoder.raw_decode(text, cursor)
        members.append(compact_bytes(text[cursor:end].encode('utf-8')))
        cursor = end
        while text[cursor].isspace():
            cursor += 1
        require(text[cursor] in ',]', '非法 ' + field + ' 分隔符')
        if text[cursor] == ',':
            cursor += 1


def commands_sha(raw):
    digest = hashlib.sha256()
    for member in array_members(raw, 'Commands'):
        digest.update(member + b'\n')
    return digest.hexdigest()


def checked_hash(value, description):
    require(isinstance(value, str) and re.fullmatch('[0-9a-f]{64}', value), '缺少或非法 SHA-256: ' + description)
    return value


def local_file(directory, name):
    path = (directory / name).resolve()
    require(path.is_relative_to(directory.resolve()), '报告内路径越界: ' + str(name))
    return path


RUNNER_IDENTITY_SCHEME = 'NavigationLab.Runner.CompileIdentity.v1'
RUNNER_PROJECT = 'Experiments~/NavigationLab/Runner/NavigationLab.Runner.csproj'
RUNNER_ASSEMBLY_INFO = 'Experiments~/NavigationLab/Runner/obj/Release/net8.0/NavigationLab.Runner.AssemblyInfo.cs'


def normalize_runner_metadata(raw, project, snapshot):
    """Normalize one generated attribute only, after validating its exact value."""
    text = raw.decode('utf-8')
    literal = r'"(?:[^"\\]|\\.)*"'
    pattern = (r'^\[assembly: System\.Reflection\.AssemblyMetadata\("RepositoryRoot", ('
               + literal + r'(?:\s*\+\s*' + literal + r')*)\)\]$')
    matches = list(re.finditer(pattern, text, re.MULTILINE))
    require(len(matches) == 1, 'Runner AssemblyInfo 必须含唯一且形状准确的 RepositoryRoot 属性')
    require(len(re.findall(r'"RepositoryRoot"', text)) == 1, 'Runner AssemblyInfo 有额外 RepositoryRoot 内容')
    match = matches[0]
    root_value = ''.join(json.loads(fragment) for fragment in re.findall(literal, match.group(1)))
    expected = project.parent.as_posix() + '/../../../'
    require(root_value == expected, 'Runner RepositoryRoot 不是该项目生成的精确根路径')
    require(Path(root_value).resolve() == snapshot, 'Runner RepositoryRoot 未指向冻结 snapshot')
    replacement = '[assembly: System.Reflection.AssemblyMetadata("RepositoryRoot", "<FROZEN_SNAPSHOT>/Experiments~/NavigationLab/Runner/../../../")]'
    normalized = (text[:match.start()] + replacement + text[match.end():]).encode('utf-8')
    return normalized, root_value


def runner_identity(manifest):
    """Identity of actual compile inputs, not a claim that raw Runner DLLs match.

    RepositoryRoot is read at runtime. Its single normalized exception is valid
    only with each snapshot's frozen input checks and equal per-case InputSha256.
    """
    snapshot = Path(manifest['snapshot']).resolve()
    require(snapshot.is_dir(), 'Runner 冻结 snapshot 不存在')
    projects = [item for item in manifest.get('compileItems', [])
                if Path(item['project']).resolve() == snapshot / RUNNER_PROJECT]
    require(len(projects) == 1, '构建清单缺少唯一实际 Runner Compile 项')
    item = projects[0]
    project = Path(item['project']).resolve()
    properties = item.get('properties')
    require(isinstance(properties, dict) and properties.get('TargetFramework') == 'net8.0'
            and properties.get('LangVersion') == '12', '缺少受支持的 Runner 实际编译属性')
    sources = manifest.get('sourceFiles', {})
    project_hash = file_sha(project)
    require(sources.get(str(project)) == project_hash, 'Runner csproj 未与实际冻结源绑定')
    require(manifest.get('relativeOriginalSourceHashes', {}).get(RUNNER_PROJECT) == project_hash, 'Runner csproj 不匹配原始冻结源')
    raw_dll_hash = checked_hash(manifest.get('runnerAssemblySha256'), 'Runner DLL')
    require(file_sha(Path(manifest['runnerAssemblyPath'])) == raw_dll_hash, 'Runner 实际 DLL 与构建清单不符')
    compile_files = item.get('compileFiles')
    require(isinstance(compile_files, dict) and compile_files, '缺少 Runner 实际 Compile 文件清单')
    identities = {}
    file_details = {}
    metadata_value = None
    for filename, expected in compile_files.items():
        path = Path(filename).resolve()
        require(path.is_relative_to(snapshot), 'Runner Compile 文件在 snapshot 外: ' + filename)
        relative = path.relative_to(snapshot).as_posix()
        require(relative not in identities, 'Runner Compile 文件别名重复: ' + relative)
        raw = path.read_bytes()
        actual = sha(raw)
        require(actual == expected and sources.get(filename) == actual, 'Runner Compile 文件已变更或未绑定: ' + filename)
        normalized = raw
        if relative == RUNNER_ASSEMBLY_INFO:
            normalized, metadata_value = normalize_runner_metadata(raw, project, snapshot)
        identities[relative] = sha(normalized)
        file_details[relative] = {'sourceSha256': actual, 'identitySha256': identities[relative], 'normalized': normalized != raw}
    require(metadata_value is not None, '实际 Runner Compile 项缺少生成 AssemblyInfo')
    compiler_info = manifest.get('dotnetInfo')
    require(isinstance(compiler_info, str) and compiler_info, '缺少实际 .NET SDK/编译器环境记录')
    identity_input = {'scheme': RUNNER_IDENTITY_SCHEME, 'project': RUNNER_PROJECT, 'projectSha256': project_hash,
                      'actualCompileProperties': properties, 'compilerInfoSha256': sha(compiler_info.encode('utf-8')),
                      'compileFiles': identities}
    digest = sha(json.dumps(identity_input, sort_keys=True, separators=(',', ':')).encode('utf-8'))
    return {'scheme': RUNNER_IDENTITY_SCHEME, 'sha256': digest, 'identityInput': identity_input,
            'rawRunnerAssemblySha256': raw_dll_hash, 'recordedCompilePropertyNames': sorted(properties), 'repositoryRootMetadata': metadata_value,
            'resolvedRepositoryRoot': str(snapshot), 'compileFileDetails': file_details,
            'normalizationScope': 'Only the unique generated RepositoryRoot attribute is replaced after exact project-path and snapshot-resolution checks; every other source byte is retained. Runtime input equality still requires per-case InputSha256.'}


def load_report(label, path):
    require(path.is_absolute(), '--report 必须使用绝对路径')
    path = path.resolve()
    raw = path.read_bytes()
    summary = parse_json(raw)
    prefix = label + ': '
    require(summary.get('schemaVersion') == 1, prefix + '不支持的摘要版本')
    for flag in ('replayRequested', 'buildProvenanceVerified', 'formalEvidenceReady', 'sourceFilesUnchanged', 'finiteCorpusOnly'):
        require(summary.get(flag) is True, prefix + flag + ' 必须明确为 true')
    require(isinstance(summary.get('sourceBefore'), dict) and summary['sourceBefore'], prefix + '缺少 sourceBefore')
    require(summary['sourceBefore'] == summary.get('sourceAfter'), prefix + '源文件前后不一致')
    for field in ('algorithm', 'actualFactoryType', 'suite', 'protocol'):
        require(summary.get(field), prefix + '缺少 ' + field)
    directory = path.parent
    manifest_path = directory / 'build-manifest.json'
    manifest_hash = file_sha(manifest_path)
    require(manifest_hash == summary.get('buildManifestSha256'), prefix + '摘要与 build-manifest 不匹配，可能是旧摘要')
    manifest = read_json(manifest_path)
    require(manifest.get('factory') == summary['actualFactoryType'], prefix + '实际 factory 与构建清单不一致')
    for role in ('runner', 'core', 'oracle', 'candidate'):
        key = role + 'AssemblySha256'
        expected = checked_hash(summary.get(key), prefix + key)
        require(manifest.get(key) == expected, prefix + role + ' 摘要与构建清单 hash 不一致')
        require(file_sha(Path(manifest[role + 'AssemblyPath'])) == expected, prefix + role + ' DLL 已变更')
    require(isinstance(manifest.get('sourceFiles'), dict) and manifest['sourceFiles'], prefix + '缺少冻结源文件清单')
    for name, expected in manifest['sourceFiles'].items():
        require(file_sha(Path(name)) == expected, prefix + '冻结源文件已变更: ' + name)
    runner_compile_identity = runner_identity(manifest)
    cases = summary.get('cases')
    require(isinstance(cases, list) and cases, prefix + '没有案例')
    raw_cases = {parse_json(member)['Name']: member for member in array_members(raw, 'cases')}
    by_name = {}
    counts = {'Passed': 0, 'Failed': 0, 'Unsupported': 0}
    case_evidence = {}
    for case in cases:
        name = case.get('Name')
        require(isinstance(name, str) and name and Path(name).name == name and '\\' not in name, prefix + '非法案例名称')
        require(name not in by_name, prefix + '重复案例: ' + name)
        status = case.get('Status')
        require(status != 'InvalidInput', prefix + name + ' 为 InvalidInput，不能计作算法得分')
        require(status in counts, prefix + name + ' 未知状态: ' + str(status))
        counts[status] += 1
        require(status != 'Passed' or case.get('ReplayVerified') is True, prefix + name + ' Passed 未验证回放')
        input_path = local_file(directory / 'inputs', name + '.json')
        input_raw = input_path.read_bytes()
        definition = parse_json(input_raw)
        input_hash = checked_hash(case.get('InputSha256'), name + ' InputSha256')
        require(sha(compact_bytes(input_raw)) == input_hash, prefix + name + ' 导出输入与 InputSha256 不匹配')
        protocol = summary['protocol']
        for field in ('microstepMilliseconds', 'logicalTickMilliseconds'):
            require(definition.get(field) == protocol.get(field), prefix + name + ' 输入 protocol 不一致')
        full = case
        full_path = path
        full_file_hash = sha(raw)
        if 'FullReport' in case:
            full_path = local_file(directory, case['FullReport'])
            full_file_hash = file_sha(full_path)
            require(full_file_hash == case.get('FullReportSha256'), prefix + name + ' 完整案例压缩文件 hash 不匹配')
            full_raw = gzip.decompress(full_path.read_bytes()) if full_path.suffix == '.gz' else full_path.read_bytes()
            full = parse_json(full_raw)
            extra = {'FullReport', 'FullReportSha256', 'TraceFileSha256'}
            require(all(full.get(key) == value for key, value in case.items() if key not in extra), prefix + name + ' 摘要与完整案例不一致')
        else:
            require(isinstance(full.get('Commands'), list), prefix + name + ' 缺少 Commands/FullReport')
            full_raw = raw_cases[name]
        unexecuted = not full.get('Commands') and full.get('CheckedMicrosteps') == 0 and not full.get('FinalState') and status != 'Passed'
        if not (unexecuted and case.get('CommandsSha256') == ''):
            require(commands_sha(full_raw) == case.get('CommandsSha256'), prefix + name + ' CommandsSha256 不匹配')
        trace = local_file(directory, case['Trace']) if case.get('Trace') else None
        trace_file_hash = case.get('TraceFileSha256')
        if trace_file_hash is not None:
            require(trace is not None and file_sha(trace) == trace_file_hash, prefix + name + ' trace 文件 hash 不匹配')
        data = {key: value for key, value in case.items() if key != 'Commands'}
        data.update({'sourceSummary': str(path), 'sourceSummarySha256': sha(raw), 'sourceFullReport': str(full_path),
                     'sourceFullReportFileSha256': full_file_hash, 'inputFile': str(input_path), 'inputFileSha256': sha(input_raw),
                     'sourceTrace': str(trace) if trace else None, 'CommandsCount': len(full.get('Commands', []))})
        by_name[name] = data
        case_evidence[name] = (definition, full, input_raw)
    for key, status in (('passed', 'Passed'), ('failed', 'Failed'), ('unsupported', 'Unsupported')):
        require(type(summary.get(key)) is int and summary[key] == counts[status], prefix + '摘要计数不一致: ' + key)
    require(summary.get('invalidInputs', 0) == 0, prefix + '存在 InvalidInput')
    execution_path = directory / 'frozen-execution.json'
    execution = read_json(execution_path)
    require(execution.get('frozenSourceAndBinariesUnchanged') is True, prefix + '冻结执行后验检查未通过')
    require(execution.get('buildManifestSha256') == manifest_hash, prefix + '冻结执行与构建清单不匹配')
    require(execution.get('freshManifestBoundReport') is True and execution.get('diagnosticNoReplay') is False, prefix + '冻结执行为陈旧/无回放报告')
    isolation = summary.get('isolationAndPauseVerified') is True
    expected_exit = 1 if counts['Failed'] or not isolation else 2 if counts['Unsupported'] else 0
    require(type(execution.get('returnCode')) is int and execution['returnCode'] == expected_exit, prefix + '冻结执行退出码与案例不一致')
    require(type(summary.get('success')) is bool and summary['success'] == (counts['Failed'] == counts['Unsupported'] == 0 and isolation), prefix + 'success 与案例/隔离证据不一致')
    require(summary.get('supportedCasesPassed') is (counts['Failed'] == 0 and isolation), prefix + 'supportedCasesPassed 与证据不一致')
    require(summary.get('allSuiteCasesSupported') is (counts['Unsupported'] == 0), prefix + 'allSuiteCasesSupported 与证据不一致')
    require(type(summary.get('isolationAndPauseVerified')) is bool, prefix + '缺少隔离验证结果')
    return {'label': label, 'path': str(path), 'sha256': sha(raw), 'summary': summary, 'manifestSha256': manifest_hash,
            'manifest': manifest, 'runnerIdentity': runner_compile_identity, 'cases': by_name, 'evidence': case_evidence, 'counts': counts}


def applied_world(definition, full):
    world = copy.deepcopy(definition['world'])
    walkable = list(world['walkable'])
    applied = []
    for command in full.get('Commands', []):
        if command.get('kind') != 'world-change' or command.get('accepted') is False:
            continue
        index = command['index']
        require(type(index) is int and 0 <= index < len(definition['worldEvents']), 'world-change 索引非法')
        require(index not in applied, 'world-change 重复执行')
        event = definition['worldEvents'][index]
        for cell in event.get('BlockCells', []):
            walkable[cell] = '0'
        for cell in event.get('OpenCells', []):
            walkable[cell] = '1'
        replacement = event.get('replaceTarget')
        if replacement is not None:
            world['targets'] = [replacement if target['id'] == replacement['id'] else target for target in world['targets']]
        applied.append(index)
    world['walkable'] = ''.join(walkable)
    return world, applied


def final_bodies(definition, full):
    profiles = {request['id']: request['radiusMm'] for request in definition['requests']}
    for command in full.get('Commands', []):
        if command.get('kind') == 'spawn':
            identity, radius = command['Id'], command['radius']
            require(identity not in profiles or profiles[identity] == radius, '出生半径与原始请求不一致')
            profiles[identity] = radius
    bodies = []
    for item in filter(None, full.get('FinalState', '').split('|')):
        identity, coordinate, state, goal, target = item.split(':')
        x, y = map(float, coordinate.split(','))
        identity, state, goal, target = map(int, (identity, state, goal, target))
        require(math.isfinite(x) and math.isfinite(y), 'FinalState 包含非有限坐标；请查原始失败报告')
        require(identity in profiles, 'FinalState ID 没有原始请求或出生半径: ' + str(identity))
        bodies.append({'id': identity, 'x': x, 'y': y, 'state': state, 'goal': goal, 'target': target, 'radius': profiles[identity]})
    require(len({body['id'] for body in bodies}) == len(bodies), 'FinalState ID 重复')
    return bodies


def escape(value):
    return html.escape(str(value), quote=True)


def svg_diagram(label, name, definition, full):
    world, applied = applied_world(definition, full)
    bodies = final_bodies(definition, full)
    width, height, size = world['width'], world['height'], world['cellSizeMm']
    origin = world['origin']
    scale = min(1100 / (width * size), 720 / (height * size))
    canvas_height = height * size * scale + 155
    def point(x, y):
        return 50 + (x - origin['x']) * scale, 95 + (origin['y'] + height * size - y) * scale
    def center(cell):
        return origin['x'] + (cell % width + .5) * size, origin['y'] + (cell // width + .5) * size
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 1200 {canvas_height:.3f}" role="img">',
             '<rect width="100%" height="100%" fill="#f6f8fb"/>',
             '<style>text{font-family:Arial,"Noto Sans CJK SC",sans-serif} .label{font-size:15px;fill:#263344}</style>',
             f'<title>{escape(label)} · {escape(name)} · FinalState</title>',
             f'<text x="35" y="30" font-size="21">{escape(label)} · {escape(name)} · {escape(full["Status"])}</text>',
             f'<text x="35" y="54" class="label">最终 tick {full.get("FinalTick")}；活体 {len(bodies)}；只应用实际 world-change 索引 {escape(applied)}</text>',
             '<text x="35" y="76" class="label">橙=移动　红=攻击　蓝=候场；圆半径为真实身体；虚线圆/连线为目标；深色=闭格，浅灰=仅通行</text>']
    for cell, value in enumerate(world['walkable']):
        x, y = point(origin['x'] + cell % width * size, origin['y'] + (cell // width + 1) * size)
        colour = '#263344' if value == '0' else '#dcebd5' if world['holding'][cell] == '1' else '#e7e8ed'
        if value == '1' and cell in world['spawnCells']:
            colour = '#b9d8f6'
        parts.append(f'<rect x="{x:.4f}" y="{y:.4f}" width="{size*scale:.4f}" height="{size*scale:.4f}" fill="{colour}" stroke="#fff" stroke-width=".25"/>')
    for target in world['targets']:
        x, y = point(target['x'], target['y'])
        parts.append(f'<circle cx="{x:.4f}" cy="{y:.4f}" r="{target["radiusMm"]*scale:.4f}" fill="#485874" stroke="#172233"/>'
                     f'<text x="{x:.4f}" y="{y+4:.4f}" text-anchor="middle" fill="white" font-size="11">基地{target["id"]}</text>')
    for body in bodies:
        x, y = point(body['x'], body['y'])
        radius = body['radius'] * scale
        if 0 <= body['goal'] < width * height:
            gx, gy = point(*center(body['goal']))
            parts.append(f'<path d="M{x:.4f},{y:.4f} L{gx:.4f},{gy:.4f}" stroke="#687b8c" stroke-width=".55" opacity=".4"/>'
                         f'<circle cx="{gx:.4f}" cy="{gy:.4f}" r="{radius:.4f}" fill="none" stroke="#687b8c" stroke-dasharray="2 2" stroke-width=".6"/>')
    for body in bodies:
        x, y = point(body['x'], body['y'])
        radius = body['radius'] * scale
        colour = {0: '#efb23c', 1: '#e2605a', 2: '#5798d3'}.get(body['state'], '#b146c2')
        parts.append(f'<g><title>ID {body["id"]}; radius {body["radius"]}mm; state {body["state"]}; goal {body["goal"]}; target {body["target"]}</title>'
                     f'<circle cx="{x:.4f}" cy="{y:.4f}" r="{radius:.4f}" fill="{colour}" fill-opacity=".88" stroke="#273344" stroke-width=".55"/>'
                     f'<text x="{x:.4f}" y="{y+min(9,radius*.9)/3:.4f}" text-anchor="middle" font-size="{min(9,radius*.9):.3f}" fill="#102030">{body["id"]}</text></g>')
    parts.append(f'<text x="35" y="{canvas_height-20:.3f}" class="label">静态最终态示意，不证明行进路径正确；失败案例可能含非法输出，以原始报告及连续 oracle 结果为准。</text></svg>')
    return '\n'.join(parts) + '\n', applied


def md(value):
    return str(value).replace('|', '\\|').replace('\r', ' ').replace('\n', ' ')


def slug(value):
    prefix = re.sub(r'[^A-Za-z0-9_-]+', '-', value).strip('-')[:65] or 'report'
    return prefix + '-' + sha(value.encode())[:8]


def validate_common_harness(reports):
    first = reports[0]
    for report in reports[1:]:
        for field in ('schemaVersion', 'coreAssemblySha256', 'oracleAssemblySha256', 'protocol', 'suite'):
            require(report['summary'].get(field) == first['summary'].get(field), '不能混合不同共同 harness/协议/套件: ' + field)
        require(report['runnerIdentity']['sha256'] == first['runnerIdentity']['sha256'], '共同 Runner 实际 Compile/源/csproj/编译属性身份不同')
        require(report['cases'].keys() == first['cases'].keys(), '案例集合不同；请对全部候选运行相同套件/过滤条件')
        for name, case in report['cases'].items():
            require(case['InputSha256'] == first['cases'][name]['InputSha256'], '同名案例输入不一致: ' + name)


def make_comparison(reports, diagrams):
    validate_common_harness(reports)
    first = reports[0]
    names = list(first['cases'])
    require(all(name in names for name in diagrams), '--diagram 案例不在报告中')
    result = {'schemaVersion': 1, 'generatedBy': 'NavigationLab/report.py', 'finiteCorpusOnly': True,
              'multipleAlgorithmsCompared': len(reports) > 1, 'performanceRankingPerformed': False,
              'scope': '有限共同输入的冻结 .NET 模型证据汇总；不代表全面普适、商业游戏原版源码、Unity/IL2CPP/设备验收。',
              'commonHarness': {**{key: first['summary'][key] for key in ('coreAssemblySha256', 'oracleAssemblySha256', 'protocol', 'suite')},
                                'runnerCompileIdentitySha256': first['runnerIdentity']['sha256'],
                                'runnerCompileIdentityScheme': RUNNER_IDENTITY_SCHEME,
                                'allRawRunnerDllHashesIdentical': len({report['summary']['runnerAssemblySha256'] for report in reports}) == 1,
                                'repositoryRootException': first['runnerIdentity']['normalizationScope']},
              'reports': [], 'cases': [], 'diagrams': []}
    text = ['# 导航候选对照', '', result['scope'], '',
            '证据核对：Core/Oracle DLL 字节一致；Runner 实际编译源、csproj、清单记录的编译属性与 SDK 记录的规范身份一致；协议、套件、案例集合及同名输入一致；每份原始 DLL 均独立与冻结构建绑定，所有 PASS 已完成新实例回放。', '',
            'Runner DLL 的原始 SHA 可以不同：生成 AssemblyInfo 的唯一 RepositoryRoot 属性指向各自 snapshot，规范化仅限该精确属性，其余源码字节不变。该根路径会被运行时读取，故还必须验证各冻结源与逐例输入一致；这不等同于 DLL 字节相同或无条件语义等价。', '',
            '只列 PASS / FAIL / UNSUPPORTED；InvalidInput 或证据不完整会拒绝生成。未进行性能排名。', '']
    if len(reports) == 1:
        text += ['当前只有一份报告：这是单报告汇总/工具检验，不能据此得出算法间比较结论。', '']
    text += ['| 标签 / 算法 | PASS | FAIL | UNSUPPORTED | 隔离及暂停 |', '| --- | ---: | ---: | ---: | --- |']
    for report in reports:
        summary = report['summary']
        counts = report['counts']
        text.append(f'| {md(report["label"])} / {md(summary["algorithm"])} | {counts["Passed"]} | {counts["Failed"]} | {counts["Unsupported"]} | {"通过" if summary["isolationAndPauseVerified"] else "失败"} |')
        result['reports'].append({'label': report['label'], 'algorithm': summary['algorithm'], 'description': summary.get('description'),
                                  'factoryType': summary['actualFactoryType'], 'candidateAssemblySha256': summary['candidateAssemblySha256'],
                                  'runnerAssemblySha256': summary['runnerAssemblySha256'], 'runnerCompileIdentity': report['runnerIdentity'],
                                  'sourceSummary': report['path'], 'sourceSummarySha256': report['sha256'], 'utc': summary.get('utc'),
                                  'buildManifestSha256': report['manifestSha256'], 'snapshotIdentitySha256': report['manifest'].get('snapshotIdentitySha256'),
                                  'originCandidate': report['manifest'].get('originCandidate'), 'originHarness': report['manifest'].get('originHarness'),
                                  'counts': counts, 'isolationAndPauseVerified': summary['isolationAndPauseVerified'],
                                  'isolationError': summary.get('isolationError'), 'timingScope': summary.get('timingScope')})
    text += ['', '共同 Runner 编译输入身份：`' + first['runnerIdentity']['sha256'] + '`。各原始 Runner DLL hash 与根路径例外明细见 comparison.json。', '', '每格格式为“状态 · 活体数 · 待入队数 · 攻击数”。容量/不可达场景的 PASS 按其原始验收条件判断，并不表示所有请求都已入场。', '',
             '| 案例 | ' + ' | '.join(md(report['label']) for report in reports) + ' |',
             '| --- | ' + ' | '.join('---' for _ in reports) + ' |']
    outputs = {}
    for name in names:
        row = {'name': name, 'inputSha256': first['cases'][name]['InputSha256'], 'input': 'inputs/' + name + '.json', 'results': {}}
        cells = []
        for report in reports:
            case = report['cases'][name]
            row['results'][report['label']] = case
            status = {'Passed': 'PASS', 'Failed': 'FAIL', 'Unsupported': 'UNSUPPORTED'}[case['Status']]
            cells.append(f'{status} · 活{case.get("Alive", 0)} · 队{case.get("Pending", 0)} · 攻{case.get("Attacking", 0)}')
        result['cases'].append(row)
        text.append('| ' + md(name) + ' | ' + ' | '.join(cells) + ' |')
        outputs['inputs/' + name + '.json'] = first['evidence'][name][2]
    text += ['', '## 失败与不支持', '']
    failures = []
    for report in reports:
        for name, case in report['cases'].items():
            if case['Status'] != 'Passed':
                failures.append(f'- **{md(report["label"])} / {md(name)} / {md(case["Status"])}**：{md(case.get("Error") or "见原报告") }')
    text += failures or ['无。']
    if diagrams:
        text += ['', '## 最终态图', '', '图按实际出生半径绘制身体，只应用 Commands 中已执行的环境变更。图不替代连续碰撞、守恒和攻击条件验收。', '']
    for name in diagrams:
        for report in reports:
            definition, full, _ = report['evidence'][name]
            filename = 'diagrams/' + slug(report['label']) + '--' + name + '.svg'
            try:
                svg, applied = svg_diagram(report['label'], name, definition, full)
                outputs[filename] = svg.encode('utf-8')
                result['diagrams'].append({'label': report['label'], 'case': name, 'file': filename, 'appliedWorldEventIndices': applied, 'sha256': sha(outputs[filename])})
                text.append(f'- [{md(report["label"])} / {md(name)}]({filename})')
            except (EvidenceError, ValueError, KeyError, TypeError, IndexError) as error:
                result['diagrams'].append({'label': report['label'], 'case': name, 'error': str(error)})
                text.append(f'- {md(report["label"])} / {md(name)}：无法可靠绘图，{md(error)}。保留原失败输出，未替换位置或猜测半径。')
    text += ['', '## 来源与复现', '', 'comparison.json 保留逐例计数、完整最终态、输入/原摘要/完整案例/trace hash、factory 身份及候选构建来源。inputs/ 是核验后的原始输入副本。', '',
             '原始大体积 trace 不复制。复现仍需原报告目录、对应冻结构建清单及其源文件/DLL；下列 SHA 可核对本次汇总使用的确切摘要。计时含宿主机调度影响，仅作诊断。', '']
    for report in reports:
        text += [f'- **{md(report["label"])}**：`{md(report["path"])}`；SHA-256 `{report["sha256"]}`；factory `{md(report["summary"]["actualFactoryType"])}`。']
    outputs['README.md'] = ('\n'.join(text) + '\n').encode('utf-8')
    outputs['comparison.json'] = (json.dumps(result, ensure_ascii=False, indent=2, allow_nan=False) + '\n').encode('utf-8')
    return outputs


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', action='append', required=True, metavar='LABEL=/abs/summary.json')
    parser.add_argument('--output', type=Path, required=True, help='New or empty output directory; source reports are never changed.')
    parser.add_argument('--diagram', action='append', default=[], metavar='CASE', help='Optional case name; may repeat.')
    args = parser.parse_args()
    try:
        reports = []
        labels = set()
        for value in args.report:
            label, separator, filename = value.partition('=')
            require(separator and label.strip() == label and 0 < len(label) <= 80 and '\n' not in label and '\r' not in label, '非法 --report；使用 LABEL=/abs/summary.json')
            require(label not in labels, '重复报告标签: ' + label)
            labels.add(label)
            reports.append(load_report(label, Path(filename)))
        output = args.output.resolve()
        require(not output.exists() or output.is_dir() and not any(output.iterdir()), '输出目录必须为新目录或空目录，避免复用旧成功产物')
        outputs = make_comparison(reports, list(dict.fromkeys(args.diagram)))
        output.mkdir(parents=True, exist_ok=True)
        for relative, data in outputs.items():
            target = output / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
        print('已核验并汇总 ' + str(len(reports)) + ' 份报告、' + str(len(reports[0]['cases'])) + ' 个共同案例: ' + str(output))
        return 0
    except (EvidenceError, OSError, ValueError, KeyError, TypeError, IndexError) as error:
        print('拒绝生成比较报告: ' + str(error), file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
