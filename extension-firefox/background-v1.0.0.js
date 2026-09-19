import { videoUrl, busyStates } from './shared.js';
import { api } from './browser-api.js';

const HOST = 'com.akis.downloader';
const VERSION = '1.0.0';
let nativePort;
let sequence = 0;
let inspection = null;
const pending = new Map();
let state = { backgroundVersion: VERSION, connected: false, nativeVersion: '', nativeCapabilities: [], folder: '', folderDefault: true, folderChoosing: false, soundpadAuto: false, video: null, job: null, error: '' };
const ready = Promise.all([api.storage.session.get('state'), api.storage.local.get('soundpadAuto')]).then(([saved, preferences]) => {
  if (saved.state) state = { ...state, ...saved.state, backgroundVersion: VERSION, connected: false, nativeVersion: '', nativeCapabilities: [], folderChoosing: false };
  state.soundpadAuto = preferences.soundpadAuto === true;
  if (busyStates.includes(state.job?.status)) {
    state.job = { ...state.job, status: 'error', message: 'Tarayıcı bağlantısı kesildi. İndirmeyi yeniden başlatın.' };
  }
});

api.storage.onChanged.addListener((changes, area) => {
  if (area !== 'local' || !changes.soundpadAuto) return;
  ready.then(async () => {
    state.soundpadAuto = (await api.storage.local.get('soundpadAuto')).soundpadAuto === true;
    publish();
  }).catch(() => {});
});

function publish() {
  api.storage.session.set({ state }).catch(() => {});
  api.runtime.sendMessage({ type: 'state', state }).catch(() => {});
  const active = busyStates.includes(state.job?.status);
  api.action.setBadgeText({ text: active ? '↓' : state.job?.status === 'done' ? '✓' : '' });
  api.action.setBadgeBackgroundColor({ color: '#6559ed' });
}

function connect() {
  if (nativePort) return nativePort;
  const p = api.runtime.connectNative(HOST);
  nativePort = p;
  p.onMessage.addListener(message => {
    if (message.event === 'progress') {
      state.job = message.job;
      publish();
      return;
    }
    const waiter = pending.get(message.id);
    if (!waiter) return;
    clearTimeout(waiter.timer);
    pending.delete(message.id);
    if (message.ok) waiter.resolve(message.data);
    else waiter.reject(new Error(message.error || 'İşlem tamamlanamadı.'));
  });
  p.onDisconnect.addListener(() => {
    const detail = p.error?.message || api.runtime.lastError?.message || '';
    if (nativePort === p) nativePort = null;
    state.connected = false;
    state.nativeCapabilities = [];
    state.folderChoosing = false;
    state.video = null;
    const message = /not found|not registered|forbidden/i.test(detail)
      ? 'Yerel yardımcı bulunamadı. Paketteki KUR.cmd dosyasını çalıştırıp tekrar deneyin.'
      : 'Yerel yardımcı bağlantısı kesildi. KUR.cmd ile kurulumu onarıp tekrar deneyin.';
    for (const waiter of pending.values()) { clearTimeout(waiter.timer); waiter.reject(new Error(message)); }
    pending.clear();
    if (busyStates.includes(state.job?.status)) state.job = { ...state.job, status: 'error', message };
    state.error = message;
    publish();
  });
  return p;
}

function request(action, params = {}, timeout = 10000) {
  return new Promise((resolve, reject) => {
    const id = `${Date.now()}-${++sequence}`;
    const timer = timeout ? setTimeout(() => { pending.delete(id); reject(new Error('İşlem zaman aşımına uğradı. Tekrar deneyin.')); }, timeout) : null;
    pending.set(id, { resolve, reject, timer });
    try { connect().postMessage({ id, action, ...params }); }
    catch (error) { clearTimeout(timer); pending.delete(id); reject(error); }
  });
}

async function handle(message) {
  await ready;
  switch (message.type) {
    case 'getState': return state;
    case 'health': {
      const wasConnected = state.connected;
      const data = await request('health');
      state.nativeVersion = data.version || '';
      state.nativeCapabilities = Array.isArray(data.capabilities) ? data.capabilities : [];
      if (!wasConnected) state.video = null;
      state.connected = true; state.folder = data.folder; state.folderDefault = data.folderDefault ?? false; state.folderChoosing = data.folderChoosing ?? false; state.error = ''; publish(); return state;
    }
    case 'soundpadCategories': {
      if ((await api.storage.local.get('soundpadAuto')).soundpadAuto !== true) throw new Error('Önce Soundpad’e otomatik eklemeyi açın.');
      if (!supportsSoundpad(state.nativeVersion)) throw new Error('Kategori seçimi için yeni paketin KUR.cmd dosyasını çalıştırıp eklentiyi yeniden yükleyin.');
      if (busyStates.includes(state.job?.status)) throw new Error('Kategorileri yenilemek için indirmenin bitmesini bekleyin.');
      return await request('soundpadCategories', {}, 15000);
    }
    case 'inspect': {
      const url = videoUrl(message.url);
      if (!url) throw new Error('Önce bir YouTube videosu açın.');
      if (busyStates.includes(state.job?.status)) return state;
      if (inspection) { await inspection; if (state.video?.url === url) return state; }
      if (state.video?.url === url && !message.refresh) return state;
      state.video = null; state.error = ''; publish();
      inspection = request('inspect', { url }, 140000);
      try { state.video = await inspection; } finally { inspection = null; }
      state.connected = true; publish(); return state;
    }
    case 'download': {
      // Read the saved choice for every job, including after a worker restart.
      const preferences = await api.storage.local.get(['soundpadAuto', 'soundpadCategory']);
      state.soundpadAuto = preferences.soundpadAuto === true;
      if (state.folderChoosing) throw new Error('Önce klasör seçimini tamamlayın.');
      if (busyStates.includes(state.job?.status)) throw new Error('Bir indirme zaten devam ediyor.');
      const url = videoUrl(message.url);
      if (!url || state.video?.url !== url) throw new Error('Video bilgilerini yeniden yükleyin.');
      const soundpadAuto = state.soundpadAuto && message.mode === 'mp3';
      if (soundpadAuto && !supportsSoundpad(state.nativeVersion)) throw new Error('Soundpad için yerel yardımcıyı güncelleyin: yeni paketin KUR.cmd dosyasını çalıştırın, ardından eklentiyi yeniden yükleyin.');
      const soundpadCategory = preferences.soundpadCategory;
      if (soundpadAuto && (!Array.isArray(soundpadCategory) || !soundpadCategory.length || soundpadCategory.some(part => typeof part !== 'string' || !part.trim()))) throw new Error('Önce bir Soundpad kategorisi seçin.');
      const previous = state.job;
      state.job = { status: 'starting', title: state.video.title, percent: 0, message: 'İndirme başlatılıyor…' }; publish();
      try {
        await request('download', { url, mode: message.mode, quality: String(message.quality), soundpadAuto, ...(soundpadAuto ? { soundpadCategory } : {}) });
      } catch (error) {
        state.job = previous; publish(); throw error;
      }
      return state;
    }
    case 'cancel': await request('cancel'); return state;
    case 'folder': await request('folder'); return state;
    case 'chooseFolder':
    case 'resetFolder': {
      if (state.folderChoosing || inspection || busyStates.includes(state.job?.status)) throw new Error('Klasörü değiştirmek için devam eden işlemin bitmesini bekleyin.');
      state.folderChoosing = true; state.error = ''; publish();
      try {
        const data = await request(message.type, {}, message.type === 'chooseFolder' ? 0 : 10000);
        state.folder = data.folder; state.folderDefault = data.folderDefault;
      } catch (error) { state.error = error.message; throw error; }
      finally { state.folderChoosing = false; publish(); }
      return state;
    }
    default: throw new Error(`ClipFlow arka planı (${VERSION}) bu isteği tanımıyor: ${String(message.type)}. Eklentiyi yeniden yükleyin.`);
  }
}

function supportsSoundpad(version) {
  // Public ClipFlow versions restart at 1.0.0; older Diskora helpers use legacy versioning.
  if (state.nativeCapabilities.includes('soundpadCategories') && state.nativeCapabilities.includes('soundpadImport')) return true;
  const match = /^(\d+)\.(\d+)\.(\d+)$/.exec(version);
  return !!match && (Number(match[1]) > 1 || (Number(match[1]) === 1 && Number(match[2]) >= 3));
}

api.runtime.onMessage.addListener((message, sender, respond) => {
  if (sender.id !== api.runtime.id || message.type === 'state') return false;
  handle(message).then(data => respond({ ok: true, data })).catch(error => respond({ ok: false, error: error.message }));
  return true;
});
