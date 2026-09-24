// AX206 Display web UI. No framework and no build step: this file is served
// as-is by the Pi. Everything user-supplied goes into the DOM as text nodes
// (never innerHTML), since device names come straight off USB descriptors.

const $ = (selector, root = document) => root.querySelector(selector);

function h(tag, attrs = {}, ...children) {
  const el = document.createElement(tag);
  for (const [key, value] of Object.entries(attrs)) {
    if (value === undefined || value === null || value === false) continue;
    if (key === 'class') el.className = value;
    else if (key === 'dataset') Object.assign(el.dataset, value);
    // CSSOM, not a style attribute: the CSP blocks inline style attributes.
    else if (key === 'style') el.style.cssText = value;
    else if (key.startsWith('on')) el.addEventListener(key.slice(2), value);
    else if (key in el && typeof value !== 'string') el[key] = value;
    else el.setAttribute(key, value === true ? '' : value);
  }
  for (const child of children.flat()) {
    if (child === null || child === undefined || child === false) continue;
    el.append(child instanceof Node ? child : document.createTextNode(String(child)));
  }
  return el;
}

class ApiError extends Error {
  constructor(message, status) { super(message); this.status = status; }
}

async function api(method, path, body) {
  const options = { method, headers: { 'X-Requested-With': 'ax206display' }, credentials: 'same-origin' };
  if (body instanceof Blob) {
    options.body = body;
    options.headers['Content-Type'] = body.type || 'application/octet-stream';
  } else if (body !== undefined) {
    options.body = JSON.stringify(body);
    options.headers['Content-Type'] = 'application/json';
  }
  const response = await fetch('/api' + path, options);
  if (response.status === 401 && path !== '/auth/login') {
    showLogin();
    throw new ApiError('Signed out.', 401);
  }
  if (!response.ok) {
    let message = response.statusText;
    try { message = (await response.json()).error || message; } catch { /* not JSON */ }
    if (response.status === 429) message = 'Too many attempts. Wait a few minutes and try again.';
    throw new ApiError(message, response.status);
  }
  if (response.status === 204) return null;
  const type = response.headers.get('content-type') || '';
  return type.includes('json') ? response.json() : response.blob();
}

let toastTimer;
function toast(message, bad = false) {
  const el = $('#toast');
  el.textContent = message;
  el.classList.toggle('bad', bad);
  el.hidden = false;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { el.hidden = true; }, bad ? 6000 : 2500);
}

const state = {
  devices: [],
  catalog: null,
  data: {},
  deviceId: null,
  draft: null,        // { name, brightness, targetFps, widgets }
  saved: null,        // JSON of the last saved draft, for dirty tracking
  selectedId: null,
  scale: 1,
  previewUrl: null,
  previewSeq: 0,
};

const current = () => state.devices.find(d => d.id === state.deviceId);
const selected = () => state.draft?.widgets.find(w => w.id === state.selectedId);
const isDirty = () => state.draft !== null && JSON.stringify(state.draft) !== state.saved;

// ---------------------------------------------------------------- auth

function showLogin() {
  $('#app').hidden = true;
  $('#login').hidden = false;
  $('#login-password').focus();
}

async function showApp() {
  $('#login').hidden = true;
  $('#app').hidden = false;
  await Promise.all([loadCatalog(), loadDevices(), loadStatus()]);
  renderIntegrations();
}

$('#login-form').addEventListener('submit', async event => {
  event.preventDefault();
  $('#login-error').textContent = '';
  try {
    await api('POST', '/auth/login', { password: $('#login-password').value });
    $('#login-password').value = '';
    await showApp();
  } catch (error) {
    $('#login-error').textContent = error.message;
  }
});

$('#logout').addEventListener('click', async () => {
  if (isDirty() && !confirm('Discard unsaved layout changes?')) return;
  await api('POST', '/auth/logout');
  state.draft = null;
  showLogin();
});

// ---------------------------------------------------------------- navigation

for (const tab of document.querySelectorAll('.tab')) {
  tab.addEventListener('click', () => {
    for (const other of document.querySelectorAll('.tab')) other.classList.toggle('active', other === tab);
    for (const view of document.querySelectorAll('.view')) view.hidden = view.id !== 'view-' + tab.dataset.view;
    if (tab.dataset.view === 'integrations') renderIntegrations();
    if (tab.dataset.view === 'settings') loadStatus();
    if (tab.dataset.view === 'displays') { layoutStage(); refreshPreview(); }
  });
}

window.addEventListener('beforeunload', event => {
  if (isDirty()) event.preventDefault();
});

// ---------------------------------------------------------------- data

async function loadCatalog() {
  state.catalog = await api('GET', '/catalog');
  $('#add-buttons').replaceChildren(...state.catalog.types.map(t =>
    h('button', { type: 'button', class: 'small', onclick: () => addWidget(t.type) }, '+ ' + t.displayName)));
}

async function loadStatus() {
  const status = await api('GET', '/status');
  $('#host-name').textContent = status.host ? '· ' + status.host : '';
  $('#about').replaceChildren(
    h('dt', {}, 'Host'), h('dd', {}, status.host || '-'),
    h('dt', {}, 'Version'), h('dd', {}, status.version || '-'),
    h('dt', {}, 'Data directory'), h('dd', {}, h('code', {}, status.dataDirectory)),
    h('dt', {}, 'Connected displays'), h('dd', {}, String(status.connectedDevices)));
}

async function loadDevices() {
  state.devices = await api('GET', '/devices');
  renderDeviceList();
  if (!state.deviceId && state.devices.length) selectDevice(state.devices[0].id);
  else if (state.deviceId && !current()) { state.deviceId = null; state.draft = null; renderEditor(); }
}

function renderDeviceList() {
  $('#no-devices').hidden = state.devices.length > 0;
  $('#devices').replaceChildren(...state.devices.map(d => h('li', {},
    h('button', { type: 'button', class: d.id === state.deviceId ? 'active' : '', onclick: () => selectDevice(d.id) },
      h('span', { class: 'device-name' }, h('span', { class: 'dot' + (d.connected ? ' on' : ''), title: d.connected ? 'Connected' : 'Not connected' }), d.name),
      h('span', { class: 'device-meta' }, `${d.screenWidth}×${d.screenHeight} · ${d.connected ? 'connected' : 'offline'}`)))));
}

function selectDevice(id) {
  if (id === state.deviceId) return;
  if (isDirty() && !confirm('Discard unsaved changes to this layout?')) return;
  const device = state.devices.find(d => d.id === id);
  state.deviceId = id;
  state.selectedId = null;
  state.draft = {
    name: device.name,
    brightness: device.brightness,
    targetFps: device.targetFps,
    widgets: structuredClone(device.widgets),
  };
  state.saved = JSON.stringify(state.draft);
  renderDeviceList();
  renderEditor();
}

$('#scan').addEventListener('click', async event => {
  const button = event.currentTarget;
  button.disabled = true;
  button.textContent = 'Scanning…';
  try {
    const result = await api('POST', '/devices/refresh');
    await loadDevices();
    toast(result.newDevices ? `Found ${result.newDevices} new display(s).` : 'No new displays found.');
  } catch (error) {
    toast(error.message, true);
  } finally {
    button.disabled = false;
    button.textContent = 'Scan USB';
  }
});

// Connection dots and live readings drift; refresh them quietly.
setInterval(async () => {
  if ($('#app').hidden || document.hidden) return;
  try {
    const devices = await api('GET', '/devices');
    for (const fresh of devices) {
      const known = state.devices.find(d => d.id === fresh.id);
      if (known) { known.connected = fresh.connected; known.hasBackgroundImage = fresh.hasBackgroundImage; }
    }
    if (devices.length !== state.devices.length) state.devices = devices;
    renderDeviceList();
    state.data = await api('GET', '/data');
    updateValueHint();
  } catch { /* next tick */ }
}, 5000);

// ---------------------------------------------------------------- editor

function renderEditor() {
  const device = current();
  $('#editor').hidden = !device;
  $('#inspector').hidden = !device;
  if (!device) return;
  layoutStage();
  renderOverlays();
  renderInspector();
  updateDirty();
  refreshPreview();
}

function layoutStage() {
  const device = current();
  if (!device) return;
  const wrap = $('#stage-wrap');
  const available = Math.max(200, wrap.clientWidth - 40);
  const availableHeight = Math.max(200, window.innerHeight - 260);
  state.scale = Math.min(available / device.screenWidth, availableHeight / device.screenHeight, 3);
  const stage = $('#stage');
  stage.style.width = device.screenWidth * state.scale + 'px';
  stage.style.height = device.screenHeight * state.scale + 'px';
  renderOverlays();
}
window.addEventListener('resize', layoutStage);

function renderOverlays() {
  if (!state.draft) return;
  const boxes = [...state.draft.widgets]
    .sort((a, b) => (a.zOrder ?? 0) - (b.zOrder ?? 0))
    .map(widget => {
      const box = h('div', {
        class: 'widget-box' + (widget.id === state.selectedId ? ' selected' : ''),
        dataset: { id: widget.id },
      }, h('span', { class: 'tag' }, describeWidget(widget)), h('span', { class: 'handle' }));
      positionBox(box, widget);
      box.addEventListener('pointerdown', event => startDrag(event, widget, event.target.classList.contains('handle')));
      return box;
    });
  $('#overlays').replaceChildren(...boxes);
}

function positionBox(box, widget) {
  const s = state.scale;
  box.style.left = widget.x * s + 'px';
  box.style.top = widget.y * s + 'px';
  box.style.width = widget.width * s + 'px';
  box.style.height = widget.height * s + 'px';
}

function describeWidget(widget) {
  const type = state.catalog?.types.find(t => t.type === widget.type)?.displayName ?? widget.type;
  const settings = widget.settings || {};
  if (widget.type === 'text' && settings.text) return `${type}: ${settings.text}`;
  if ((widget.type === 'stat' || widget.type === 'gauge') && settings.dataKey) {
    const key = allStatKeys().find(k => k.key === settings.dataKey);
    return `${type}: ${key ? key.displayName : settings.dataKey}`;
  }
  return type;
}

function selectWidget(id) {
  state.selectedId = id;
  for (const box of document.querySelectorAll('.widget-box')) box.classList.toggle('selected', box.dataset.id === id);
  renderInspector();
}

// Snap an edge to the canvas edges/centre and to other widgets' edges.
function snap(value, size, others, limit) {
  const threshold = 5;
  const candidates = [0, limit, limit / 2];
  for (const o of others) candidates.push(o.start, o.end, (o.start + o.end) / 2);
  let best = value;
  let bestDistance = threshold + 1;
  for (const target of candidates) {
    for (const offset of [0, size, size / 2]) {
      const distance = Math.abs(value + offset - target);
      if (distance < bestDistance) { bestDistance = distance; best = target - offset; }
    }
  }
  return Math.round(best);
}

function startDrag(event, widget, resizing) {
  if (event.button !== 0) return;
  event.preventDefault();
  event.stopPropagation();
  selectWidget(widget.id);
  $('#stage').focus({ preventScroll: true });

  const device = current();
  const box = event.currentTarget;
  const origin = { x: event.clientX, y: event.clientY, wx: widget.x, wy: widget.y, ww: widget.width, wh: widget.height };
  const others = state.draft.widgets.filter(w => w !== widget);
  const xs = others.map(w => ({ start: w.x, end: w.x + w.width }));
  const ys = others.map(w => ({ start: w.y, end: w.y + w.height }));
  box.setPointerCapture(event.pointerId);

  const move = moveEvent => {
    const dx = (moveEvent.clientX - origin.x) / state.scale;
    const dy = (moveEvent.clientY - origin.y) / state.scale;
    const snapping = !moveEvent.altKey;
    if (resizing) {
      let right = origin.wx + origin.ww + dx;
      let bottom = origin.wy + origin.wh + dy;
      if (snapping) {
        right = snap(right, 0, xs, device.screenWidth);
        bottom = snap(bottom, 0, ys, device.screenHeight);
      }
      widget.width = Math.max(4, Math.min(Math.round(right - widget.x), device.screenWidth - widget.x));
      widget.height = Math.max(4, Math.min(Math.round(bottom - widget.y), device.screenHeight - widget.y));
    } else {
      let x = origin.wx + dx;
      let y = origin.wy + dy;
      if (snapping) {
        x = snap(x, widget.width, xs, device.screenWidth);
        y = snap(y, widget.height, ys, device.screenHeight);
      }
      widget.x = clamp(Math.round(x), 0, Math.max(0, device.screenWidth - widget.width));
      widget.y = clamp(Math.round(y), 0, Math.max(0, device.screenHeight - widget.height));
    }
    positionBox(box, widget);
    syncGeometryFields();
    changed();
  };
  const end = () => {
    box.removeEventListener('pointermove', move);
    box.removeEventListener('pointerup', end);
    box.removeEventListener('pointercancel', end);
  };
  box.addEventListener('pointermove', move);
  box.addEventListener('pointerup', end);
  box.addEventListener('pointercancel', end);
}

$('#stage').addEventListener('pointerdown', event => {
  if (event.target.closest('.widget-box')) return;
  selectWidget(null);
});

document.addEventListener('keydown', event => {
  if ($('#app').hidden || $('#view-displays').hidden || !state.draft) return;
  const typing = event.target.closest('input, select, textarea');
  if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 's') {
    event.preventDefault();
    save();
    return;
  }
  const widget = selected();
  if (!widget || typing) return;
  const step = event.shiftKey ? 10 : 1;
  const device = current();
  const moves = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
  if (moves[event.key]) {
    event.preventDefault();
    const [dx, dy] = moves[event.key];
    widget.x = clamp(widget.x + dx, 0, Math.max(0, device.screenWidth - widget.width));
    widget.y = clamp(widget.y + dy, 0, Math.max(0, device.screenHeight - widget.height));
    const box = document.querySelector(`.widget-box[data-id="${CSS.escape(widget.id)}"]`);
    if (box) positionBox(box, widget);
    syncGeometryFields();
    changed();
  } else if (event.key === 'Delete' || event.key === 'Backspace') {
    event.preventDefault();
    deleteSelected();
  }
});

async function addWidget(type) {
  const device = current();
  const nextZ = Math.max(-1, ...state.draft.widgets.map(w => w.zOrder ?? 0)) + 1;
  try {
    const widget = await api('GET', `/catalog/new-widget?type=${encodeURIComponent(type)}&width=${device.screenWidth}&height=${device.screenHeight}&zOrder=${nextZ}`);
    state.draft.widgets.push(widget);
    state.selectedId = widget.id;
    renderOverlays();
    renderInspector();
    changed();
  } catch (error) {
    toast(error.message, true);
  }
}

function deleteSelected() {
  if (!state.selectedId) return;
  state.draft.widgets = state.draft.widgets.filter(w => w.id !== state.selectedId);
  state.selectedId = null;
  renderOverlays();
  renderInspector();
  changed();
}

function changed() {
  updateDirty();
  schedulePreview();
}

function updateDirty() {
  const dirty = isDirty();
  $('#dirty').hidden = !dirty;
  $('#revert').disabled = !dirty;
}

$('#revert').addEventListener('click', () => {
  state.draft = JSON.parse(state.saved);
  state.selectedId = null;
  renderEditor();
});

$('#save').addEventListener('click', () => save());

async function save() {
  if (!state.draft) return;
  const button = $('#save');
  button.disabled = true;
  try {
    const updated = await api('PUT', '/devices/' + encodeURIComponent(state.deviceId), state.draft);
    const index = state.devices.findIndex(d => d.id === updated.id);
    state.devices[index] = updated;
    state.draft.name = updated.name;
    state.saved = JSON.stringify(state.draft);
    renderDeviceList();
    updateDirty();
    toast(updated.connected ? 'Saved. The display picks it up within a few seconds.' : 'Saved. It will show when the display is connected.');
  } catch (error) {
    toast(error.message, true);
  } finally {
    button.disabled = false;
  }
}

// ---------------------------------------------------------------- preview

let previewTimer;
function schedulePreview() {
  clearTimeout(previewTimer);
  previewTimer = setTimeout(refreshPreview, 120);
}

async function refreshPreview() {
  if (!state.draft || !state.deviceId) return;
  const seq = ++state.previewSeq;
  try {
    const blob = await api('POST', `/devices/${encodeURIComponent(state.deviceId)}/preview`, { widgets: state.draft.widgets });
    if (seq !== state.previewSeq) return; // a newer render is on its way
    const url = URL.createObjectURL(blob);
    const img = $('#preview');
    img.onload = () => { if (state.previewUrl && state.previewUrl !== url) URL.revokeObjectURL(state.previewUrl); state.previewUrl = url; };
    img.src = url;
  } catch (error) {
    if (error.status !== 401 && error.status !== 400) toast('Preview failed: ' + error.message, true);
  }
}

// The clock ticks and readings move, so keep the preview live.
setInterval(() => {
  if (!$('#app').hidden && !$('#view-displays').hidden && !document.hidden) refreshPreview();
}, 2000);

// ---------------------------------------------------------------- inspector

function allStatKeys() {
  return state.catalog?.statKeys ?? [];
}

function field(label, input, extraClass) {
  return h('label', { class: extraClass }, label, input);
}

function numberInput(value, onchange, { min, max, step = 1 } = {}) {
  return h('input', { type: 'number', value: value ?? '', min, max, step, oninput: event => onchange(event.target.value === '' ? null : Number(event.target.value)) });
}

function textInput(value, onchange, attrs = {}) {
  return h('input', { type: 'text', value: value ?? '', oninput: event => onchange(event.target.value), ...attrs });
}

function select(options, value, onchange) {
  return h('select', { onchange: event => onchange(event.target.value) },
    options.map(o => h('option', { value: o.value, selected: o.value === value }, o.label)));
}

function setSetting(widget, key, value) {
  widget.settings ??= {};
  if (value === null || value === undefined || value === '') delete widget.settings[key];
  else widget.settings[key] = value;
  const box = document.querySelector(`.widget-box[data-id="${CSS.escape(widget.id)}"] .tag`);
  if (box) box.textContent = describeWidget(widget);
  changed();
}

function syncGeometryFields() {
  const widget = selected();
  if (!widget) return;
  for (const key of ['x', 'y', 'width', 'height']) {
    const input = document.querySelector(`[data-geometry="${key}"]`);
    if (input && document.activeElement !== input) input.value = widget[key];
  }
}

function geometryInput(widget, key, min) {
  const device = current();
  const input = numberInput(widget[key], value => {
    if (value === null || Number.isNaN(value)) return;
    widget[key] = Math.max(min, Math.round(value));
    const limitW = device.screenWidth, limitH = device.screenHeight;
    widget.width = Math.min(widget.width, limitW);
    widget.height = Math.min(widget.height, limitH);
    const box = document.querySelector(`.widget-box[data-id="${CSS.escape(widget.id)}"]`);
    if (box) positionBox(box, widget);
    changed();
  }, { min });
  input.dataset.geometry = key;
  return input;
}

function colorPicker(widget, key, label) {
  const currentValue = (widget.settings?.[key] || '').toUpperCase();
  const swatches = h('div', { class: 'swatches' },
    h('button', { type: 'button', class: 'swatch none' + (currentValue ? '' : ' active'), title: 'Default', onclick: () => { setSetting(widget, key, null); renderInspector(); } }),
    state.catalog.colors.map(c => h('button', {
      type: 'button', class: 'swatch' + (currentValue === c.hex.toUpperCase() ? ' active' : ''), title: c.name,
      style: `background:${c.hex}`, onclick: () => { setSetting(widget, key, c.hex); renderInspector(); },
    })),
    h('input', { type: 'color', title: 'Custom colour', value: /^#[0-9A-F]{6}$/.test(currentValue) ? currentValue.toLowerCase() : '#ffffff', onchange: event => { setSetting(widget, key, event.target.value.toUpperCase()); renderInspector(); } }));
  return h('label', {}, label, swatches);
}

function readingSelect(widget) {
  const keys = allStatKeys();
  const selectedKey = widget.settings?.dataKey;
  const groups = new Map();
  for (const key of keys) {
    if (!groups.has(key.category)) groups.set(key.category, []);
    groups.get(key.category).push(key);
  }
  const hasSelected = keys.some(k => k.key === selectedKey);
  const control = h('select', {
    onchange: event => {
      const descriptor = keys.find(k => k.key === event.target.value);
      setSetting(widget, 'dataKey', descriptor.key);
      setSetting(widget, 'label', descriptor.defaultLabel);
      setSetting(widget, 'unit', descriptor.defaultUnit);
      renderInspector();
    },
  },
  hasSelected ? null : h('option', { value: selectedKey ?? '', selected: true }, selectedKey ? `${selectedKey} (not reporting)` : 'Choose…'),
  [...groups].map(([category, items]) => h('optgroup', { label: category },
    items.map(k => h('option', { value: k.key, selected: k.key === selectedKey }, k.displayName)))));
  return h('label', {}, 'Reading', control, h('div', { class: 'value-hint', id: 'value-hint' }));
}

function updateValueHint() {
  const hint = $('#value-hint');
  const widget = selected();
  if (!hint || !widget) return;
  const value = state.data[widget.settings?.dataKey];
  hint.textContent = value === undefined ? 'No value yet.' : `Now: ${typeof value === 'number' ? Math.round(value * 100) / 100 : value}`;
}

function fontControls(widget) {
  const s = widget.settings ?? {};
  const fonts = [{ value: '', label: 'Default' }, ...state.catalog.fonts.map(f => ({ value: f, label: f }))];
  const sizes = [{ value: '', label: 'Auto (fit to box)' }, ...state.catalog.fontSizes.map(px => ({ value: String(px), label: px + ' px' }))];
  return [
    h('div', { class: 'grid2' },
      field('Font', select(fonts, s.fontFamily ?? '', v => setSetting(widget, 'fontFamily', v || null))),
      field('Size', select(sizes, s.fontSizePx !== undefined ? String(s.fontSizePx) : '', v => setSetting(widget, 'fontSizePx', v ? Number(v) : null)))),
    h('div', { class: 'row' },
      h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: !!s.bold, onchange: e => setSetting(widget, 'bold', e.target.checked || null) }), 'Bold'),
      h('label', { class: 'check' }, h('input', { type: 'checkbox', checked: !!s.italic, onchange: e => setSetting(widget, 'italic', e.target.checked || null) }), 'Italic')),
  ];
}

function renderInspector() {
  renderWidgetPanel();
  renderDevicePanel();
}

function renderWidgetPanel() {
  const panel = $('#widget-panel');
  const widget = selected();
  if (!widget) {
    panel.replaceChildren(h('section', {}, h('h3', {}, 'Widget'), h('p', { class: 'muted small' },
      state.draft?.widgets.length ? 'Click a widget on the canvas to edit it.' : 'Add a widget from the toolbar to get started.')));
    return;
  }

  const s = widget.settings ?? {};
  const parts = [h('h3', {}, describeWidget(widget))];

  if (widget.type === 'text') {
    parts.push(field('Text', textInput(s.text, v => setSetting(widget, 'text', v))));
  }
  if (widget.type === 'clock') {
    const formats = state.catalog.timeFormats.map(f => ({ value: f, label: f }));
    parts.push(field('Time format', select(formats, s.timeFormat ?? formats[0].value, v => setSetting(widget, 'timeFormat', v))));
  }
  if (widget.type === 'stat' || widget.type === 'gauge') {
    parts.push(readingSelect(widget));
    parts.push(h('div', { class: 'grid2' },
      field('Label', textInput(s.label, v => setSetting(widget, 'label', v))),
      field('Unit', textInput(s.unit, v => setSetting(widget, 'unit', v)))));
    parts.push(field('Decimals', numberInput(s.decimals ?? 0, v => setSetting(widget, 'decimals', v === null ? null : clamp(Math.round(v), 0, 4)), { min: 0, max: 4 })));
  }
  if (widget.type === 'gauge') {
    parts.push(h('div', { class: 'grid2' },
      field('Min', numberInput(s.minValue ?? 0, v => setSetting(widget, 'minValue', v))),
      field('Max', numberInput(s.maxValue ?? 100, v => setSetting(widget, 'maxValue', v)))));
    parts.push(h('div', { class: 'grid2' },
      field('Value size (px)', numberInput(s.valueFontSizePx, v => setSetting(widget, 'valueFontSizePx', v), { min: 4, max: 200 })),
      field('Label gap (px)', numberInput(s.labelGapPx ?? 0, v => setSetting(widget, 'labelGapPx', v || null), { min: 0, max: 100 }))));
    parts.push(colorPicker(widget, 'gaugeColor', 'Gauge colour'));
  }

  parts.push(colorPicker(widget, 'textColor', 'Text colour'));
  parts.push(...fontControls(widget));

  parts.push(h('div', { class: 'grid4' },
    field('X', geometryInput(widget, 'x', 0)),
    field('Y', geometryInput(widget, 'y', 0)),
    field('W', geometryInput(widget, 'width', 1)),
    field('H', geometryInput(widget, 'height', 1))));

  parts.push(h('div', { class: 'row' },
    h('button', { type: 'button', class: 'small', onclick: () => restack(widget, 1) }, 'Bring forward'),
    h('button', { type: 'button', class: 'small', onclick: () => restack(widget, -1) }, 'Send back'),
    h('button', { type: 'button', class: 'small danger', onclick: deleteSelected }, 'Delete')));

  panel.replaceChildren(h('section', {}, parts));
  updateValueHint();
}

function restack(widget, direction) {
  const ordered = [...state.draft.widgets].sort((a, b) => (a.zOrder ?? 0) - (b.zOrder ?? 0));
  const index = ordered.indexOf(widget);
  const target = index + direction;
  if (target < 0 || target >= ordered.length) return;
  [ordered[index], ordered[target]] = [ordered[target], ordered[index]];
  ordered.forEach((w, i) => { w.zOrder = i; });
  renderOverlays();
  changed();
}

function renderDevicePanel() {
  const device = current();
  if (!device) return;
  const draft = state.draft;
  const brightnessLabel = h('span', {}, String(draft.brightness));

  const upload = h('input', {
    type: 'file', accept: 'image/png,image/jpeg,image/gif,image/webp,image/bmp', hidden: true,
    onchange: async event => {
      const file = event.target.files[0];
      if (!file) return;
      try {
        await api('PUT', `/devices/${encodeURIComponent(device.id)}/background`, file);
        device.hasBackgroundImage = true;
        toast('Background updated.');
        renderDevicePanel();
        refreshPreview();
      } catch (error) {
        toast(error.message, true);
      }
    },
  });

  $('#device-panel').replaceChildren(h('section', {},
    h('h3', {}, 'Display'),
    field('Name', textInput(draft.name, v => { draft.name = v; updateDirty(); }, { maxlength: 80 })),
    h('label', {}, 'Brightness ', brightnessLabel,
      h('input', { type: 'range', min: 0, max: 7, step: 1, value: draft.brightness, oninput: e => { draft.brightness = Number(e.target.value); brightnessLabel.textContent = e.target.value; updateDirty(); } })),
    field('Frames per second', numberInput(draft.targetFps, v => { if (v !== null) { draft.targetFps = clamp(Math.round(v), 1, 30); updateDirty(); } }, { min: 1, max: 30 })),
    h('p', { class: 'muted small' }, 'Brightness and frame rate apply when you save. 1 fps is plenty for clocks and stats.'),
    h('label', {}, 'Background image'),
    h('div', { class: 'row' },
      upload,
      h('button', { type: 'button', class: 'small', onclick: () => upload.click() }, device.hasBackgroundImage ? 'Replace…' : 'Upload…'),
      device.hasBackgroundImage ? h('button', {
        type: 'button', class: 'small danger', onclick: async () => {
          await api('DELETE', `/devices/${encodeURIComponent(device.id)}/background`);
          device.hasBackgroundImage = false;
          renderDevicePanel();
          refreshPreview();
        },
      }, 'Remove') : null),
    h('p', { class: 'muted small' }, `Scaled to ${device.screenWidth}×${device.screenHeight} on upload.`),
    h('p', { class: 'muted small' }, 'ID: ', h('code', {}, device.id)),
    device.connected ? null : h('div', { class: 'row' }, h('button', {
      type: 'button', class: 'small danger', onclick: async () => {
        if (!confirm(`Forget "${device.name}"? Its layout is deleted. If you plug it back in it starts over with the default layout.`)) return;
        await api('DELETE', '/devices/' + encodeURIComponent(device.id));
        state.draft = null;
        state.deviceId = null;
        await loadDevices();
        renderEditor();
      },
    }, 'Forget this display'))));
}

// ---------------------------------------------------------------- integrations

const integrationForms = [
  {
    kind: 'pihole', title: 'Pi-hole', note: 'Pi-hole v6. Use an app password (Settings → Web interface / API → App password).',
    fields: [
      { name: 'host', label: 'Host or IP', placeholder: 'pi.hole' },
      { name: 'port', label: 'Port', type: 'number', placeholder: '80' },
      { name: 'useHttps', label: 'Use HTTPS', type: 'checkbox' },
      { name: 'secret', label: 'App password', type: 'password' },
    ],
  },
  {
    kind: 'proxmox', title: 'Proxmox VE', note: 'A read-only user (PVEAuditor role) is enough.',
    fields: [
      { name: 'baseUrl', label: 'URL', placeholder: 'https://pve.lan:8006' },
      { name: 'username', label: 'Username', placeholder: 'monitor' },
      { name: 'realm', label: 'Realm', placeholder: 'pam' },
      { name: 'secret', label: 'Password', type: 'password' },
    ],
  },
  {
    kind: 'unifi', title: 'UniFi', note: 'Needs a local-access-only admin account. For a 2FA account, paste the base32 setup key, not a 6-digit code.',
    fields: [
      { name: 'baseUrl', label: 'URL', placeholder: 'https://192.168.1.1' },
      { name: 'username', label: 'Username' },
      { name: 'site', label: 'Site', placeholder: 'default' },
      { name: 'secret', label: 'Password', type: 'password' },
      { name: 'totpSecret', label: '2FA secret (optional)', type: 'password' },
    ],
  },
];

async function renderIntegrations() {
  let saved = [];
  try { saved = await api('GET', '/integrations'); } catch { return; }
  $('#integrations').replaceChildren(...integrationForms.map(def => integrationCard(def, saved.find(i => i.kind === def.kind))));
}

function integrationCard(def, existing) {
  const values = { ...existing };
  if (def.kind === 'pihole' && existing?.baseUrl) {
    try {
      const url = new URL(existing.baseUrl);
      values.host = url.hostname;
      values.port = url.port || (url.protocol === 'https:' ? 443 : 80);
      values.useHttps = url.protocol === 'https:';
    } catch { /* leave blank */ }
  }

  const status = h('span', { class: 'status', role: 'status' });
  const setStatus = (text, kind) => { status.textContent = text; status.className = 'status' + (kind ? ' ' + kind : ''); };

  const inputs = {};
  const fieldEls = def.fields.map(f => {
    if (f.type === 'checkbox') {
      inputs[f.name] = h('input', { type: 'checkbox', checked: !!values[f.name] });
      return h('label', { class: 'check' }, inputs[f.name], f.label);
    }
    const isSecret = f.type === 'password';
    const hasSaved = f.name === 'secret' ? existing?.hasSecret : f.name === 'totpSecret' ? existing?.hasTotpSecret : false;
    inputs[f.name] = h('input', {
      type: f.type || 'text', autocomplete: isSecret ? 'new-password' : 'off',
      value: isSecret ? '' : values[f.name] ?? '',
      placeholder: isSecret && hasSaved ? '•••••••• (saved, leave blank to keep)' : f.placeholder ?? '',
    });
    return field(f.label, inputs[f.name]);
  });

  const thumbprint = h('input', { type: 'text', value: existing?.pinnedCertificateSha256Thumbprint ?? '', placeholder: 'Only for self-signed HTTPS', spellcheck: false });
  const baseUrlForDetect = () => def.kind === 'pihole'
    ? `${inputs.useHttps.checked ? 'https' : 'http'}://${inputs.host.value.trim()}:${inputs.port.value || (inputs.useHttps.checked ? 443 : 80)}`
    : inputs.baseUrl.value.trim();

  const detect = h('button', {
    type: 'button', class: 'small', onclick: async () => {
      setStatus('Fetching certificate…');
      try {
        const result = await api('POST', '/integrations/detect-certificate', { baseUrl: baseUrlForDetect() });
        if (confirm(`Certificate SHA-256 thumbprint:\n\n${result.thumbprint}\n\nCheck it matches the one your server shows, then press OK to trust it.`)) {
          thumbprint.value = result.thumbprint;
          setStatus('Thumbprint set. Press Test & save to apply it.');
        } else {
          setStatus('');
        }
      } catch (error) {
        setStatus(error.message, 'bad');
      }
    },
  }, 'Detect');

  const form = h('form', {
    class: 'card',
    onsubmit: async event => {
      event.preventDefault();
      setStatus('Testing…');
      const body = { pinnedCertificateSha256Thumbprint: thumbprint.value.trim() || null };
      for (const [name, input] of Object.entries(inputs)) {
        body[name] = input.type === 'checkbox' ? input.checked : input.type === 'number' ? (input.value ? Number(input.value) : null) : input.value.trim() || null;
      }
      if (def.kind === 'pihole' && body.port === null) body.port = body.useHttps ? 443 : 80;
      try {
        const result = await api('PUT', '/integrations/' + def.kind, body);
        setStatus(result.message, 'ok');
        for (const input of Object.values(inputs)) if (input.type === 'password') input.value = '';
        setTimeout(renderIntegrations, 1500);
      } catch (error) {
        setStatus(error.message, 'bad');
      }
    },
  },
  h('div', { class: 'card-head' }, h('h3', {}, def.title), h('span', { class: 'badge' + (existing ? ' ok' : '') }, existing ? 'Configured' : 'Not set up')),
  h('p', { class: 'muted small' }, def.note),
  h('div', { class: 'grid2' }, fieldEls),
  h('div', { class: 'thumb-row' }, field('Pinned certificate (SHA-256)', thumbprint), detect),
  h('div', { class: 'row' },
    h('button', { type: 'submit', class: 'primary' }, 'Test & save'),
    existing ? h('button', {
      type: 'button', class: 'danger', onclick: async () => {
        if (!confirm(`Remove the ${def.title} integration and its saved password?`)) return;
        await api('DELETE', '/integrations/' + def.kind);
        renderIntegrations();
      },
    }, 'Remove') : null,
    status));
  return form;
}

// ---------------------------------------------------------------- settings

$('#password-form').addEventListener('submit', async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const status = $('.status', form);
  const data = Object.fromEntries(new FormData(form));
  if (data.newPassword !== data.repeatPassword) {
    status.textContent = "New passwords don't match.";
    status.className = 'status bad';
    return;
  }
  try {
    await api('POST', '/auth/password', { currentPassword: data.currentPassword, newPassword: data.newPassword });
    form.reset();
    status.textContent = 'Password changed.';
    status.className = 'status ok';
  } catch (error) {
    status.textContent = error.message;
    status.className = 'status bad';
  }
});

// ---------------------------------------------------------------- boot

function clamp(value, min, max) { return Math.min(Math.max(value, min), max); }

(async () => {
  try {
    const status = await api('GET', '/auth/status');
    if (status.authenticated) await showApp();
    else showLogin();
  } catch {
    showLogin();
  }
})();
