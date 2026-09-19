"""Exercises the native download-completion path with a recording Soundpad stub."""
import argparse,json,os,queue,shutil,struct,subprocess,threading
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--work',required=True);args=p.parse_args()
package=Path(__file__).resolve().parents[1]
root=Path(args.work).resolve()/'soundpad-consent-test';(root/'bin').mkdir(parents=True,exist_ok=True)
compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
common=[str(compiler),'/nologo','/target:exe','/platform:x64','/reference:System.Web.Extensions.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll']
subprocess.run(common+[f'/out:{root/"AkisHost.exe"}',str(package/'native/Host.cs'),str(package/'tests/FakeSoundpad.cs')],check=True)
subprocess.run(common+[f'/out:{root/"bin/yt-dlp.exe"}',str(package/'tests/FakeDownloader.cs')],check=True)
shutil.copyfile(package/'native/extension-id.txt',root/'extension-id.txt')
for name in ('ffmpeg.exe','ffprobe.exe','deno.exe','finish-test.txt'):(root/'bin'/name).touch()
(root/'settings.json').write_text(json.dumps({'downloadDirectory':str(root/'downloads')}),encoding='utf8')
for name in ('import-calls.txt','import-error.txt'):(root/name).unlink(missing_ok=True)
host=subprocess.Popen([str(root/'AkisHost.exe'),'chrome-extension://'+(root/'extension-id.txt').read_text().strip()+'/'],stdin=subprocess.PIPE,stdout=subprocess.PIPE)
messages=queue.Queue()
def read():
    while header:=host.stdout.read(4):
        n,=struct.unpack('<I',header);messages.put(json.loads(host.stdout.read(n)))
threading.Thread(target=read,daemon=True).start()
def request(id,action,**values):
    data=json.dumps(dict(id=id,action=action,**values)).encode();host.stdin.write(struct.pack('<I',len(data))+data);host.stdin.flush()
def response(id):
    while True:
        m=messages.get(timeout=15)
        if m.get('id')==id:return m
def done():
    while True:
        m=messages.get(timeout=15)
        if m.get('job',{}).get('status') in ('done','error','cancelled'):return m['job']
try:
    url='https://www.youtube.com/watch?v=aqz-KE-bpKQ'
    request('info','inspect',url=url);assert response('info')['ok']
    request('categories','soundpadCategories');assert response('categories')['data']['categories'][0]['path']==['Oyun','Efektler']
    for mode,extra in [('mp3',{}),('mp3',{'soundpadAuto':False}),('mp4',{'soundpadAuto':True}),('mp3',{'soundpadAuto':'true'})]:
        request('download','download',url=url,mode=mode,quality='128' if mode=='mp3' else '299',**extra)
        assert response('download')['ok'];job=done();assert job['status']=='done' and 'soundpad' not in job
        assert not (root/'import-calls.txt').exists()
    print('PASS Missing/off/non-boolean consent and MP4 make zero Soundpad calls')
    for invalid in [None,[], 'Efektler', [1], [''], ['Oyun']*33]:
        request('invalid','download',url=url,mode='mp3',quality='128',soundpadAuto=True,soundpadCategory=invalid)
        assert not response('invalid')['ok']
    assert not (root/'import-calls.txt').exists()
    print('PASS Native category list returned; malformed or missing selections rejected before download')
    request('download','download',url=url,mode='mp3',quality='128',soundpadAuto=True,soundpadCategory=['Oyun','Efektler'])
    assert response('download')['ok'];job=done();assert job['soundpad']['status']=='added'
    assert len((root/'import-calls.txt').read_text().splitlines())==1
    assert (root/'import-calls.txt').read_text().strip().endswith('|Oyun / Efektler')
    print('PASS Explicit MP3 opt-in imports once, after file completion')
    (root/'import-error.txt').touch()
    request('download','download',url=url,mode='mp3',quality='128',soundpadAuto=True,soundpadCategory=['Oyun','Efektler'])
    assert response('download')['ok'];job=done();assert job['status']=='done' and job['soundpad']['status']=='error'
    assert (root/'downloads/Consent test.mp3').exists()
    print('PASS Soundpad failure preserves downloaded MP3 and reports separate error')
finally:
    host.stdin.close();host.wait(timeout=10)
