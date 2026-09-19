const { chromium } = await import(process.env.CLIPFLOW_PLAYWRIGHT || 'playwright');
import path from 'node:path';
import fs from 'node:fs';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
const project = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const work = path.resolve(process.env.CLIPFLOW_TEST_WORK || 'test-work/browser');
fs.mkdirSync(work,{recursive:true});
const extension = path.join(project,'extension');
const channel=process.env.CLIPFLOW_BROWSER || 'msedge';
const video = 'https://www.youtube.com/watch?v=aqz-KE-bpKQ';
const context = await chromium.launchPersistentContext(fs.mkdtempSync(path.join(work,'browser-profile-')), {channel,headless:true,ignoreDefaultArgs:channel==='chrome'?['--disable-extensions']:[],viewport:{width:392,height:600},args:channel==='chrome'?['--enable-unsafe-extension-debugging']:[`--disable-extensions-except=${extension}`,`--load-extension=${extension}`]});
const errors = [];
context.on('page', page => page.on('pageerror', e => errors.push(e.message)));
try {
  if(channel==='chrome'){const cdp=await context.browser().newBrowserCDPSession();await cdp.send('Extensions.loadUnpacked',{path:extension});}
  const worker = context.serviceWorkers()[0] || await context.waitForEvent('serviceworker');
  const probe = {title:'Big Buck Bunny',channel:'ClipFlow test fixture',duration:596};
  await worker.evaluate(({video, probe}) => {
    globalThis.downloadRequests=[]; globalThis.nativeRequests=[];
    globalThis.soundpadFailure=false;
    globalThis.categoryOffline=false;
    globalThis.categoryRows=[{index:7,name:'İndirilenler',path:['İndirilenler'],label:'İndirilenler',selectable:true},{index:9,name:'Efektler',path:['Oyun','Efektler'],label:'Oyun / Efektler',selectable:true},{index:11,name:'Müzik',path:['Müzik'],label:'Müzik',selectable:true}];
    let listeners=[], cancelled=false, chosenFolder='C:\\Users\\Tester\\Downloads', folderChoosing=false;
    const folderInfo=()=>({version:globalThis.hostVersion || '1.0.0',capabilities:globalThis.hostVersion?[]:['soundpadCategories','soundpadImport'],folder:chosenFolder,folderDefault:chosenFolder.endsWith('Downloads'),folderChoosing});
    const videoInfo={url:video,title:probe.title,channel:probe.channel,duration:probe.duration,qualities:[{id:'315',height:2160,fps:60},{id:'299',height:1080,fps:60},{id:'136',height:720,fps:30}]};
    chrome.runtime.connectNative=()=>({
      onMessage:{addListener:fn=>listeners.push(fn)},onDisconnect:{addListener:()=>{}},
      postMessage:message=>{ globalThis.nativeRequests.push(message);
        const emit=msg=>listeners.forEach(fn=>fn(msg));
        const reply=data=>emit({id:message.id,ok:true,data});
        const progress=(status,percent,text)=>emit({event:'progress',job:{title:probe.title,status,percent,message:text}});
        if(message.action==='health')reply(folderInfo());
        if(message.action==='soundpadCategories')setTimeout(()=>{globalThis.categoryOffline?emit({id:message.id,ok:false,error:'Soundpad açık değil.'}):reply({categories:globalThis.categoryRows});},150);
        globalThis.clearJob=()=>emit({event:'progress',job:null});
        if(message.action==='chooseFolder'){
          folderChoosing=true;
          setTimeout(()=>{chosenFolder='D:\\Videolar\\Seçtiğim klasör';folderChoosing=false;reply(folderInfo());},1800);
        }
        if(message.action==='resetFolder'){chosenFolder='C:\\Users\\Tester\\Downloads';reply(folderInfo());}
        if(message.action==='inspect')reply(videoInfo);
        if(message.action==='download'){
          globalThis.downloadRequests.push(message);
          cancelled=false;reply({accepted:true});progress('starting',0,'Hazırlanıyor…');
          setTimeout(()=>{if(!cancelled)progress('downloading',38,'2.4 MiB/s · Kalan 00:04');},400);
          if(message.mode==='mp3')setTimeout(()=>{if(!cancelled){
            const job={title:probe.title,status:'done',percent:100,message:'Big Buck Bunny - 192kbps.mp3'};
            if(message.soundpadAuto)job.soundpad=globalThis.soundpadFailure?{status:'error',message:'Soundpad açık değil. Dosyan kaydedildi.'}:{status:'added',message:'Soundpad · '+message.soundpadCategory.join(' / ')+' kategorisine eklendi.'};
            emit({event:'progress',job});
          }},1600);
        }
        if(message.action==='cancel'){cancelled=true;reply({});progress('cancelled',0,'İndirme iptal edildi.');}
      }
    });
  },{video,probe});
  const popup = new URL('popup.html', worker.url()).href;
  async function open() {
    const page = await context.newPage();
    // Opening a popup as a test tab does not receive toolbar activeTab grants.
    // Substitute the active-tab URL; the native port is a test double above.
    await page.addInitScript(url => { chrome.tabs.query = async () => [{url}]; globalThis.preferenceRequests = []; const send = chrome.runtime.sendMessage.bind(chrome.runtime); chrome.runtime.sendMessage = (...args) => { globalThis.preferenceRequests.push(args[0]); return send(...args); }; }, video);
    await page.goto(popup);
    await page.waitForFunction(() => document.querySelector('#connection').classList.contains('connected'),{},{timeout:20000});
    return page;
  }
  let page = await open();
  await page.waitForFunction(() => !document.querySelector('#download').disabled,{},{timeout:140000});
  assert.match(await page.locator('#title').innerText(),/Big Buck Bunny/);
  assert.match(await page.locator('.brand').innerText(),/Diskora/);
  assert.equal(await page.title(),'ClipFlow');
  assert.equal(await worker.evaluate(()=>chrome.runtime.getManifest().name),'ClipFlow • YouTube MP3 / MP4');
  assert.equal(await worker.evaluate(()=>globalThis.nativeRequests.filter(r=>r.action==='soundpadCategories').length),0);
  const choices = await page.locator('#quality option').allTextContents();
  assert(choices.some(t => t.includes('2160p')));
  const q1080 = await page.locator('#quality option').evaluateAll(opts => opts.find(o => o.textContent.includes('1080p')).value);
  await page.selectOption('#quality',q1080);
  assert.match(await page.locator('#folder-name').innerText(),/Varsayılan/);
  await page.click('#chooseFolder');
  await page.waitForFunction(()=>document.querySelector('#chooseFolder').disabled);
  assert.equal(await page.locator('#download').isEnabled(),false);
  await page.close();
  page=await open();
  await page.waitForFunction(()=>document.querySelector('#folder-path').textContent.includes('Seçtiğim klasör'));
  assert.equal(await page.locator('#resetFolder').isVisible(),true);
  console.log('PASS Folder selection persists across popup close; pending picker blocks download');
  await page.click('#resetFolder');
  await page.waitForFunction(()=>document.querySelector('#folder-name').textContent.includes('Varsayılan'));
  assert.equal(await page.locator('#resetFolder').isVisible(),false);
  console.log('PASS Default Downloads restored');
  await page.click('[data-mode="mp3"]');
  assert.equal(await page.locator('#soundpadAuto').isChecked(),false);
  assert.equal(await page.locator('#quality').inputValue(),'192');
  await page.click('#download');
  await page.waitForFunction(() => !document.querySelector('#progress-panel').hidden);
  assert.equal(await page.locator('#chooseFolder').isEnabled(),false);
  await page.close();
  console.log('PASS Popup closed during download');
  page = await open();
  await page.waitForFunction(() => document.querySelector('#progress-label').textContent === 'Kaydedildi',{},{timeout:300000});
  assert.match(await page.locator('#progress-detail').innerText(),/192kbps\.mp3$/);
  console.log('PASS Simulated MP3 progress restored after popup reopened');
  assert.equal(await worker.evaluate(()=>globalThis.downloadRequests.at(-1).soundpadAuto),false);
  assert.equal(await page.locator('#soundpad-result').isVisible(),false);
  await page.click('[data-mode="mp3"]');
  await page.check('#soundpadAuto');
  await page.waitForFunction(()=>document.querySelector('#soundpadAuto').checked && !document.querySelector('#soundpadAuto').disabled);
  assert.equal(await worker.evaluate(async()=> (await chrome.storage.local.get('soundpadAuto')).soundpadAuto),true);
  assert.equal(await page.locator('#notice').isVisible(),false);
  assert.equal(await page.evaluate(()=>globalThis.preferenceRequests.some(r=>r.type==='setSoundpadAuto')),false);
  assert.equal(await worker.evaluate(()=>globalThis.nativeRequests.some(r=>r.action==='setSoundpadAuto')),false);
  console.log('PASS Clicking checkbox sends no unsupported background or native request');
  await page.waitForFunction(()=>document.querySelector('#soundpadCategory').value===JSON.stringify(['İndirilenler']));
  await page.selectOption('#soundpadCategory',JSON.stringify(['Oyun','Efektler']));
  await page.waitForFunction(()=>!document.querySelector('#soundpadCategory').disabled);
  assert.deepEqual(await worker.evaluate(async()=>(await chrome.storage.local.get('soundpadCategory')).soundpadCategory),['Oyun','Efektler']);
  await page.close();page=await open();await page.click('[data-mode="mp3"]');
  await page.waitForFunction(()=>document.querySelector('#soundpadCategory').value===JSON.stringify(['Oyun','Efektler']) && !document.querySelector('#soundpadCategory').disabled);
  await worker.evaluate(()=>globalThis.clearJob());
  await page.waitForFunction(()=>document.querySelector('#progress-panel').hidden);
  assert((await page.locator('#download').boundingBox()).y+(await page.locator('#download').boundingBox()).height <= 600);
  await page.screenshot({path:path.resolve(path.join(work,'ClipFlow-v1.0.0-onizleme.png')),fullPage:true});
  assert.equal(await page.locator('body').evaluate(e=>e.scrollWidth),392);
  console.log('PASS ClipFlow name and Diskora branding and category selector; prior default migrates and custom selection persists');
  assert.equal(await page.locator('#soundpadAuto').isChecked(),true);
  await page.click('#download');
  await page.waitForFunction(()=>document.querySelector('#soundpad-result').textContent.includes('kategorisine eklendi'));
  assert.equal(await worker.evaluate(()=>globalThis.downloadRequests.at(-1).soundpadAuto),true);
  console.log('PASS Checkbox defaults off, persists when enabled and opts MP3 into target category');
  assert.deepEqual(await worker.evaluate(()=>globalThis.downloadRequests.at(-1).soundpadCategory),['Oyun','Efektler']);
  await worker.evaluate(()=>{globalThis.soundpadFailure=true;});
  await page.click('#download');
  await page.waitForFunction(()=>document.querySelector('#soundpad-result').classList.contains('soundpad-error'));
  assert.equal(await page.locator('#progress-label').innerText(),'Kaydedildi');
  console.log('PASS Soundpad warning is separate from successful download');
  await worker.evaluate(()=>{globalThis.categoryRows=globalThis.categoryRows.filter(c=>c.name!=='Efektler');});
  await page.click('#refreshCategories');
  await page.waitForFunction(()=>document.querySelector('#category-hint').textContent.includes('silinmiş'));
  assert.equal(await page.locator('#download').isEnabled(),false);
  assert.equal(await page.locator('#soundpadCategory').inputValue(),'');
  await page.selectOption('#soundpadCategory',JSON.stringify(['Müzik']));
  await page.waitForFunction(()=>!document.querySelector('#soundpadCategory').disabled);
  await worker.evaluate(()=>{globalThis.categoryOffline=true;});
  await page.click('#refreshCategories');
  await page.waitForFunction(()=>document.querySelector('#category-hint').textContent.includes('açık değil'));
  assert.equal(await page.locator('#download').isEnabled(),false);
  console.log('PASS Missing category never silently replaced; offline Soundpad shows recoverable error');
  await page.uncheck('#soundpadAuto');
  await page.waitForFunction(()=>!document.querySelector('#soundpadAuto').checked && !document.querySelector('#soundpadAuto').disabled);
  await page.click('#download');
  await page.waitForFunction(()=>document.querySelector('#progress-label').textContent==='Kaydedildi');
  assert.equal(await worker.evaluate(()=>globalThis.downloadRequests.at(-1).soundpadAuto),false);
  assert.equal(await page.locator('#soundpad-result').isVisible(),false);
  assert.equal(await page.locator('#soundpad-category-panel').isVisible(),false);
  assert.equal(await worker.evaluate(()=> 'soundpadCategory' in globalThis.downloadRequests.at(-1)),false);
  await worker.evaluate(()=>{globalThis.categoryOffline=false;});
  console.log('PASS Unchecking prevents the next import and restores normal download after category error');
  await page.check('#soundpadAuto');
  await page.waitForFunction(()=>document.querySelector('#soundpadAuto').checked && !document.querySelector('#soundpadAuto').disabled);
  await page.click('[data-mode="mp4"]');
  assert.equal(await page.locator('#soundpad-option').isVisible(),false);
  await page.click('#download');
  await page.waitForFunction(() => document.querySelector('#progress-label').textContent === 'İndiriliyor',{},{timeout:120000});
  await page.click('#cancel');
  await page.waitForFunction(() => document.querySelector('#progress-label').textContent === 'İptal edildi',{},{timeout:20000});
  assert.equal(await page.locator('#download').isEnabled(),true);
  assert.equal(await worker.evaluate(()=>globalThis.downloadRequests.at(-1).soundpadAuto),false);
  console.log('PASS Active MP4 download cancelled and UI recovered');
  await page.click('[data-mode="mp3"]');
  await worker.evaluate(()=>{globalThis.hostVersion='1.2.2';});
  await page.close(); page=await open(); await page.click('[data-mode="mp3"]');
  const before=await worker.evaluate(()=>globalThis.downloadRequests.length);
  await page.waitForFunction(()=>document.querySelector('#category-hint').textContent.includes('Kategori seçimi'));
  assert.equal(await page.locator('#download').isEnabled(),false);
  assert.equal(await worker.evaluate(()=>globalThis.downloadRequests.length),before);
  // An old 1.0.0 helper must not be confused with public ClipFlow 1.0.0.
  await worker.evaluate(()=>{globalThis.hostVersion='1.0.0';});
  await page.close(); page=await open(); await page.click('[data-mode="mp3"]');
  await page.waitForFunction(()=>document.querySelector('#category-hint').textContent.includes('Kategori seçimi'));
  assert.equal(await page.locator('#download').isEnabled(),false);
  assert.equal(await worker.evaluate(()=>globalThis.downloadRequests.length),before);
  console.log('PASS Public 1.0.0 advertises Soundpad capabilities; legacy 1.0.0 without capabilities is rejected');
  await page.uncheck('#soundpadAuto');
  await page.waitForFunction(()=>!document.querySelector('#soundpadAuto').disabled);
  await page.click('#download');
  await page.waitForFunction(()=>document.querySelector('#progress-label').textContent==='Kaydedildi');
  assert.equal(await worker.evaluate(()=>globalThis.downloadRequests.at(-1).soundpadAuto),false);
  console.log('PASS Old native helper cannot silently ignore opt-in; unchecked download still works');
  assert.deepEqual(errors,[]);
  console.log('PASS No browser page errors');
  fs.writeFileSync(path.join(work,`browser-results-v1.0.0-${channel}.json`),JSON.stringify({passed:true,browser:channel,nativePort:'test double; real host tested separately',qualities:choices,tests:['folder settings','format switching','state after popup close/reopen','checkbox default off','persistent opt-in','unchecked means no import','MP4 ignores remembered opt-in','Soundpad failure warning','cancel UI','no page errors']},null,2));
} finally { await context.close(); }
