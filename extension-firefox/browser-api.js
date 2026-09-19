// Firefox uses its Promise-based browser namespace; Chromium uses chrome.
export const api = globalThis.browser ?? globalThis.chrome;
