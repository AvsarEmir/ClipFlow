import { videoUrl, busyStates } from './shared.js';
import { api } from './browser-api.js';
const $ = id => document.getElementById(id);
const VERSION = '1.0.0';
let state = { connected: false, video: null, job: null };
let currentUrl = null;
let mode = 'mp4';
let loading = false;
let localError = '';
let qualityVideo = '';
let savingPreference = false;
let soundpadAuto = false;
let backgroundMismatch = false;
let soundpadCategory = null;
let categories = [];
let categoriesLoaded = false;
let categoriesLoading = false;
let categoryError = '';
const categoryKey = path => JSON.stringify(path);
function selectedCategory() {
  return categories.find(c => c.selectable && categoryKey(c.path) === categoryKey(soundpadCategory));
}

async function loadCategories(force = false) {
  if (mode !== 'mp3' || !soundpadAuto || !state.connected || backgroundMismatch || busyStates.includes(state.job?.status) || categoriesLoading || (categoriesLoaded && !force)) return;
  categoriesLoading = true; categoryError = ''; render();
  try {
    const data = await send('soundpadCategories');
    categories = data.categories || [];
    categoriesLoaded = true;
    // Preserve the previous default only on the first use. Never replace a lost selection.
    if (!soundpadCategory && soundpadAuto) {
      const defaults = categories.filter(c => c.selectable && c.path.length === 1 && c.name.toLocaleLowerCase('tr-TR') === 'indirilenler');
      if (defaults.length === 1) {
        await api.storage.local.set({ soundpadCategory: defaults[0].path });
        soundpadCategory = defaults[0].path;
      }
    }
  } catch (error) { categories = []; categoriesLoaded = false; categoryError = error.message; }
  finally { categoriesLoading = false; render(); }
}

function setState(next) {
  state = next;
  backgroundMismatch = state.backgroundVersion !== VERSION;
}

async function send(type, params = {}) {
  const result = await api.runtime.sendMessage({ type, ...params });
  if (!result?.ok) throw new Error(result?.error || 'Bağlantı kurulamadı.');
  return result.data;
}

function options(force = false) {
  const video = state.video?.url === currentUrl ? state.video : null;
  const key = `${video?.url || ''}:${mode}:${JSON.stringify(video?.qualities || [])}`;
  if (qualityVideo === key && !force) return;
  qualityVideo = key;
  const choices = mode === 'mp3'
    ? [320, 256, 192, 128].map(n => ({ value: String(n), label: `${n} kbps${n === 192 ? ' · Dengeli' : n === 320 ? ' · Yüksek bit hızı' : ''}` }))
    : (video?.qualities || []).map(q => ({ value: q.id, label: `${q.height}p${q.fps > 30 ? ` · ${Math.round(q.fps)} fps` : ''}${q.height >= 2160 ? ' · 4K' : q.height === 1080 ? ' · Full HD' : ''}` }));
  $('quality').replaceChildren(...(choices.length ? choices : [{ value: '', label: 'Kullanılabilir kalite yok' }]).map(q => new Option(q.label, q.value)));
  if (mode === 'mp3') $('quality').value = '192';
}

function render() {
  const active = busyStates.includes(state.job?.status);
  const video = state.video?.url === currentUrl ? state.video : null;
  const needsCategory = mode === 'mp3' && soundpadAuto;
  const category = selectedCategory();
  document.body.classList.toggle('with-category', needsCategory);
  $('title').textContent = video?.title || (loading ? 'Video bilgileri alınıyor…' : currentUrl ? 'Video bilgileri bekleniyor' : 'Bir YouTube videosu aç');
  $('subtitle').textContent = video ? `${video.channel || 'YouTube'}${video.duration ? ` · ${Math.floor(video.duration / 60)}:${String(Math.floor(video.duration % 60)).padStart(2, '0')}` : ''}` : active ? 'Yeni video için indirmenin bitmesini bekle.' : 'MP3 veya MP4 olarak bilgisayarına kaydet.';
  options();
  $('quality').disabled = !video || active || loading;
  for (const button of document.querySelectorAll('.format')) {
    button.classList.toggle('selected', button.dataset.mode === mode);
    button.setAttribute('aria-pressed', String(button.dataset.mode === mode));
    button.disabled = active;
  }
  $('quality-tag').textContent = mode === 'mp4' ? 'Videoya göre' : 'Ses bit hızı';
  $('quality-note').textContent = mode === 'mp4' ? 'Kullanılabilir çözünürlükler videodan alınır.' : 'Yüksek bit hızı, kaynak sesin kalitesini artırmaz.';
  $('download').disabled = backgroundMismatch || !state.connected || !video || !currentUrl || loading || active || savingPreference || state.folderChoosing || !$('quality').value || (needsCategory && (categoriesLoading || !category));
  $('download').querySelector('b').textContent = active ? 'İndirme devam ediyor' : loading ? 'Video hazırlanıyor…' : `${mode.toUpperCase()} olarak indir`;
  $('refresh').disabled = loading || active || !currentUrl;
  $('folder').disabled = !state.connected;
  $('folder').title = state.folder || '';
  $('folder-name').textContent = state.folderDefault ? 'İndirilenler · Varsayılan' : (state.folder?.replace(/[\\/]+$/, '').split(/[\\/]/).pop() || 'Seçilen klasör');
  $('folder-path').textContent = state.folder || 'Bağlantı bekleniyor';
  $('folder-path').title = state.folder || '';
  $('chooseFolder').disabled = !state.connected || active || loading || state.folderChoosing;
  $('chooseFolder').textContent = state.folderChoosing ? 'Seçiliyor…' : 'Klasör seç';
  $('resetFolder').hidden = state.folderDefault || !state.connected;
  $('resetFolder').disabled = active || loading || state.folderChoosing;
  $('folder-hint').hidden = !state.folderChoosing;
  $('soundpad-option').hidden = mode !== 'mp3';
  $('soundpadAuto').checked = soundpadAuto;
  $('soundpadAuto').disabled = backgroundMismatch || active || savingPreference;
  $('soundpad-category-panel').hidden = !needsCategory;
  const picker = $('soundpadCategory');
  const placeholder = new Option(categoriesLoading ? 'Kategoriler yükleniyor…' : soundpadCategory && !category ? 'Önceki kategori bulunamadı — yeniden seç' : 'Kategori seç', '');
  placeholder.disabled = true;
  picker.replaceChildren(placeholder, ...categories.map(c => {
    const option = new Option(c.label + (c.selectable ? '' : ' (aynı ad)'), categoryKey(c.path));
    option.disabled = !c.selectable;
    return option;
  }));
  picker.value = category ? categoryKey(category.path) : '';
  picker.disabled = backgroundMismatch || active || savingPreference || categoriesLoading || !categories.length;
  $('refreshCategories').disabled = backgroundMismatch || active || savingPreference || categoriesLoading || !state.connected;
  $('category-hint').textContent = categoryError || (categoriesLoading ? 'Soundpad’deki kategoriler okunuyor…' : categoriesLoaded && !categories.length ? 'Soundpad’de bir kategori oluşturup listeyi yenile.' : soundpadCategory && !category ? 'Seçilen kategori silinmiş veya adı değişmiş olabilir. Listeden yeniden seç.' : 'Tamamlanan MP3 seçtiğin kategoriye eklenir.');
  $('category-hint').classList.toggle('category-error', !!categoryError || (categoriesLoaded && !category));
  $('category-hint').hidden = !!category && !categoriesLoading && !categoryError;
  $('connection').className = state.connected ? 'connected' : '';
  $('connection').replaceChildren(Object.assign(document.createElement('i'), {}), document.createTextNode(state.connected ? 'Yerel yardımcı bağlı' : 'Yerel yardımcı bağlı değil'));
  $('setup').hidden = state.connected;
  const error = backgroundMismatch ? 'Güncellemenin tamamlanması için eklentiyi yeniden yükle. Bu pencere kapanacak; ardından ClipFlow simgesine tekrar tıkla.' : localError || state.error;
  $('notice').textContent = error || ''; $('notice').hidden = !error;
  $('reloadExtension').hidden = !backgroundMismatch;
  $('reloadExtension').disabled = active || state.folderChoosing;
  const job = state.job;
  $('progress-panel').hidden = !job;
  if (job) {
    $('progress-label').textContent = ({ starting: 'Başlatılıyor', downloading: 'İndiriliyor', processing: 'Dosya hazırlanıyor', importing: 'Soundpad’e ekleniyor', done: 'Kaydedildi', error: 'İndirme başarısız', cancelled: 'İptal edildi', cancelling: 'İptal ediliyor' })[job.status] || 'İndiriliyor';
    $('job-title').textContent = job.title || '';
    $('progress').value = job.status === 'done' ? 100 : (job.percent || 0);
    $('percent').textContent = job.status === 'processing' ? '…' : `${Math.floor(job.status === 'done' ? 100 : job.percent || 0)}%`;
    $('progress-detail').textContent = job.message || '';
    $('cancel').hidden = !active || job.status === 'importing'; $('cancel').disabled = job.status === 'cancelling';
    $('soundpad-result').hidden = !job.soundpad;
    $('soundpad-result').textContent = job.soundpad?.message || '';
    $('soundpad-result').className = job.soundpad?.status === 'error' ? 'soundpad-error' : '';
  }
}

async function inspect(refresh = false) {
  if (!currentUrl || !state.connected || busyStates.includes(state.job?.status)) return;
  loading = true; localError = ''; render();
  try { setState(await send('inspect', { url: currentUrl, refresh })); }
  catch (error) { localError = error.message; }
  finally { loading = false; render(); }
}
async function connect() {
  localError = '';
  try { setState(await send('health')); render(); if (!backgroundMismatch) await Promise.all([inspect(), loadCategories()]); }
  catch (error) { localError = error.message; render(); }
}
api.runtime.onMessage.addListener(message => {
  if (message.type === 'state') { setState(message.state); render(); }
});
api.storage.onChanged.addListener((changes, area) => {
  if (area === 'local' && changes.soundpadAuto) { soundpadAuto = changes.soundpadAuto.newValue === true; render(); }
  if (area === 'local' && changes.soundpadCategory) { soundpadCategory = changes.soundpadCategory.newValue || null; render(); }
});
$('reloadExtension').addEventListener('click', () => {
  if (!busyStates.includes(state.job?.status) && !state.folderChoosing) api.runtime.reload();
});
for (const button of document.querySelectorAll('.format')) button.addEventListener('click', () => { mode = button.dataset.mode; render(); loadCategories(); });
$('refresh').addEventListener('click', () => inspect(true));
$('reconnect').addEventListener('click', connect);
$('soundpadAuto').addEventListener('change', async () => {
  const enabled = $('soundpadAuto').checked;
  const previous = soundpadAuto;
  soundpadAuto = enabled;
  savingPreference = true; localError = ''; render();
  try { await api.storage.local.set({ soundpadAuto: enabled }); }
  catch (error) { soundpadAuto = previous; localError = error.message; }
  finally { savingPreference = false; render(); }
  if (soundpadAuto) await loadCategories();
});
$('refreshCategories').addEventListener('click', () => loadCategories(true));
$('soundpadCategory').addEventListener('change', async () => {
  const selected = categories.find(c => c.selectable && categoryKey(c.path) === $('soundpadCategory').value);
  if (!selected) return;
  const previous = soundpadCategory;
  soundpadCategory = selected.path; savingPreference = true; localError = ''; render();
  try { await api.storage.local.set({ soundpadCategory: selected.path }); }
  catch (error) { soundpadCategory = previous; localError = error.message; }
  finally { savingPreference = false; render(); }
});
$('download').addEventListener('click', async () => {
  localError = ''; $('download').disabled = true;
  try { setState(await send('download', { url: currentUrl, mode, quality: $('quality').value })); }
  catch (error) { localError = error.message; }
  render();
});
for (const action of ['cancel', 'folder']) $(action).addEventListener('click', async () => {
  try { await send(action); } catch (error) { localError = error.message; render(); }
});
for (const action of ['chooseFolder', 'resetFolder']) $(action).addEventListener('click', async () => {
  localError = ''; $('chooseFolder').disabled = true; $('resetFolder').disabled = true;
  try { setState(await send(action)); } catch (error) { localError = error.message; }
  render();
});
try {
  const [[tab], preferences] = await Promise.all([api.tabs.query({ active: true, currentWindow: true }), api.storage.local.get(['soundpadAuto', 'soundpadCategory'])]);
  soundpadAuto = preferences.soundpadAuto === true;
  soundpadCategory = Array.isArray(preferences.soundpadCategory) ? preferences.soundpadCategory : null;
  currentUrl = videoUrl(tab?.url);
  setState(await send('getState')); render(); if (!backgroundMismatch) await connect();
} catch (error) { localError = error.message; render(); }
