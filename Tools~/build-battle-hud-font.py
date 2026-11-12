"""Build the Medium and Bold battle HUD subsets from the OFL Noto Sans SC variable TTF."""

import argparse
from pathlib import Path
from xml.etree import ElementTree
from zipfile import ZipFile

from fontTools import subset
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont


ROOT = Path(__file__).resolve().parents[1]


def battle_characters():
    characters = set(chr(code) for code in range(32, 256))
    scripts = ROOT / "Assets/HotUpdate/TowerDefense/Presentation"
    for source in scripts.glob("*.cs"):
        characters.update(source.read_text(encoding="utf-8-sig"))

    data = ROOT / "Config/Luban/Data"
    for name in ("tdtower", "tdenemy", "tdcard", "tdstage"):
        with ZipFile(data / (name + ".xlsx")) as workbook:
            for xml in workbook.namelist():
                if xml == "xl/sharedStrings.xml" or xml.startswith("xl/worksheets/sheet") and xml.endswith(".xml"):
                    for value in ElementTree.fromstring(workbook.read(xml)).itertext():
                        characters.update(value)
    return "".join(sorted(characters))


def write_weight(source, text, style, weight):
    with TTFont(source, recalcTimestamp=False) as font:
        if font["name"].getDebugName(16) != "Noto Sans SC" and font["name"].getDebugName(1) != "Noto Sans SC":
            raise ValueError("The source must be the Noto Sans SC variable TTF.")
        options = subset.Options()
        options.name_IDs = ["*"]
        options.name_languages = ["*"]
        cutter = subset.Subsetter(options=options)
        cutter.populate(text=text)
        cutter.subset(font)
        instantiateVariableFont(font, {"wght": weight}, inplace=True)
        family = "Battle HUD Sans"
        names = {
            1: family, 2: style, 3: f"BattleHUDSans-{style}", 4: f"{family} {style}",
            6: f"BattleHUDSans-{style}", 16: family, 17: style,
        }
        for record in font["name"].names:
            if record.nameID in names:
                record.string = names[record.nameID].encode(record.getEncoding())
        for name_id, value in names.items():
            font["name"].setName(value, name_id, 3, 1, 0x0409)
        output = ROOT / f"Assets/GameAssets/Common/Fonts/BattleHud{style}.ttf"
        font.save(output)
        print(f"{output.name}: weight {font['OS/2'].usWeightClass}, {len(font.getBestCmap())} characters")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    source = parser.parse_args().source
    text = battle_characters()
    write_weight(source, text, "Medium", 500)
    write_weight(source, text, "Bold", 700)


if __name__ == "__main__":
    main()
