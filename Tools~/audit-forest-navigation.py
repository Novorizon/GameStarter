#!/usr/bin/env python3
"""Generate a logical-terrain overlay without modifying the original game image."""

import argparse
import hashlib
import json
import math
import os
from pathlib import Path

import openpyxl

os.environ.setdefault("MPLCONFIGDIR", "/tmp/gamestarter-navigation-matplotlib")

W, H, C = 44, 30, 500
OX, OY = -11000, -7560
TX, TY, R = 7553, -3527, 1100
root = Path(__file__).resolve().parents[1]

arguments = argparse.ArgumentParser(
    description="Audit the authored forest navigation map and generate a diagnostic overlay."
)
arguments.add_argument(
    "--write-layout",
    action="store_true",
    help=(
        "Explicitly regenerate the reference layout from the documented "
        "footprint/shoulder rules; overwrites hand edits."
    ),
)
args = arguments.parse_args()


def read(name):
    workbook = openpyxl.load_workbook(
        root / f"Config/Luban/Data/{name}.xlsx", data_only=True
    )
    return [row[1:] for row in workbook.active.values if row[0] is None]


routes = [(row[-2], row[-1]) for row in read("tdroutepoint")]
towers = [(row[0], row[-2], row[-1]) for row in read("tdbuildslot")]


def world(point):
    return (
        (point[0] / 1672 - 0.5) * 22000,
        (0.5 - point[1] / 941) * 15120,
    )


def polygon(point, points):
    x, y = point
    inside = False
    for (ax, ay), (bx, by) in zip(points, points[1:] + points[:1]):
        if (ay > y) != (by > y) and x < (bx - ax) * (y - ay) / (by - ay) + ax:
            inside = not inside
    return inside


north = [
    world(point)
    for point in [
        (594, 301),
        (747, 269),
        (841, 312),
        (872, 344),
        (852, 380),
        (821, 431),
        (680, 431),
        (605, 400),
        (573, 354),
    ]
]
south = [
    world(point)
    for point in [
        (615, 512),
        (737, 514),
        (822, 516),
        (848, 543),
        (817, 613),
        (702, 658),
        (601, 635),
        (568, 594),
        (580, 549),
    ]
]


def distance(point, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    t = max(
        0,
        min(
            1,
            ((point[0] - a[0]) * dx + (point[1] - a[1]) * dy)
            / (dx * dx + dy * dy),
        ),
    )
    return math.hypot(point[0] - a[0] - t * dx, point[1] - a[1] - t * dy)


# Explicit, conservatively authored obstacle samples; never classify pixels at runtime.
decor = [
    (*world((1540, 790)), 450),
    (*world((1530, 535)), 475),
    (*world((1410, 825)), 425),
    (*world((1155, 802)), 650),
]
cells = []
for y in range(H):
    row = []
    for x in range(W):
        point = (OX + (x + 0.5) * C, OY + (y + 0.5) * C)
        px, py = point
        symbol = "#"
        if -6500 <= px <= 2500 and 4400 <= py <= 7000:
            symbol = "^"
        if polygon(point, north) or polygon(point, south):
            symbol = "~"
        if any(
            math.hypot(px - dx, py - dy) <= radius
            for dx, dy, radius in decor
        ):
            symbol = "*"
        onroad = min(
            distance(point, a, b) for a, b in zip(routes, routes[1:])
        ) <= 650
        bridge = -2350 <= px <= 150 and -600 <= py <= 600
        if onroad and (symbol not in "~*^" or bridge):
            symbol = "."
        if (
            2500 <= px <= 10000
            and -6000 <= py <= -1250
            and symbol not in "~*^"
        ):
            symbol = "a"
            # Reserve outer circulation and the entrance spoke for passage.
            # Ground boundaries and decoration narrow parts of the reference bands.
            if px < 3500 or px > 9500 or py < -5500 or py > -2250:
                symbol = "+"
            if px <= 6500 and abs(py + 3527) <= 650:
                symbol = "+"
        if math.hypot(px - TX, py - TY) <= R:
            symbol = "B"
        for tid, tx, ty in towers:
            if abs(px - tx) < 750 and abs(py - ty) < 750:
                symbol = "T"
        row.append(symbol)
    cells.append(row)

sx = int((routes[0][0] - OX) // C)
sy = int((routes[0][1] - OY) // C)
assert cells[sy][sx] == ".", (sx, sy, cells[sy][sx])
cells[sy][sx] = "S"
header = """; Forest Outpost navigation: explicit gameplay grid, not automatic image extraction.
; North-to-south rows. Origin is the southwest grid boundary; units are integer millimetres.
; . road/shoulder, + circulation-only, a holding/attack candidates, B target terrain, S spawn gate.
; # closed grass/border, ~ water, ^ raised terrain without entry, * decoration, T reserved tower footprint.
; The image is unchanged. Logical road shoulders and the defended-base apron are authoritative.
NAVGRID 1
size 44 30
cell 500
origin -11000 -7560
target 7553 -3527 1100
rows north-to-south
"""
path = root / "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"
if args.write_layout:
    path.write_text(
        header + "\n".join("".join(row) for row in reversed(cells)) + "\n"
    )
else:
    lines = [
        line.strip()
        for line in path.read_text().splitlines()
        if line.strip() and not line.strip().startswith(";")
    ]
    assert lines[:6] == [
        "NAVGRID 1",
        "size 44 30",
        "cell 500",
        "origin -11000 -7560",
        "target 7553 -3527 1100",
        "rows north-to-south",
    ], "Unexpected forest layout metadata"
    assert len(lines) == H + 6 and all(
        len(row) == W for row in lines[6:]
    ), "Malformed terrain rows"
    cells = [list(row) for row in reversed(lines[6:])]
    assert all(
        symbol in ".+aBS#~^*T" for row in cells for symbol in row
    ), "Unknown terrain symbol"
    assert cells[sy][sx] == "S", "The authored route entry must be marked S"


def walk(x, y):
    return (
        0 <= x < W
        and 0 <= y < H
        and cells[y][x] in ".+aBS"
        and math.hypot(
            OX + (x + 0.5) * C - TX, OY + (y + 0.5) * C - TY
        ) >= R + 250
    )


seen = {(sx, sy)}
todo = [(sx, sy)]
steps = {(sx, sy): 0}
for x, y in todo:
    for nxt in [(x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)]:
        if nxt not in seen and walk(*nxt):
            a = (OX + (x + 0.5) * C, OY + (y + 0.5) * C)
            b = (OX + (nxt[0] + 0.5) * C, OY + (nxt[1] + 0.5) * C)
            if distance((TX, TY), a, b) >= R + 250:
                seen.add(nxt)
                todo.append(nxt)
                steps[nxt] = steps[(x, y)] + 1

for tid, tx, ty in towers:
    assert cells[int((ty - OY) // C)][int((tx - OX) // C)] == "T", (
        "Tower footprint not reserved", tid
    )
for px, py in routes[:-1]:
    cell = (int((px - OX) // C), int((py - OY) // C))
    assert cell in seen, ("route disconnected", px, py, cell)
holding = [
    (x, y)
    for y in range(H)
    for x in range(W)
    if cells[y][x] == "a" and walk(x, y)
]
assert all(cell in seen for cell in holding), [
    cell for cell in holding if cell not in seen
]

report = {
    "width": W,
    "height": H,
    "cellSizeMm": C,
    "origin": [OX, OY],
    "baseCenter": [TX, TY],
    "targetRadiusMm": R,
    "walkableCells": sum(cell in ".+aBS" for row in cells for cell in row),
    "reachableForRadius250": len(seen),
    "holdingCandidatesForRadius250": len(holding),
    "oldRouteWaypointsVerified": len(routes) - 1,
    "towerFootprintsReserved": len(towers),
    "layoutSource": "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes",
    "logicalMapSha256": hashlib.sha256(path.read_bytes()).hexdigest(),
    "visualMapSha256": hashlib.sha256(
        (root / "Assets/GameAssets/TowerDefense/Art/Map.png").read_bytes()
    ).hexdigest(),
    "oldPolylineDistanceMm": round(
        sum(math.dist(a, b) for a, b in zip(routes, routes[1:]))
    ),
    "shortestApproachToCandidateMm": min(steps[cell] for cell in holding) * C,
    "note": (
        "Capacity is candidate-cell count, not proof all can be occupied "
        "simultaneously without cutting access. Crowd tests determine admissible capacity."
    ),
}
(root / "Documentation/TowerDefense/Navigation/forest-navigation-map-audit.json").write_text(
    json.dumps(report, indent=2) + "\n"
)
print(report)

# Diagnostic plot, not a modified game image.
import matplotlib

matplotlib.use("Agg")
import matplotlib.image as mpimg
import matplotlib.patches as patches
import matplotlib.pyplot as plt

fig, ax = plt.subplots(figsize=(16, 10))
ax.imshow(
    mpimg.imread(root / "Assets/GameAssets/TowerDefense/Art/Map.png"),
    extent=[-11000, 11000, -7560, 7560],
    alpha=0.65,
)
colors = {
    ".": ("#e5bf56", 0.45),
    "+": ("#15cfe4", 0.55),
    "a": ("#57d664", 0.50),
    "B": ("#fa5663", 0.55),
    "S": ("#ffffff", 0.85),
    "~": ("#175ea8", 0.45),
    "^": ("#79447e", 0.40),
    "T": ("#ee8615", 0.65),
    "*": ("#111111", 0.6),
    "#": ("#454545", 0.10),
}
for y in range(H):
    for x in range(W):
        color, alpha = colors[cells[y][x]]
        ax.add_patch(
            patches.Rectangle(
                (OX + x * C, OY + y * C),
                C,
                C,
                facecolor=color,
                alpha=alpha,
                edgecolor="white",
                linewidth=0.2,
            )
        )
ax.add_patch(
    patches.Circle((TX, TY), R, fill=False, color="#e31a1c", linewidth=2)
)
ax.plot(
    [point[0] for point in routes],
    [point[1] for point in routes],
    "w--",
    linewidth=1,
    label="Existing authored route",
)
for i, point in enumerate(routes):
    ax.text(
        *point,
        str(i),
        fontsize=7,
        color="black",
        bbox=dict(facecolor="white", alpha=0.7, pad=0.3),
    )
for i, x, y in towers:
    ax.text(x, y, "T" + str(i), ha="center", color="black", fontsize=8)
labels = [
    (".", "Road + shoulder"),
    ("+", "Circulation: no standing goals"),
    ("a", "Holding / attack candidate area"),
    ("B", "Base target exclusion"),
    ("T", "Reserved build footprint"),
    ("~", "Water"),
    ("^", "Raised terrain"),
    ("*", "Decoration"),
    ("#", "Closed grass / border"),
]
ax.legend(
    handles=[patches.Patch(color=colors[key][0], label=value) for key, value in labels],
    loc="upper center",
    bbox_to_anchor=(0.5, -0.06),
    ncol=3,
    fontsize=10,
)
ax.set_title(
    "Forest navigation audit - explicit logical terrain over unchanged artwork\n"
    "500 mm cells; candidate goals do not prove simultaneous crowd capacity"
)
ax.set_xlabel("Logical X (mm)")
ax.set_ylabel("Logical Y (mm)")
ax.set_xlim(-11000, 11000)
ax.set_ylim(-7560, 7560)
ax.set_aspect("equal")
fig.tight_layout()
fig.savefig(
    root / "Documentation/TowerDefense/Navigation/forest-navigation-overlay.png",
    dpi=140,
    bbox_inches="tight",
)
