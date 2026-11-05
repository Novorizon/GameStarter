#!/usr/bin/env python3
"""Export a clean Unity project, optionally bundling the pinned UPM archives."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import zipfile
from verify import ROOT, validate_project


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--archives', type=Path, help='Directory containing catalog archive files (searched recursively)')
    args = parser.parse_args()
    catalog = validate_project()
    output = args.output.resolve()
    with tempfile.TemporaryDirectory(prefix='game-starter-export-') as temp:
        project = Path(temp) / 'GameStarter'
        project.mkdir()
        for folder in ('Assets', 'Packages', 'ProjectSettings', 'Tools~', 'Verification~'):
            shutil.copytree(ROOT / folder, project / folder, ignore=shutil.ignore_patterns('bin', 'obj', '__pycache__', 'OptionalArchives~'))
        for doc in ('README.md', 'REPOSITORIES.zh-CN.md', 'VALIDATION.md', '.gitignore'):
            shutil.copy2(ROOT / doc, project / doc)
        if args.archives:
            archives = project / 'Packages/OptionalArchives~'
            archives.mkdir()
            for spec in catalog:
                matches = sorted(args.archives.rglob(spec['archive']))
                if not matches:
                    raise FileNotFoundError(spec['archive'])
                archive = next((p for p in matches if hashlib.sha256(p.read_bytes()).hexdigest() == spec['sha256']), None)
                if archive is None:
                    raise ValueError('No archive matches the expected checksum: ' + spec['archive'])
                shutil.copy2(archive, archives / archive.name)
            manifest_path = project / 'Packages/manifest.json'
            manifest = json.loads(manifest_path.read_text())
            framework = next(m for m in catalog if m['id'] == 'com.novorizon.framework')
            manifest['dependencies'][framework['id']] = 'file:OptionalArchives~/' + framework['archive']
            manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
        validate_project(project)
        # Fixed zip timestamps and ordering keep exports reproducible.
        output.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as zipped:
            for file in sorted(p for p in project.rglob('*') if p.is_file()):
                info = zipfile.ZipInfo(str(file.relative_to(project.parent)), (2026, 11, 5, 0, 0, 0))
                info.compress_type = zipfile.ZIP_DEFLATED
                info.external_attr = 0o100644 << 16
                zipped.writestr(info, file.read_bytes())
        digest = hashlib.sha256(output.read_bytes()).hexdigest()
        output.with_suffix(output.suffix + '.sha256').write_text(f'{digest}  {output.name}\n')
        print(f'Exported {output} ({output.stat().st_size} bytes)\nSHA256 {digest}')


if __name__ == '__main__':
    main()
