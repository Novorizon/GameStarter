#!/usr/bin/env python3
"""Optional font maintenance: pip install fonttools openpyxl; provide licensed Noto CJK TTC."""
import argparse
from pathlib import Path
from fontTools.ttLib import TTCollection
from fontTools import subset
from openpyxl import load_workbook

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True, help='NotoSansCJK-Regular.ttc from notofonts/noto-cjk')
    args = parser.parse_args()
    fonts = TTCollection(args.source)
    font = next(item for item in fonts.fonts if item['name'].getDebugName(1) == 'Noto Sans CJK SC')
    chars = {chr(i) for a, b in [(32, 256), (0x2000, 0x2070), (0x3000, 0x3040), (0xff00, 0xfff0)] for i in range(a, b)}
    for first in range(0xa1, 0xf8):
        for second in range(0xa1, 0xff):
            try:
                chars.add(bytes([first, second]).decode('gb2312'))
            except UnicodeDecodeError:
                pass
    for file in (ROOT / 'Assets/HotUpdate/TowerDefense').rglob('*.cs'):
        chars.update(file.read_text())
    for file in (ROOT / 'Config/Luban/Data').glob('*.xlsx'):
        for row in load_workbook(file, read_only=True).active.iter_rows(values_only=True):
            for value in row:
                if isinstance(value, str):
                    chars.update(value)
    options = subset.Options()
    options.name_IDs = ['*']
    options.name_languages = ['*']
    options.name_legacy = True
    builder = subset.Subsetter(options=options)
    builder.populate(text=''.join(sorted(chars)))
    builder.subset(font)
    names = {1: 'Tower Defense UI', 2: 'Regular', 4: 'Tower Defense UI Regular', 6: 'TowerDefenseUI-Regular', 16: 'Tower Defense UI', 17: 'Regular'}
    for record in font['name'].names:
        if record.nameID in names:
            record.string = names[record.nameID].encode(record.getEncoding())
    cff = font['CFF '].cff
    cff.fontNames = ['TowerDefenseUI-Regular']
    cff.topDictIndex[0].FamilyName = 'Tower Defense UI'
    cff.topDictIndex[0].FullName = 'Tower Defense UI Regular'
    output = ROOT / 'Assets/GameAssets/TowerDefense/Fonts/TowerDefenseUI.otf'
    font.save(output)
    print('Saved', output, 'with current business text; rerun Unity Prepare TMP Font before publishing.')


if __name__ == '__main__':
    main()
