"""Собирает Mine-Server-Pack-<version>.mrpack из свежих версий модов на Modrinth.

Использование: python scripts/build_mrpack.py [версия-пака] (по умолчанию 1.0.0)
"""
import json, os, sys, urllib.parse, urllib.request, zipfile

API = 'https://api.modrinth.com/v2'
UA = {'User-Agent': 'mine-server-pack-builder/1.0'}
MC_VERSION = '26.2'
FABRIC_LOADER = '0.19.3'

def get(url):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req) as r:
        return json.loads(r.read())

def latest_version(slug, loaders, fallback_no_gv=False):
    q = f'{API}/project/{slug}/version?loaders={urllib.parse.quote(json.dumps(loaders))}'
    url = q + f'&game_versions={urllib.parse.quote(json.dumps([MC_VERSION]))}'
    versions = get(url)
    if not versions and fallback_no_gv:
        versions = get(q)
    if not versions:
        raise SystemExit(f'{slug}: нет версии под MC {MC_VERSION}')
    v = versions[0]
    f = next((x for x in v['files'] if x.get('primary')), v['files'][0])
    return v, f

MODS = [
    ('fabric-api', ['fabric'], 'mods', {'client': 'required', 'server': 'required'}),
    ('sodium', ['fabric'], 'mods', {'client': 'required', 'server': 'unsupported'}),
    ('iris', ['fabric'], 'mods', {'client': 'required', 'server': 'unsupported'}),
    ('lithium', ['fabric'], 'mods', {'client': 'required', 'server': 'optional'}),
]

def main():
    pack_version = sys.argv[1] if len(sys.argv) > 1 else '1.0.0'
    files = []
    for slug, loaders, prefix, env in MODS:
        v, f = latest_version(slug, loaders)
        files.append({
            'path': f"{prefix}/{f['filename']}",
            'hashes': {'sha1': f['hashes']['sha1'], 'sha512': f['hashes']['sha512']},
            'env': env,
            'downloads': [f['url']],
            'fileSize': f['size'],
        })
        print(f"{slug:12s} {v['version_number']:24s} {f['filename']}")

    v, f = latest_version('complementary-reimagined', ['iris'], fallback_no_gv=True)
    shader_filename = f['filename']
    files.append({
        'path': f"shaderpacks/{shader_filename}",
        'hashes': {'sha1': f['hashes']['sha1'], 'sha512': f['hashes']['sha512']},
        'env': {'client': 'required', 'server': 'unsupported'},
        'downloads': [f['url']],
        'fileSize': f['size'],
    })
    print(f"{'shaders':12s} {v['version_number']:24s} {shader_filename}")

    index = {
        'formatVersion': 1,
        'game': 'minecraft',
        'versionId': pack_version,
        'name': 'Mine Server Pack',
        'summary': 'Клиентский пак для сервера 88.204.150.46 — Fabric 26.2, Sodium, Iris + шейдеры Complementary',
        'files': files,
        'dependencies': {'minecraft': MC_VERSION, 'fabric-loader': FABRIC_LOADER},
    }
    iris_props = f'enableShaders=true\nshaderPack={shader_filename}\n'

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    out = os.path.join(root, f'Mine-Server-Pack-{pack_version}.mrpack')
    with zipfile.ZipFile(out, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('modrinth.index.json', json.dumps(index, ensure_ascii=False, indent=2))
        z.writestr('overrides/config/iris.properties', iris_props)
    print('\nOK:', out, os.path.getsize(out), 'bytes')

if __name__ == '__main__':
    main()
