#!/usr/bin/env python3
"""Update this standalone project from a published Novorizon package catalog."""
import argparse
import json
from pathlib import Path
import urllib.request

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--ref', default='v0.1.0-preview.5')
    parser.add_argument('--catalog', type=Path, help='Optional explicit catalog file for package maintainers')
    args = parser.parse_args()
    if args.catalog:
        catalog = json.loads(args.catalog.read_text())
    else:
        url = f'https://raw.githubusercontent.com/Novorizon/UnityPackages/{args.ref}/Packages/com.novorizon.framework.all/Dependencies.json'
        with urllib.request.urlopen(url, timeout=30) as response:
            catalog = json.load(response)
    path = ROOT / 'Assets/Settings/OptionalModules.json'
    previous = {m['id']: m for m in json.loads(path.read_text())['modules']}
    modules = []
    for package in catalog['packages']:
        module = previous.get(package['name'], dict(id=package['name'], title=package['name']))
        module.update(version=package['version'], source=package['git'], dependencies=package.get('dependencies', []), embed=package.get('embed', False))
        module.pop('archive', None)
        module.pop('sha256', None)
        assert module['source'].startswith('https://github.com/')
        modules.append(module)
    path.write_text(json.dumps(dict(modules=modules), ensure_ascii=False, indent=2) + '\n')
    manifest_path = ROOT / 'Packages/manifest.json'
    manifest = json.loads(manifest_path.read_text())
    framework = next(m for m in modules if m['id'] == 'com.novorizon.framework')
    manifest['dependencies']['com.novorizon.framework'] = framework['source']
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    print('Updated standalone Git dependencies; no sibling repository paths are required.')


if __name__ == '__main__':
    main()
