export function videoUrl(value) {
  try {
    const u = new URL(value);
    if (u.protocol !== 'https:') return null;
    let id;
    if (u.hostname === 'youtu.be') id = u.pathname.split('/')[1];
    else if (['youtube.com', 'www.youtube.com', 'm.youtube.com', 'music.youtube.com'].includes(u.hostname)) {
      id = u.pathname === '/watch' ? u.searchParams.get('v') : /^\/(?:shorts|live|embed)\/([^/]+)\/?$/.exec(u.pathname)?.[1];
    }
    return /^[\w-]{11}$/.test(id || '') ? `https://www.youtube.com/watch?v=${id}` : null;
  } catch { return null; }
}
export const busyStates = ['starting', 'downloading', 'processing', 'importing', 'cancelling'];
