#!/usr/bin/env python3
import argparse
from pathlib import Path
import zipfile
from verify import ROOT, validate_project

parser = argparse.ArgumentParser()
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
validate_project()
args.output.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(args.output, 'w', zipfile.ZIP_DEFLATED) as zipped:
    for file in sorted(ROOT.rglob('*')):
        if not file.is_file() or any(p in {'.git','bin','obj','__pycache__','Library','Temp','Logs','UserSettings','.github'} for p in file.relative_to(ROOT).parts): continue
        info = zipfile.ZipInfo('GameStarter/' + file.relative_to(ROOT).as_posix(), (2026,11,5,0,0,0))
        info.compress_type = zipfile.ZIP_DEFLATED
        info.external_attr = 0o100644 << 16
        zipped.writestr(info, file.read_bytes())
print(args.output)
