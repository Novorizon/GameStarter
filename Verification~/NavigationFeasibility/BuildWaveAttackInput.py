#!/usr/bin/env python3
"""Read actual stage XLSX sources and build a maximum-attack full assignment independently."""
import argparse
import hashlib
import json
from pathlib import Path

import openpyxl

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--root", type=Path, default=Path.cwd())
parser.add_argument("--output", type=Path, default=Path("Artifacts/NavigationFeasibility/wave-matching-input.json"))
args = parser.parse_args()
root = args.root.resolve()
asset = root / "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"
lines = [line.strip() for line in asset.read_text().splitlines() if line.strip() and not line.startswith(";")]
assert lines[0] == "NAVGRID 1"
width, height = map(int, lines[1].split()[1:])
cell = int(lines[2].split()[1])
origin_x, origin_y = map(int, lines[3].split()[1:])
target_x, target_y, target_radius = map(int, lines[4].split()[1:])
rows = list(reversed(lines[6:]))
assert len(rows) == height and all(len(row) == width for row in rows)


def read(name):
    values = list(openpyxl.load_workbook(root / f"Config/Luban/Data/{name}.xlsx", data_only=True).active.values)
    names = values[0][1:]
    return [dict(zip(names, row[1:])) for row in values[3:] if row[0] is None and row[1] is not None]


def walkable(x, y):
    return 0 <= x < width and 0 <= y < height and rows[y][x] in ".+aBS"


def blocked_ray(x, y):
    """Independent closed-box slab intersections, rather than calling the production supercover."""
    dx, dy = target_x - x, target_y - y
    for row in range(height):
        for column in range(width):
            if walkable(column, row):
                continue
            enter, leave = 0.0, 1.0
            axes = [
                (x, dx, origin_x + column * cell, origin_x + (column + 1) * cell),
                (y, dy, origin_y + row * cell, origin_y + (row + 1) * cell),
            ]
            for point, delta, low, high in axes:
                if delta == 0:
                    if not low <= point <= high:
                        enter = 2
                        break
                else:
                    first, second = (low - point) / delta, (high - point) / delta
                    enter = max(enter, min(first, second))
                    leave = min(leave, max(first, second))
            if enter <= leave:
                return True
    return False


enemies = {enemy["id"]: enemy for enemy in read("tdenemy")}
groups = read("tdspawn")
waves = sorted((wave for wave in read("tdwave") if wave["stageId"] == 1), key=lambda wave: wave["sequence"])
assert len(waves) == 6, "This proof targets the actual six-wave first stage."
holding = [index for index in range(width * height) if rows[index // width][index % width] == "a"]
attack_cells, valid_cells = {}, {}
for identity, enemy in enemies.items():
    assert 0 < enemy["radius"] <= cell / 2
    attack_cells[identity], valid_cells[identity] = [], []
    for index in holding:
        x = origin_x + (index % width + 0.5) * cell
        y = origin_y + (index // width + 0.5) * cell
        square = (x - target_x) ** 2 + (y - target_y) ** 2
        if square < (target_radius + enemy["radius"]) ** 2 or square > (target_radius + 4000) ** 2:
            continue
        valid_cells[identity].append(index)
        if square <= (target_radius + enemy["radius"] + enemy["attackRange"]) ** 2 and not blocked_ray(x, y):
            attack_cells[identity].append(index)


def full_assignment(units):
    """Min-cost maximum flow: assign every body, then maximize attacking bodies (gather cost = 1)."""
    goals = sorted({goal for unit in units for goal in valid_cells[unit["enemyId"]]})
    count = len(units)
    target = count + len(goals) + 1
    graph = [[] for _ in range(target + 1)]

    def edge(source, destination, capacity, cost):
        graph[source].append([destination, len(graph[destination]), capacity, cost])
        graph[destination].append([source, len(graph[source]) - 1, 0, -cost])

    for identity, unit in enumerate(units):
        edge(0, 1 + identity, 1, 0)
        for offset, goal in enumerate(goals):
            if goal in valid_cells[unit["enemyId"]]:
                cost = 0 if goal in attack_cells[unit["enemyId"]] else 1
                edge(1 + identity, 1 + count + offset, 1, cost)
    for offset in range(len(goals)):
        edge(1 + count + offset, target, 1, 0)
    total_cost = 0
    for _ in units:
        distance = [10**9] * len(graph)
        previous = [None] * len(graph)
        distance[0] = 0
        queue, queued = [0], {0}
        for source in queue:
            queued.discard(source)
            for index, (destination, reverse, capacity, cost) in enumerate(graph[source]):
                if capacity and distance[source] + cost < distance[destination]:
                    distance[destination] = distance[source] + cost
                    previous[destination] = source, index
                    if destination not in queued:
                        queue.append(destination)
                        queued.add(destination)
        assert previous[target] is not None, "Not all requested bodies can be assigned compatible final positions."
        total_cost += distance[target]
        destination = target
        while destination:
            source, index = previous[destination]
            graph[source][index][2] -= 1
            graph[destination][graph[source][index][1]][2] += 1
            destination = source
    for identity, unit in enumerate(units):
        assigned = [goals[destination - count - 1] for destination, _, capacity, _ in graph[1 + identity]
                    if 1 + count <= destination < target and capacity == 0]
        assert len(assigned) == 1
        unit["goal"] = assigned[0]
        unit["attack"] = assigned[0] in attack_cells[unit["enemyId"]]
    return count - total_cost


cases = []
for wave in waves:
    units = []
    for group in groups:
        if group["waveId"] != wave["id"]:
            continue
        enemy = enemies[group["enemyId"]]
        for index in range(group["count"]):
            units.append({
                "id": len(units) + 1,
                "enemyId": enemy["id"],
                "radius": enemy["radius"],
                "range": enemy["attackRange"],
                "speed": enemy["speedPerTick"],
                "release": group["startTick"] + index * group["intervalTicks"],
            })
    cases.append({"name": "wave-" + str(wave["id"]), "expectedMaximumAttack": full_assignment(units), "units": units})
burst = [dict(unit, release=0) for unit in max(cases, key=lambda case: len(case["units"]))["units"]]
assert len(burst) == 30
cases.append({"name": "burst-30", "expectedMaximumAttack": full_assignment(burst), "units": burst})
data = {
    "scope": "Actual XLSX stage sources; independent geometry and typed full min-cost assignment.",
    "mapSha256": hashlib.sha256(asset.read_bytes()).hexdigest(),
    "sourceSha256": {name: hashlib.sha256((root / f"Config/Luban/Data/{name}.xlsx").read_bytes()).hexdigest()
                     for name in ["tdenemy", "tdspawn", "tdwave"]},
    "enemies": list(enemies.values()),
    "groups": groups,
    "waves": waves,
    "independentAttackCells": attack_cells,
    "cases": cases,
}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n")
for case in cases:
    print(case["name"], "units=" + str(len(case["units"])), "maximumAttack=" + str(case["expectedMaximumAttack"]))
