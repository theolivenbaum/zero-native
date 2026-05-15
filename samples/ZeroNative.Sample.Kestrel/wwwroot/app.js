const output = document.getElementById('output');

function show(label, value) {
  output.textContent = `${label}\n${typeof value === 'string' ? value : JSON.stringify(value, null, 2)}`;
}

async function callJson(method, path, body) {
  const init = { method, headers: { 'Content-Type': 'application/json' } };
  if (body !== undefined) init.body = JSON.stringify(body);
  const res = await fetch(path, init);
  const text = await res.text();
  try { return JSON.parse(text); } catch { return text; }
}

document.getElementById('echo').addEventListener('click', async () => {
  try { show('GET /api/echo', await callJson('GET', '/api/echo?message=' + encodeURIComponent('hello from the WebView'))); }
  catch (e) { show('error', e.message ?? String(e)); }
});

document.getElementById('post').addEventListener('click', async () => {
  try { show('POST /api/echo', await callJson('POST', '/api/echo', { now: new Date().toISOString(), nested: { value: 42 } })); }
  catch (e) { show('error', e.message ?? String(e)); }
});

document.getElementById('info').addEventListener('click', async () => {
  try { show('GET /api/info', await callJson('GET', '/api/info')); }
  catch (e) { show('error', e.message ?? String(e)); }
});

document.getElementById('clipboard').addEventListener('click', async () => {
  try { show('native.clipboard', await window.zero.invoke('native.clipboard', null)); }
  catch (e) { show('bridge error', { code: e.code, message: e.message }); }
});

document.getElementById('windows').addEventListener('click', async () => {
  try { show('window.list', await window.zero.window.list()); }
  catch (e) { show('bridge error', { code: e.code, message: e.message }); }
});
