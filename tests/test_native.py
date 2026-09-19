"""Offline integration tests. Run: python tests/test_native.py --work <scratch-directory>"""
import argparse, ctypes, json, os, queue, shutil, struct, subprocess, threading, time
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--work', required=True)
args = parser.parse_args()
package = Path(__file__).resolve().parents[1]
root = Path(args.work).resolve() / 'akis-native-test'
root.mkdir(parents=True, exist_ok=True)
(root/'bin').mkdir(exist_ok=True)
compiler = Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
for sources, target in [([package/'native/Host.cs',package/'native/Soundpad.cs'], root/'AkisHost.exe'), ([package/'tests/FakeDownloader.cs'],root/'bin/yt-dlp.exe')]:
    subprocess.run([str(compiler),'/nologo','/target:exe','/platform:x64','/reference:System.Web.Extensions.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:System.Xml.Linq.dll',f'/out:{target}',*[str(source) for source in sources]],check=True)
shutil.copyfile(package/'native/extension-id.txt',root/'extension-id.txt')
shutil.copyfile(package/'native/firefox-extension-id.txt',root/'firefox-extension-id.txt')
(root/'host-firefox.json').write_text('{}')
(root/'settings.json').write_text(json.dumps({'downloadDirectory':str(root/'downloads')}),encoding='utf8')
for name in ['ffmpeg.exe','ffprobe.exe','deno.exe']:(root/'bin'/name).touch()
origin='chrome-extension://'+(root/'extension-id.txt').read_text().strip()+'/'
bad=subprocess.run([str(root/'AkisHost.exe'),'chrome-extension://'+'a'*32+'/'],capture_output=True,timeout=5)
assert bad.returncode==2 and not bad.stdout
for argv in [[str(root/'host-firefox.json'),'evil@diskora.local'],[str(root/'other.json'),'diskora@diskora.local'],['diskora@diskora.local'],[]]:
    rejected=subprocess.run([str(root/'AkisHost.exe'),*argv],capture_output=True,timeout=5)
    assert rejected.returncode==2 and not rejected.stdout
health=json.dumps({'id':'firefox','action':'health'}).encode()
firefox=subprocess.run([str(root/'AkisHost.exe'),str(root/'host-firefox.json'),'diskora@diskora.local'],input=struct.pack('<I',len(health))+health,capture_output=True,timeout=5)
assert firefox.returncode==0
n,=struct.unpack('<I',firefox.stdout[:4]);reply=json.loads(firefox.stdout[4:4+n])
assert reply['ok'] and reply['data']['version']=='1.0.0'
assert set(reply['data']['capabilities']) >= {'soundpadCategories','soundpadImport'}
print('PASS Chromium and Firefox caller formats; wrong origins, addon IDs and manifest paths rejected')
p=subprocess.Popen([str(root/'AkisHost.exe'),origin],stdin=subprocess.PIPE,stdout=subprocess.PIPE)
q=queue.Queue()
def read():
    while header:=p.stdout.read(4):
        n,=struct.unpack('<I',header)
        q.put(json.loads(p.stdout.read(n)))
threading.Thread(target=read,daemon=True).start()
def request(id,action,**values):
    msg=json.dumps(dict(id=id,action=action,**values)).encode()
    p.stdin.write(struct.pack('<I',len(msg))+msg);p.stdin.flush()
def response(id):
    while True:
        msg=q.get(timeout=15)
        if msg.get('id')==id:return msg
try:
    for i,url in enumerate(['file:///C:/Windows/win.ini','https://youtube.com.evil.com/watch?v=aqz-KE-bpKQ','https://localhost/watch?v=aqz-KE-bpKQ','https://youtube.com/watch?v=abc','https://youtube.com:1234/watch?v=aqz-KE-bpKQ']):
        request(str(i),'inspect',url=url);assert not response(str(i))['ok']
    print('PASS Five invalid URL types rejected')
    url='https://www.youtube.com/watch?v=aqz-KE-bpKQ'
    request('info','inspect',url=url);info=response('info');assert info['ok'],info
    assert [x['height'] for x in info['data']['qualities']]==[1080,720]
    print('PASS Native framing, stdin isolation, metadata and DRM filtering')
    for mode,quality in [('exe','128'),('mp3','--exec calc'),('mp4','999999'),('mp3','999')]:
        request('bad','download',url=url,mode=mode,quality=quality);assert not response('bad')['ok']
    print('PASS Invalid formats, bitrate and argument injection rejected')
    request('start','download',url=url,mode='mp4',quality='299');assert response('start')['ok']
    request('busy','download',url=url,mode='mp3',quality='128');assert not response('busy')['ok']
    request('reset-busy','resetFolder');assert not response('reset-busy')['ok']
    request('choose-busy','chooseFolder');assert not response('choose-busy')['ok']
    while True:
        msg=q.get(timeout=15)
        if msg.get('job',{}).get('status')=='downloading':
            assert msg['job']['percent']==12.5;break
    arguments=json.loads((root/'bin/last-args.json').read_text())
    assert arguments[arguments.index('--format')+1]=='299+bestaudio[ext=m4a]/299+bestaudio'
    assert '--ignore-config' in arguments and '--no-playlist' in arguments
    print('PASS MP4 audio merge selection, progress and concurrent-job rejection')
    pid=int((root/'bin/child-pid.txt').read_text())
    request('cancel','cancel');assert response('cancel')['ok']
    while True:
        msg=q.get(timeout=15)
        if msg.get('job',{}).get('status')=='cancelled':break
    kernel=ctypes.WinDLL('kernel32',use_last_error=True)
    kernel.OpenProcess.restype=ctypes.c_void_p
    kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_ulong]
    kernel.CloseHandle.argtypes=[ctypes.c_void_p]
    handle=kernel.OpenProcess(0x100000,False,pid)
    if handle:
        assert kernel.WaitForSingleObject(handle,3000)==0
        kernel.CloseHandle(handle)
    request('health','health');assert response('health')['ok']
    assert not list((root/'downloads/.akis-temp').glob('*'))
    print('PASS Cancellation kills process tree, clears temporary files and leaves host responsive')
finally:
    p.stdin.close();p.wait(timeout=10)
subprocess.run([str(compiler),'/nologo','/target:exe','/platform:x64','/reference:System.Web.Extensions.dll',f'/reference:{root / "AkisHost.exe"}',f'/out:{root / "FolderSettingsTests.exe"}',str(package/'tests/FolderSettingsTests.cs')],check=True)
subprocess.run([str(root/'FolderSettingsTests.exe')],check=True)
subprocess.run([str(compiler),'/nologo','/target:exe','/platform:x64','/reference:System.Xml.Linq.dll',f'/reference:{root / "AkisHost.exe"}',f'/out:{root / "SoundpadTests.exe"}',str(package/'tests/SoundpadTests.cs')],check=True)
subprocess.run([str(root/'SoundpadTests.exe')],check=True)
print('ALL OFFLINE NATIVE TESTS PASSED')
