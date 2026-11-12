"""Bake the battle HUD surface and glyph artwork into the runtime texture."""

from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter


CELL = 128
SUPERSAMPLE = 3
SIZE = CELL * SUPERSAMPLE
ROOT = Path(__file__).resolve().parents[1]


def rgb(value):
    return tuple(bytes.fromhex(value))


def ramp(top, bottom, size=(SIZE, SIZE)):
    top, bottom = rgb(top), rgb(bottom)
    width, height = size
    image = Image.new("RGBA", size)
    draw = ImageDraw.Draw(image)
    for row in range(height):
        t = row / (height - 1)
        color = tuple(round(a + (b - a) * t) for a, b in zip(top, bottom))
        draw.line((0, row, width - 1, row), fill=(*color, 255))
    return image


def rounded(inset, radius):
    image = Image.new("L", (SIZE, SIZE))
    low, high = inset * SUPERSAMPLE, (CELL - inset) * SUPERSAMPLE - 1
    ImageDraw.Draw(image).rounded_rectangle((low, low, high, high), radius * SUPERSAMPLE, fill=255)
    return image


def solid(color, mask, opacity=255):
    image = Image.new("RGBA", mask.size, rgb(color))
    image.putalpha(mask.point(lambda alpha: alpha * opacity // 255))
    return image


def reflection(shape, box, softness):
    mask = Image.new("L", shape.size)
    ImageDraw.Draw(mask).ellipse(tuple(value * SUPERSAMPLE for value in box), fill=255)
    return ImageChops.multiply(mask.filter(ImageFilter.GaussianBlur(softness * SUPERSAMPLE)), shape)


def surface(top, bottom, edge_top, edge_bottom, radius, halo=90, gloss=90, selected=False):
    outer = rounded(11, radius)
    exterior = ImageChops.invert(outer)
    stroke = 3.3 if selected else 2.45
    inner = rounded(11 + stroke, radius - stroke)
    rim = ImageChops.subtract(outer, inner)
    shadow = ImageChops.multiply(outer.filter(ImageFilter.GaussianBlur(4 * SUPERSAMPLE)), exterior)
    image = solid("061522", shadow, 147)
    halo_tint = "35FFD0" if selected else edge_bottom
    spread = 7 if selected else 4.5
    halo_mask = ImageChops.multiply(outer.filter(ImageFilter.GaussianBlur(spread * SUPERSAMPLE)), exterior)
    image.alpha_composite(solid(halo_tint, halo_mask, halo))

    # Fill the panel with clean color and keep the specular line on the rim.
    body = ramp(top, bottom)
    body.putalpha(outer)
    image.alpha_composite(body)
    edge = ramp(edge_top, edge_bottom)
    edge.putalpha(rim)
    image.alpha_composite(edge)
    highlight = reflection(rim, (24, 7, 101, 15), 1.4)
    image.alpha_composite(solid("ECFCFF", highlight, gloss))
    image = image.resize((CELL, CELL), Image.Resampling.LANCZOS)
    gutter = Image.new("L", image.size)
    draw = ImageDraw.Draw(gutter)
    for inset, alpha in [(1, 85), (2, 170), (3, 255)]:
        draw.rectangle((inset, inset, CELL - inset - 1, CELL - inset - 1), fill=alpha)
    image.putalpha(ImageChops.multiply(image.getchannel("A"), gutter))
    return image


def pill(top, bottom, edge_top, edge_bottom, rim_width=1.25):
    # This 112 x 32 sprite is sliced horizontally at each 16-pixel cap.
    width, height = 112 * SUPERSAMPLE, 32 * SUPERSAMPLE

    def mask(inset):
        inset *= SUPERSAMPLE
        image = Image.new("L", (width, height))
        ImageDraw.Draw(image).rounded_rectangle((inset, inset, width - inset - 1, height - inset - 1), height / 2 - inset, fill=255)
        return image

    edge = ramp(edge_top, edge_bottom, (width, height))
    edge.putalpha(mask(0.75))
    core = ramp(top, bottom, (width, height))
    core.putalpha(mask(.75 + rim_width))
    edge.alpha_composite(core)
    tile = Image.new("RGBA", (CELL, CELL))
    tile.paste(edge.resize((112, 32), Image.Resampling.LANCZOS), (8, 48))
    return tile


def glyph(name):
    mask = Image.new("L", (SIZE, SIZE))
    draw = ImageDraw.Draw(mask)

    def scaled(points):
        return [(round(x * SUPERSAMPLE), round(y * SUPERSAMPLE)) for x, y in points]

    def line(points, width=7):
        points = scaled(points)
        weight = width * SUPERSAMPLE
        draw.line(points, fill=255, width=weight, joint="curve")
        radius = weight // 2
        for x, y in (points[0], points[-1]):
            draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=255)

    def poly(points):
        draw.polygon(scaled(points), fill=255)

    def oval(box, width=0):
        box = tuple(round(value * SUPERSAMPLE) for value in box)
        if width:
            draw.ellipse(box, outline=255, width=width * SUPERSAMPLE)
        else:
            draw.ellipse(box, fill=255)

    def bar(box, radius=5):
        draw.rounded_rectangle(tuple(value * SUPERSAMPLE for value in box), radius * SUPERSAMPLE, fill=255)

    if name == "wave":
        line([(64, 22), (98, 36), (96, 72), (84, 92), (64, 106), (43, 92), (31, 72), (30, 36), (64, 22)], 7)
        line([(49, 61), (64, 49), (79, 61)], 6)
        line([(50, 77), (64, 66), (78, 77)], 6)
    elif name == "heart":
        oval((22, 29, 66, 73))
        oval((62, 29, 106, 73))
        poly([(24, 58), (31, 75), (64, 103), (97, 75), (104, 58)])
    elif name == "battle":
        poly([(27, 24), (48, 28), (94, 74), (83, 85), (31, 40)])
        poly([(101, 24), (80, 28), (34, 74), (45, 85), (97, 40)])
        line([(31, 70), (59, 98)], 7)
        line([(97, 70), (69, 98)], 7)
        line([(38, 91), (28, 102)], 8)
        line([(90, 91), (100, 102)], 8)
    elif name == "pause":
        bar((34, 27, 54, 101), 5)
        bar((74, 27, 94, 101), 5)
    elif name == "play":
        poly([(37, 24), (101, 64), (37, 104)])
    elif name == "camp":
        line([(22, 96), (64, 28), (106, 96), (22, 96)], 8)
        line([(52, 96), (64, 72), (76, 96)], 6)
    elif name == "menu":
        for y in (38, 64, 90):
            line([(27, y), (101, y)], 9)
    elif name == "deploy":
        poly([(29, 25), (43, 25), (43, 42), (56, 42), (56, 25), (72, 25), (72, 42), (85, 42),
              (85, 25), (99, 25), (99, 51), (87, 62), (85, 87), (96, 87), (96, 104), (32, 104),
              (32, 87), (43, 87), (41, 62), (29, 51)])
    elif name == "damage":
        poly([(62, 43), (99, 25), (105, 22), (101, 58), (57, 85), (43, 69)])
        line([(37, 60), (68, 93)], 8)
        line([(49, 80), (28, 101)], 10)
    elif name == "range":
        oval((34, 34, 94, 94), 8)
        oval((58, 58, 70, 70))
        for edge in [[(64, 22), (64, 42)], [(64, 86), (64, 106)], [(22, 64), (42, 64)], [(86, 64), (106, 64)]]:
            line(edge, 7)
    elif name == "speed":
        for x in (20, 46, 72):
            line([(x, 37), (x + 28, 64), (x, 91)], 8)
    elif name == "output":
        poly([(72, 18), (37, 69), (59, 69), (52, 109), (94, 53), (71, 53)])
    elif name == "upgrade":
        poly([(64, 20), (106, 61), (82, 61), (82, 104), (46, 104), (46, 61), (22, 61)])
    elif name == "sell":
        line([(28, 48), (94, 48)], 9)
        line([(77, 29), (97, 48), (77, 66)], 8)
        line([(100, 81), (34, 81)], 9)
        line([(51, 63), (31, 81), (51, 99)], 8)
    elif name == "close":
        line([(35, 35), (93, 93)], 9)
        line([(93, 35), (35, 93)], 9)
    elif name == "insight":
        poly([(64, 19), (75, 51), (108, 64), (75, 76), (64, 109), (52, 76), (20, 64), (52, 51)])

    shadow = ImageChops.offset(mask, 0, 3 * SUPERSAMPLE).filter(ImageFilter.GaussianBlur(2 * SUPERSAMPLE))
    image = solid("0D2735", shadow, 165)
    image.alpha_composite(solid("F2FAFF", mask))
    return image.resize((CELL, CELL), Image.Resampling.LANCZOS)


def main():
    # Cell indices match BattleHudPiece in the Unity view.
    plates = [
        surface("264F66", "14333F", "CAEFFA", "71BBCD", 30, halo=118, gloss=154),              # Panel
        surface("204758", "14333E", "99CADB", "548EA1", 28, halo=58, gloss=108),               # Card
        surface("1D525A", "14424A", "EFFFF9", "A1FFDE", 28, halo=252, gloss=165, selected=True),
        surface("2A414D", "192F39", "72909A", "4A6573", 28, halo=34, gloss=49),                # Unavailable
        surface("2D5361", "193641", "A9CFDA", "698F9E", 22, halo=64, gloss=91),                # Button
        surface("267E62", "176648", "E4FFEB", "8FF5AC", 23, halo=148, gloss=185),               # Upgrade / active control
        surface("A15C69", "743744", "FFD6DC", "EF97A5", 23, halo=115, gloss=139),              # Sell
        pill("30576A", "183E4D", "9CD2E4", "659AAF", rim_width=1.8),                          # Badge
        pill("172F3E", "0D222F", "7698A9", "3D6579"),                        # Track
        pill("AAFFBF", "4FD883", "E0FFDD", "79F6AB"),                        # Fill
        surface("1E4355", "143140", "729DB0", "466F82", 22, halo=51, gloss=62),                # Inset
        pill("30695A", "1B493E", "C9FFDF", "8DE6AC", rim_width=1.8),                          # Active badge
        pill("FFC0B1", "E36870", "FFE1D0", "FF9A96"),                        # Low HP fill
    ]
    names = ["wave", "heart", "battle", "pause", "play", "camp", "menu", "deploy",
             "damage", "range", "speed", "output", "upgrade", "sell", "close", "insight"]
    atlas = Image.new("RGBA", (CELL * 8, CELL * 4))
    for index, tile in list(enumerate(plates)) + [(16 + i, glyph(name)) for i, name in enumerate(names)]:
        atlas.paste(tile, ((index % 8) * CELL, (index // 8) * CELL))
    atlas.save(ROOT / "Assets/GameAssets/Common/UI/BattleHud.png", optimize=True)


if __name__ == "__main__":
    main()
