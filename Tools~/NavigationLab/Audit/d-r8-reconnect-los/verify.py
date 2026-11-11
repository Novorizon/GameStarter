import gzip
import hashlib
import json
import math
from pathlib import Path

root = Path('/workspace/navigation-comparison')
source = root / 'results/d-v2-stress'
report_path = source / 'open-arena.json'
input_path = source / 'inputs/open-arena.json'
trace_path = source / 'traces/open-arena.jsonl.gz'
report = json.loads(report_path.read_bytes())
original = json.loads(input_path.read_bytes())
world = original['world']
profiles = {request['id']: request for request in original['requests']}
bodies = []
for item in report['FinalState'].split('|'):
    identity, coordinate, state, goal, target = item.split(':')
    x, y = map(float, coordinate.split(','))
    bodies.append({'id': int(identity), 'position': [x, y], 'state': int(state), 'goal': int(goal)})
actor = next(body for body in bodies if body['id'] == 3)
a = actor['position']
cell = actor['goal']
size = world['cellSizeMm']
b = [world['origin']['x'] + (cell % world['width'] + .5) * size,
     world['origin']['y'] + (cell // world['width'] + .5) * size]

def segment_distance(point, start, end):
    vx, vy = end[0] - start[0], end[1] - start[1]
    squared = vx * vx + vy * vy
    fraction = max(0, min(1, ((point[0] - start[0]) * vx + (point[1] - start[1]) * vy) / squared)) if squared else 0
    return math.hypot(start[0] + fraction * vx - point[0], start[1] + fraction * vy - point[1])

radius = profiles[3]['radiusMm']
checks = [{'kind': 'body', 'id': other['id'],
           'gapMm': segment_distance(other['position'], a, b) - radius - profiles[other['id']]['radiusMm']}
          for other in bodies if other['id'] != 3]
checks += [{'kind': 'base', 'id': target['id'],
            'gapMm': segment_distance([target['x'], target['y']], a, b) - radius - target['radiusMm']}
           for target in world['targets']]
left, bottom = world['origin']['x'], world['origin']['y']
checks.append({'kind': 'world-boundary', 'gapMm': min(min(a[0], b[0]) - left, min(a[1], b[1]) - bottom,
               left + world['width'] * size - max(a[0], b[0]), bottom + world['height'] * size - max(a[1], b[1])) - radius})
# Every closed rectangle lies outside the entire swept disc AABB, an exact conservative disjointness certificate.
nearby = []
for cell, walkable in enumerate(world['walkable']):
    if walkable == '1':
        continue
    x, y = left + cell % world['width'] * size, bottom + cell // world['width'] * size
    disjoint = x + size < min(a[0], b[0]) - radius or x > max(a[0], b[0]) + radius or y + size < min(a[1], b[1]) - radius or y > max(a[1], b[1]) + radius
    if not disjoint:
        nearby.append(cell)
assert not nearby, nearby
assert min(check['gapMm'] for check in checks) >= 0


output = {'scope': 'Read-only R8 actor3 reconnection guard diagnostic; original endpoint, original goal and all other bodies fixed, no candidate pass claimed.',
          'actorId': 3, 'from': a, 'toUnchangedGoal': b,
          'minimumBodyGapMm': min(check['gapMm'] for check in checks if check['kind'] == 'body'),
          'minimumBaseGapMm': min(check['gapMm'] for check in checks if check['kind'] == 'base'),
          'continuousChecks': checks, 'closedCellsDisjointFromSweptDiscAabb': True,
          'microstepsOf5MsAtOriginalSpeedLimit': math.ceil(math.dist(a,b) / (profiles[3]['speedMmPerTick']/10))}
Path(__file__).with_name('geometry.json').write_text(json.dumps(output, indent=2)+'\n')
print(json.dumps({key:value for key,value in output.items() if key!='continuousChecks'}, indent=2))
subject = next(body for body in bodies if body['id'] == 1)
path = [subject['position'], [21900, subject['position'][1]], [22000, 16400], [22500, 16600], [24500, 17500]]
escape_checks = []
for start, end in zip(path, path[1:]):
    gaps = [{'kind': 'body', 'id': other['id'], 'gapMm': segment_distance(other['position'], start, end) - 500}
            for other in bodies if other['id'] != 1]
    gaps += [{'kind': 'base', 'id': target['id'], 'gapMm': segment_distance([target['x'], target['y']], start, end) - target['radiusMm'] - 250}
             for target in world['targets']]
    assert min(gap['gapMm'] for gap in gaps) > 9
    escape_checks.append({'from': start, 'to': end, 'minimum': min(gaps, key=lambda gap: gap['gapMm']),
                          'microstepsOf5Ms': math.ceil(math.dist(start, end) / 100)})
preferred = [20500 - path[0][0], 15500 - path[0][1]]
score_upper_bound = preferred[0] / math.hypot(*preferred) + 24 * .015
assert score_upper_bound < -.2
output['actor1EscapeWitness'] = {'scope': 'All other 47 bodies fixed; unchanged goal551. Only an existence/steering-limit witness, not a candidate pass or claim that the R9 correction needs another strategy.',
                                 'segments': escape_checks, 'minimumGapMm': min(leg['minimum']['gapMm'] for leg in escape_checks),
                                 'firstEastwardStepScoreUpperBound': score_upper_bound, 'minimumAcceptedScore': -.2}
Path(__file__).with_name('geometry.json').write_text(json.dumps(output, indent=2) + '\n')
