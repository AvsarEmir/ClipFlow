import json, urllib.request, urllib.error, subprocess, socket, time, zipfile, tempfile, shutil, base64, http.server, threading, urllib.parse
from pathlib import Path
import argparse
parser=argparse.ArgumentParser()
parser.add_argument('--firefox',required=True)
parser.add_argument('--geckodriver',required=True)
parser.add_argument('--work',default='test-work/firefox')
args=parser.parse_args()
work=Path(args.work).resolve();work.mkdir(parents=True,exist_ok=True)
project=Path(__file__).resolve().parents[1]
stage=Path(tempfile.mkdtemp(prefix='firefox-extension-',dir=work))
shutil.copytree(project/'extension-firefox',stage,dirs_exist_ok=True)
addon_id='diskora@diskora.local'
uuid='80e09c31-2d8c-47bd-bdbb-4eac3b5f5214'
fixture=r'''
const a=globalThis.browser;
a.runtime.connectNative=()=>{
  let receive;
  let requests=[];
  return {onMessage:{addListener:fn=>receive=fn},onDisconnect:{addListener:()=>{}},postMessage:async m=>{
    requests.push(m); await a.storage.local.set({_testRequests:requests});
    const reply=data=>receive({id:m.id,ok:true,data});
    if(m.action==='health') reply({version:'1.0.0',capabilities:['soundpadCategories','soundpadImport'],folder:'C:\\Downloads',folderDefault:true});
    if(m.action==='inspect') reply({url:m.url,title:'Firefox test video',channel:'ClipFlow test',duration:12,qualities:[{id:'18',height:360,fps:30}]});
    if(m.action==='soundpadCategories') reply({categories:[{index:7,name:'İndirilenler',path:['İndirilenler'],label:'İndirilenler',selectable:true},{index:9,name:'Efektler',path:['Oyun','Efektler'],label:'Oyun / Efektler',selectable:true}]});
    if(m.action==='download') {
      reply({accepted:true});
      receive({event:'progress',job:{status:'starting',percent:0,title:'Firefox test video',message:'Hazırlanıyor'}});
      setTimeout(()=>receive({event:'progress',job:{status:'done',percent:100,title:'Firefox test video',message:'firefox-test.mp3',...(m.soundpadAuto?{soundpad:{status:'added',message:'Soundpad · '+m.soundpadCategory.join(' / ')+' kategorisine eklendi.'}}:{})}}),200);
    }
  }};
};
'''
(stage/'native-fixture.js').write_text(fixture+"\nsetTimeout(()=>browser.tabs.create({url:browser.runtime.getURL('popup.html')}),400);\n",encoding='utf-8')
(stage/'popup-fixture.js').write_text("browser.tabs.query=async()=>[{url:'https://www.youtube.com/watch?v=aqz-KE-bpKQ'}];",encoding='utf-8')
manifest=json.loads((stage/'manifest.json').read_text(encoding='utf-8'))
manifest['background']['scripts'].insert(0,'native-fixture.js')
(stage/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False),encoding='utf-8')
p=stage/'popup.html';p.write_text(p.read_text(encoding='utf-8').replace('<script type="module" src="popup.js">','<script src="popup-fixture.js"></script><script type="module" src="popup.js">'),encoding='utf-8')
result_event=threading.Event()
result_data={}
class Report(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        result_data.update(json.loads(urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)['result'][0]))
        self.send_response(200);self.end_headers();self.wfile.write(b'Test complete')
        result_event.set()
    def log_message(self,*args):pass
report_server=http.server.ThreadingHTTPServer(('127.0.0.1',0),Report)
threading.Thread(target=report_server.serve_forever,daemon=True).start()
qa=r"""
const check=(v,m)=>{if(!v)throw new Error(m);};
const $=s=>document.querySelector(s);
async function wait(fn){for(let i=0;i<150;i++){if(fn())return;await new Promise(r=>setTimeout(r,40));}throw new Error('UI timeout: '+document.body.innerText);}
try {
  await wait(() => $('#download') && !$('#download').disabled);
  check(document.title==='ClipFlow','wrong title');
  $('[data-mode="mp3"]').click();
  const {testPhase}=await browser.storage.local.get('testPhase');
  if(!testPhase){
    check(!$('#soundpadAuto').checked,'default consent');
    check($('#soundpad-category-panel').hidden,'category panel should be hidden');
    $('#soundpadAuto').click();
    await wait(()=>$('#soundpadCategory').value===JSON.stringify(['İndirilenler']) && !$('#soundpadCategory').disabled);
    $('#soundpadCategory').value=JSON.stringify(['Oyun','Efektler']);
    $('#soundpadCategory').dispatchEvent(new Event('change'));
    await wait(()=>!$('#soundpadCategory').disabled);
    await browser.storage.local.set({testPhase:1});
    location.reload();
  } else {
    await wait(()=>$('#soundpadCategory').value===JSON.stringify(['Oyun','Efektler']) && !$('#download').disabled);
    $('#download').click();
    await wait(()=>$('#progress-label').textContent==='Kaydedildi');
    check($('#soundpad-result').textContent.includes('Oyun / Efektler'),'wrong result category');
    let request=(await browser.storage.local.get('_testRequests'))._testRequests.filter(r=>r.action==='download').at(-1);
    check(request.soundpadAuto && JSON.stringify(request.soundpadCategory)===JSON.stringify(['Oyun','Efektler']),'wrong download category');
    $('#soundpadAuto').click();await wait(()=>!$('#soundpadAuto').disabled);
    const count=(await browser.storage.local.get('_testRequests'))._testRequests.filter(r=>r.action==='download').length;
    $('#download').click();
    await wait(()=>$('#progress-label').textContent==='Kaydedildi' && $('#soundpad-result').hidden);
    let requests=(await browser.storage.local.get('_testRequests'))._testRequests.filter(r=>r.action==='download');
    request=requests.at(-1);
    check(requests.length===count+1 && request.soundpadAuto===false && !('soundpadCategory' in request),'unchecked import');
    browser.tabs.create({url:'REPORT_URL'+encodeURIComponent(JSON.stringify({passed:true,nativePort:'test double',tests:['MV3 module background','browser namespace promises','health and inspection','checkbox and category','stored selection after reopen','selected import','unchecked download']}))});
  }
} catch(error){browser.tabs.create({url:'REPORT_URL'+encodeURIComponent(JSON.stringify({passed:false,error:String(error),body:document.body.innerText}))});}
"""
qa=qa.replace('REPORT_URL',f'http://127.0.0.1:{report_server.server_address[1]}/?result=')
(stage/'qa.js').write_text(qa,encoding='utf-8')
p=stage/'popup.html';p.write_text(p.read_text(encoding='utf-8').replace('</body>','<script type="module" src="qa.js"></script></body>'),encoding='utf-8')
xpi=stage/'test.xpi'
with zipfile.ZipFile(xpi,'w',zipfile.ZIP_DEFLATED) as z:
    for f in stage.rglob('*'):
        if f.is_file() and f!=xpi:z.write(f,f.relative_to(stage))
with socket.socket() as s:s.bind(('127.0.0.1',0));port=s.getsockname()[1]
base=f'http://127.0.0.1:{port}'
log=(work/'firefox-v2-driver.log').open('w')
driver=subprocess.Popen([str(Path(args.geckodriver).resolve()),'--host','127.0.0.1','--port',str(port)],stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
def call(method,path,data=None):
    body=None if data is None else json.dumps(data).encode()
    req=urllib.request.Request(base+path,data=body,method=method,headers={'Content-Type':'application/json'})
    try:
        with urllib.request.urlopen(req,timeout=45) as r:return json.load(r).get('value')
    except urllib.error.HTTPError as e: raise RuntimeError(e.read().decode())
sid=None
try:
    for _ in range(50):
        try:call('GET','/status');break
        except Exception:time.sleep(.1)
    session=call('POST','/session',{'capabilities':{'alwaysMatch':{'browserName':'firefox','moz:firefoxOptions':{'binary':str(Path(args.firefox).resolve()),'args':['-headless'],'prefs':{'extensions.webextensions.uuids':json.dumps({addon_id:uuid}),'browser.shell.checkDefaultBrowser':False,'datareporting.policy.dataSubmissionEnabled':False,'toolkit.telemetry.enabled':False}}}}})
    sid=session['sessionId'];prefix=f'/session/{sid}'
    assert call('POST',prefix+'/moz/addon/install',{'path':str(xpi),'temporary':True})==addon_id
    if not result_event.wait(25): raise AssertionError('Firefox test did not report; inspect work/firefox-v2-driver.log')
    assert result_data.get('passed'),result_data
    result_data['browser']=session['capabilities']['browserVersion']
    (work/'firefox-results-v1.0.0.json').write_text(json.dumps(result_data,indent=2))
    print('PASS Real Firefox',result_data['browser'],'temporary addon, UI, messaging, persistent category, opt-in/off',flush=True)
finally:
    if sid:
        try:call('DELETE',f'/session/{sid}')
        except Exception:pass
    driver.terminate();driver.wait(timeout=10);log.close();report_server.shutdown()
