"""Run after scripts/Build-Packages.ps1. Uses only the Python standard library."""
import base64
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile

root = Path(__file__).resolve().parents[1]
chromium = json.loads((root/'extension/manifest.json').read_text(encoding='utf-8'))
firefox = json.loads((root/'extension-firefox/manifest.json').read_text(encoding='utf-8'))
version = chromium['version']
assert version == firefox['version'] == '1.0.0'
assert chromium['name'] == firefox['name'] == 'ClipFlow • YouTube MP3 / MP4'
digest = hashlib.sha256(base64.b64decode(chromium['key'])).hexdigest()[:32]
extension_id = ''.join(chr(ord('a')+int(c,16)) for c in digest)
assert extension_id == (root/'native/extension-id.txt').read_text().strip() == 'ohfplmcgkgmiaaninbjfeamfijgfaihh'
gecko = firefox['browser_specific_settings']['gecko']
assert gecko['id'] == (root/'native/firefox-extension-id.txt').read_text().strip() == 'diskora@diskora.local'
assert gecko['strict_min_version'] == '140.0'
assert gecko['data_collection_permissions']['required'] == ['browsingActivity','websiteContent']
assert 'key' not in firefox and 'minimum_chrome_version' not in firefox
assert 'service_worker' not in firefox['background']
assert firefox['background'] == {'scripts':[chromium['background']['service_worker']],'type':'module'}
assert chromium['permissions'] == firefox['permissions'] == ['activeTab','nativeMessaging','storage']
assert not chromium.get('host_permissions') and not firefox.get('host_permissions')
expected = {str(p.relative_to(root/'extension')).replace('\\','/') for p in (root/'extension').rglob('*') if p.is_file()}
for rel in expected - {'manifest.json'}:
    assert (root/'extension'/rel).read_bytes() == (root/'extension-firefox'/rel).read_bytes(), rel
for folder,name in [('extension',f'ClipFlow-Chromium-{version}.zip'),('extension-firefox',f'ClipFlow-Firefox-{version}-unsigned.zip')]:
    with ZipFile(root/'packages'/name) as z:
        assert set(z.namelist()) == expected
        for item in z.namelist():
            assert z.read(item) == (root/folder/item).read_bytes()
        assert all('fixture' not in f and '/qa.' not in f for f in z.namelist())
with ZipFile(root/'packages'/f'ClipFlow-Windows-{version}.zip') as z:
    assert {'ClipFlow/KUR.cmd','ClipFlow/Kur.ps1','ClipFlow/scripts/NativeHosts.ps1','ClipFlow/extension/manifest.json','ClipFlow/extension-firefox/manifest.json'} <= set(z.namelist())
    assert {name for name in z.namelist() if name.lower().endswith('.md')} == {'ClipFlow/README.md'}
    assert {name for name in z.namelist() if name.startswith('ClipFlow/scripts/')} == {'ClipFlow/scripts/NativeHosts.ps1'}
    for item in z.namelist():
        assert item.startswith('ClipFlow/') and '/node_modules/' not in item
        assert '/packages/' not in item and '/tests/' not in item
        assert not Path(item).name.startswith('.git')
        assert z.read(item) == (root/item.removeprefix('ClipFlow/')).read_bytes()
assert {p.name for p in (root/'packages').iterdir()} == {
    f'ClipFlow-Windows-{version}.zip', f'ClipFlow-Chromium-{version}.zip', f'ClipFlow-Firefox-{version}-unsigned.zip'
}
print('PASS Stable browser identities, shared assets and minimal, complete installation packages.')
