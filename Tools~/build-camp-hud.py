"""Bake the camp's nine-slice surfaces, capsule meters and small UI glyphs."""

from pathlib import Path
import runpy

from PIL import Image, ImageDraw


shared = runpy.run_path(str(Path(__file__).with_name("build-battle-hud.py")))
surface = shared["surface"]
pill = shared["pill"]
shared_glyph = shared["glyph"]
solid = shared["solid"]
CELL = shared["CELL"]
SCALE = shared["SUPERSAMPLE"]


def camp_glyph(name):
    mask = Image.new("L", (CELL * SCALE, CELL * SCALE))
    draw = ImageDraw.Draw(mask)

    def scaled(points):
        return [(x * SCALE, y * SCALE) for x, y in points]

    def line(points, width=7):
        points = scaled(points)
        draw.line(points, fill=255, width=width * SCALE, joint="curve")
        radius = width * SCALE / 2
        for x, y in (points[0], points[-1]):
            draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=255)

    def polygon(points):
        draw.polygon(scaled(points), fill=255)

    def box(bounds, radius=0, outline=0):
        rect = tuple(value * SCALE for value in bounds)
        draw.rounded_rectangle(rect, radius * SCALE, fill=None if outline else 255,
                               outline=255 if outline else None, width=outline * SCALE)

    if name == "wood":
        line([(28, 65), (68, 39), (94, 40), (103, 56), (96, 72), (59, 97)], 8)
        box((21, 58, 62, 99), 20, 7)
        box((34, 71, 49, 86), 7, 5)
        line([(61, 66), (83, 51)], 6)
        line([(66, 79), (92, 61)], 6)
    elif name == "stone":
        line([(21, 88), (28, 62), (54, 36), (79, 33), (103, 57), (108, 83), (88, 98), (40, 98), (21, 88)], 7)
        line([(28, 62), (51, 71), (73, 58), (79, 33)], 6)
        line([(73, 58), (99, 59)], 6)
        line([(51, 71), (57, 95)], 6)
    elif name == "journal":
        box((31, 24, 99, 104), 10, 7)
        line([(45, 27), (45, 101)], 6)
        line([(59, 48), (84, 48)], 6)
        line([(59, 65), (79, 65)], 6)
    elif name == "compass":
        box((24, 24, 104, 104), 40, 7)
        polygon([(80, 43), (70, 72), (47, 85), (57, 56)])
    elif name == "lock":
        line([(46, 56), (46, 41), (53, 31), (64, 27), (75, 31), (82, 41), (82, 56)], 7)
        box((33, 53, 95, 101), 9, 7)
        line([(64, 72), (64, 83)], 6)
    elif name == "arrow":
        line([(33, 64), (94, 64)], 7)
        line([(70, 41), (94, 64), (70, 87)], 7)
    elif name == "flag":
        line([(34, 101), (34, 29)], 7)
        polygon([(39, 29), (63, 25), (96, 36), (83, 54), (94, 70), (63, 58), (39, 64)])
    elif name == "coin":
        box((27, 27, 101, 101), 37, 7)
        line([(64, 44), (64, 84)], 6)
        line([(53, 55), (77, 55)], 5)
        line([(53, 73), (77, 73)], 5)
    elif name == "check":
        line([(30, 65), (53, 88), (99, 42)], 9)
    elif name == "waves":
        for y in (41, 65, 89):
            line([(23, y), (37, y - 6), (51, y), (64, y + 6), (77, y), (91, y - 6), (105, y)], 6)
    elif name == "user":
        box((47, 24, 81, 58), 17, 7)
        line([(30, 101), (33, 86), (46, 74), (64, 70), (82, 74), (95, 86), (98, 101)], 7)
    elif name == "edit":
        line([(32, 90), (38, 68), (82, 25), (101, 44), (57, 89), (32, 96), (32, 90)], 7)
        line([(69, 37), (89, 57)], 6)
    else:
        raise ValueError(name)

    return solid("EFFCF8", mask).resize((CELL, CELL), Image.Resampling.LANCZOS)


def main():
    # Cell order is CampPiece in CampUI.cs.
    plates = [
        surface("244C4B", "112C32", "C3E5D7", "639A99", 28, halo=78, gloss=132),
        surface("214341", "132F33", "87ADA2", "466F6F", 18, halo=28, gloss=44),
        surface("315A55", "1B3B41", "B1D6C5", "659286", 17, halo=60, gloss=94),
        surface("F2CB86", "C99551", "FFF3C8", "E8BA73", 17, halo=74, gloss=146),
        pill("315A53", "1D403F", "96C5AC", "6A9A86", rim_width=1.8),
        pill("132E31", "0E252C", "638981", "426662"),
        pill("BBE6BA", "6BBB9F", "E2F6CB", "A6DDB4"),
        surface("326158", "1C4143", "F0FFEA", "C0E8B5", 18, halo=167, gloss=139, selected=True),
    ]
    icons = [shared_glyph("camp"), camp_glyph("wood"), camp_glyph("stone"), camp_glyph("journal"),
             camp_glyph("compass"), camp_glyph("lock"), camp_glyph("arrow"), shared_glyph("close"),
             camp_glyph("flag"), shared_glyph("wave"), camp_glyph("coin"), shared_glyph("insight"),
             camp_glyph("check"), camp_glyph("waves"), camp_glyph("user"), camp_glyph("edit")]
    atlas = Image.new("RGBA", (8 * CELL, 3 * CELL))
    for index, sprite in enumerate(plates + icons):
        atlas.paste(sprite, ((index % 8) * CELL, (index // 8) * CELL))
    atlas.save(shared["ROOT"] / "Assets/GameAssets/Common/UI/CampHud.png", optimize=True)


if __name__ == "__main__":
    main()
