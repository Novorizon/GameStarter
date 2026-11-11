import json
import math
from pathlib import Path

root = Path(__file__).resolve().parent
report = json.loads((root / 'original-report.json').read_text())
actors = {}
for item in report['FinalState'].split('|'):
    identifier, point, state, goal, target = item.split(':')
    actors[int(identifier)] = tuple(map(float, point.split(',')))

# Minkowski expansion by actor46's actual radius490; others remain physically fixed.
circles = [(actors[45], 980, 'actor45'), ((4500, 4500), 1490, 'base'),
           (actors[38], 980, 'actor38'), (actors[48], 980, 'actor48')]
point = actors[46]
goal = (3500, 6500)

def inside(test):
    hit = False
    for i in range(len(circles)):
        one = circles[i][0]
        two = circles[(i + 1) % len(circles)][0]
        if (one[1] > test[1]) != (two[1] > test[1]) and test[0] < (two[0] - one[0]) * (test[1] - one[1]) / (two[1] - one[1]) + one[0]:
            hit = not hit
    return hit

edges = []
for i in range(len(circles)):
    one, r1, n1 = circles[i]
    two, r2, n2 = circles[(i + 1) % len(circles)]
    overlap = r1 + r2 - math.dist(one, two)
    assert overlap > 0
    edges.append({'first': n1, 'second': n2, 'centerDistanceMm': math.dist(one, two),
                  'expandedRadiiSumMm': r1 + r2, 'overlapDepthMm': overlap})
assert inside(point) and not inside(goal)
assert all(math.dist(point, center) >= radius for center, radius, _ in circles)
certificate = {
    'actor': 46, 'position': point, 'goal': goal, 'radiusMm': 490,
    'otherBodiesFixed': True,
    'expandedCircleBarrier': [{'center': center, 'expandedRadius': radius, 'source': name} for center, radius, name in circles],
    'adjacentCircleOverlaps': edges,
    'startInsideCenterPolygon': inside(point), 'goalOutsideCenterPolygon': not inside(goal),
    'conclusion': 'The closed polygon boundary is contained in the union of expanded forbidden disks. '
                  'Actor46 cannot reach its original Goal with all other bodies fixed; cooperative movement is necessary, independent of path graph resolution.',
    'proofScope': 'Analytical closed-barrier certificate from actual failed state; not a no-solution proof for cooperative motion.'
}
(root / 'barrier-certificate.json').write_text(json.dumps(certificate, indent=2) + '\n')
print('PASS strict expanded-disk closed barrier; actor46 inside, Goal57 outside, cooperative movement required.')
