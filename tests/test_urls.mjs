import assert from 'node:assert/strict';
import { videoUrl } from '../extension/shared.js';
const canonical = 'https://www.youtube.com/watch?v=aqz-KE-bpKQ';
for (const url of [canonical,canonical+'&list=PL123&t=7','https://youtu.be/aqz-KE-bpKQ','https://www.youtube.com/shorts/aqz-KE-bpKQ','https://music.youtube.com/watch?v=aqz-KE-bpKQ','https://www.youtube.com/live/aqz-KE-bpKQ']) assert.equal(videoUrl(url),canonical);
for (const url of ['file:///C:/Windows','javascript:alert(1)','https://youtube.com.evil.test/watch?v=aqz-KE-bpKQ','https://www.youtube.com/playlist?list=PL123','https://www.youtube.com/watch?v=bad','https://example.com','not a url']) assert.equal(videoUrl(url),null);
console.log('PASS 13 URL recognition and rejection cases');
