// Shared helpers of the SDS Console: API access, uploads, background job tracking and the
// small set of UI building blocks (elements, forms, tables, dialogs, toasts) every view uses.
//
// Security note: every piece of text coming from the server is inserted with textContent
// (through h()), never as HTML, so names of images, drivers or computers cannot inject markup.

// ---------------------------------------------------------------------------------------
// Section 1: API access
// ---------------------------------------------------------------------------------------

/** Base address of the SDS REST API, provided by /config.js (no trailing slash). */
export const API = String(window.SDS_API || 'http://localhost:8350').replace(/\/+$/, '');

/** sessionStorage key: when the page last sent the browser to accept the API certificate. */
const TRUST_KEY = 'sds.trustAttempt';
/** Do not send the browser again within this time, so an API that is down cannot cause a loop. */
const TRUST_RETRY_MS = 60_000;

function readTrustAttempt() {
  try { return Number(sessionStorage.getItem(TRUST_KEY)) || 0; } catch { return 0; }
}

function writeTrustAttempt(value) {
  try {
    if (value) { sessionStorage.setItem(TRUST_KEY, String(value)); } else { sessionStorage.removeItem(TRUST_KEY); }
  } catch {
    // Storage blocked (private mode): the retry limit then only lasts for this page load.
  }
}

/**
 * Makes sure the browser accepts the API's certificate, before the sign-in page talks to it.
 *
 * With a self-signed certificate on the API (HTTPS), fetch() fails without any prompt until the
 * browser has accepted that certificate, and a page cannot open the prompt for a fetch. So when
 * the API cannot be reached over HTTPS, the browser is sent to the API's /api/trust address as
 * an ordinary page visit: it shows its certificate warning there, and once the user accepts,
 * the API sends the browser straight back to this page. Over HTTP, or when the API answers,
 * nothing happens. If the API still cannot be reached after one round trip within a minute
 * (server down, firewall), the page stays put and the sign-in screen reports the error.
 */
export async function ensureApiTrusted() {
  if (!API.startsWith('https:')) {
    return;
  }

  try {
    await fetch(`${API}/api/trust`, { credentials: 'omit' });
    writeTrustAttempt(0);
    return;
  } catch {
    // Untrusted certificate, or the API is unreachable: the browser does not say which.
  }

  const now = Date.now();
  if (now - readTrustAttempt() < TRUST_RETRY_MS) {
    return;
  }

  writeTrustAttempt(now);
  location.replace(`${API}/api/trust?return=${encodeURIComponent(location.href)}`);
  // Navigation is under way; keep the caller waiting instead of rendering the sign-in screen.
  await new Promise(() => {});
}

/** Error raised for a failed API call; message is ready to show to the user. */
export class ApiError extends Error {
  constructor(status, message, problem) {
    super(message);
    this.status = status;
    this.problem = problem;
  }
}

/**
 * Calls the API. Objects are sent as JSON, strings as text (contentType defaults to
 * text/plain). Returns parsed JSON, text for non JSON answers, or null for 204.
 * Credentials are always included so the browser sends the password session cookie or
 * performs Windows Negotiate sign in.
 */
export async function api(method, path, { body, contentType, text = false } = {}) {
  // The custom header proves the call comes from a page the API's CORS policy allows; the
  // server requires it for writes made with a password session cookie (CSRF guard).
  const headers = { 'X-SDS-Request': '1' };
  let payload;
  if (body !== undefined && body !== null) {
    if (typeof body === 'string' || body instanceof Blob) {
      payload = body;
      headers['Content-Type'] = contentType || 'text/plain';
    } else {
      payload = JSON.stringify(body);
      headers['Content-Type'] = 'application/json';
    }
  }

  let response;
  try {
    response = await fetch(API + path, { method, headers, body: payload, credentials: 'include' });
  } catch (e) {
    // fetch only throws for network or CORS failures; the browser hides which one.
    throw new ApiError(0, `Cannot reach ${API} (${e.message}). Check that the SDS server is running and that Sds:CorsOrigins lists ${location.origin}.`);
  }

  if (!response.ok) {
    throw await toApiError(response);
  }

  if (response.status === 204) {
    return null;
  }

  const type = response.headers.get('Content-Type') || '';
  return text || !type.includes('json') ? response.text() : response.json();
}

/** Like api(), but returns null instead of failing when the resource does not exist (404). */
export async function apiOptional(method, path, options) {
  try {
    return await api(method, path, options);
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) {
      return null;
    }
    throw e;
  }
}

/** Builds an ApiError from an error response (ProblemDetails when available). */
async function toApiError(response) {
  let problem = null;
  try {
    problem = await response.json();
  } catch {
    // Not JSON: keep the status line only.
  }

  if (response.status === 401) {
    // Lets the console return to the sign-in screen when a session ends.
    window.dispatchEvent(new CustomEvent('sds:unauthorized'));
    return new ApiError(401, problem?.detail || 'Not signed in (401). Sign in again.', problem);
  }
  if (response.status === 403) {
    return new ApiError(403, problem?.detail || 'Access denied (403). This needs membership of Administrators or SDS Administrators (SDS Operators for read only).', problem);
  }

  let message = `${response.status} ${response.statusText}`;
  if (problem) {
    message = [problem.title, problem.detail].filter(Boolean).join(': ') || message;
    if (problem.errors) {
      // Model validation errors: { field: [messages] }.
      const lines = Object.entries(problem.errors).map(([k, v]) => `${k}: ${[].concat(v).join(' ')}`);
      message += '\n' + lines.join('\n');
    }
  }
  return new ApiError(response.status, message, problem);
}

/** Encodes one path segment. */
export const seg = (value) => encodeURIComponent(String(value));

/** Builds a query string from an object, skipping empty values. */
export function query(params) {
  const q = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (v !== null && v !== undefined && v !== '') {
      q.set(k, String(v));
    }
  }
  const s = q.toString();
  return s ? '?' + s : '';
}

/**
 * Uploads a file to POST /api/uploads as the raw body and returns { uploadId, fileName, size }.
 * XMLHttpRequest is used instead of fetch because it reports upload progress.
 */
export function upload(file, onProgress) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', `${API}/api/uploads${query({ fileName: file.name })}`);
    xhr.withCredentials = true;
    xhr.setRequestHeader('Content-Type', 'application/octet-stream');
    xhr.setRequestHeader('X-SDS-Request', '1');
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable && onProgress) {
        onProgress(Math.round((e.loaded / e.total) * 100));
      }
    };
    xhr.onload = () => {
      if (xhr.status >= 200 && xhr.status < 300) {
        resolve(JSON.parse(xhr.responseText));
      } else {
        reject(new ApiError(xhr.status, `Upload failed: ${xhr.status} ${xhr.statusText} ${xhr.responseText}`));
      }
    };
    xhr.onerror = () => reject(new ApiError(0, 'Upload failed: network or CORS error.'));
    xhr.send(file);
  });
}

// ---------------------------------------------------------------------------------------
// Section 2: background jobs
// ---------------------------------------------------------------------------------------

/** Download address of the file produced by a staged export job. */
export const jobFileUrl = (jobId) => `${API}/api/jobs/${seg(jobId)}/file`;

/** Parses the JSON result of a job, or returns null. */
export function jobResult(job) {
  try {
    return job.resultJson ? JSON.parse(job.resultJson) : null;
  } catch {
    return null;
  }
}

const trackedJobs = new Set();

/**
 * Shows a job in the tray and polls it until it finishes. onDone(job) runs once, for both
 * success and failure, so views can refresh their tables.
 */
export function trackJob(job, { onDone } = {}) {
  if (!job || !job.id || trackedJobs.has(job.id)) {
    return;
  }
  trackedJobs.add(job.id);

  const bar = h('div', { style: 'width:0%' });
  const state = h('span', {}, job.status);
  const card = h('div', { class: 'job-card' },
    h('div', { class: 't' }, h('span', { title: job.description || job.type }, job.description || job.type), state),
    h('div', { class: 'progress' }, bar));
  document.getElementById('jobTray').append(card);

  const poll = async () => {
    let current;
    try {
      current = await api('GET', `/api/jobs/${seg(job.id)}`);
    } catch (e) {
      state.textContent = 'poll error';
      setTimeout(poll, 3000);
      return;
    }

    bar.style.width = `${current.progress ?? 0}%`;
    state.textContent = current.status === 'Running' ? `${current.progress ?? 0}%` : current.status;

    if (current.status === 'Succeeded' || current.status === 'Failed') {
      trackedJobs.delete(job.id);
      if (current.status === 'Succeeded') {
        toast(`${current.description || current.type}: done`, 'ok');
        setTimeout(() => card.remove(), 4000);
      } else {
        toast(`${current.description || current.type} failed:\n${current.error || 'unknown error'}`, 'error', 15000);
        card.append(h('div', { class: 'hint' }, current.error || ''), h('button', { class: 'small', onclick: () => card.remove() }, 'Dismiss'));
      }
      onDone?.(current);
      return;
    }

    setTimeout(poll, 1000);
  };

  poll();
}

// ---------------------------------------------------------------------------------------
// Section 3: DOM building blocks
// ---------------------------------------------------------------------------------------

/**
 * Creates an element. attrs: class, style, any attribute, and on<event> handlers (functions).
 * Children: strings and numbers become text nodes; arrays are flattened; null is skipped.
 */
export function h(tag, attrs = {}, ...children) {
  const el = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs || {})) {
    if (v === null || v === undefined || v === false) {
      continue;
    }
    if (k.startsWith('on') && typeof v === 'function') {
      el.addEventListener(k.slice(2), v);
    } else if (k === 'value') {
      el.value = v;
    } else if (k === 'checked') {
      el.checked = !!v;
    } else {
      el.setAttribute(k, v === true ? '' : String(v));
    }
  }
  append(el, children);
  return el;
}

function append(el, children) {
  for (const child of children) {
    if (child === null || child === undefined || child === false) {
      continue;
    }
    if (Array.isArray(child)) {
      append(el, child);
    } else if (child instanceof Node) {
      el.append(child);
    } else {
      el.append(document.createTextNode(String(child)));
    }
  }
}

/** Shows a notification. kind: info, ok or error. */
export function toast(message, kind = 'info', timeout = 6000) {
  const el = h('div', { class: `toast ${kind}`, onclick: () => el.remove() }, message);
  document.getElementById('toasts').append(el);
  setTimeout(() => el.remove(), timeout);
}

/** Runs an async action and turns failures into an error toast. Returns the result or undefined. */
export async function attempt(action) {
  try {
    return await action();
  } catch (e) {
    toast(e.message || String(e), 'error', 12000);
    return undefined;
  }
}

/** Formats a byte count. */
export function fmtBytes(n) {
  if (n === null || n === undefined) return '';
  const units = ['B', 'KB', 'MB', 'GB', 'TB'];
  let i = 0;
  let v = Number(n);
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${v.toFixed(i === 0 ? 0 : 1)} ${units[i]}`;
}

/** Formats an ISO date in local time. */
export function fmtDate(s) {
  if (!s) return '';
  const d = new Date(s);
  return isNaN(d) ? String(s) : d.toLocaleString();
}

/** Small colored label. kind: ok, bad, warn or empty. */
export const badge = (text, kind = '') => h('span', { class: `badge ${kind}` }, text);

/** Pretty JSON block. */
export const jsonBlock = (value) => h('pre', { class: 'out' }, typeof value === 'string' ? value : JSON.stringify(value, null, 2));

/**
 * Renders a table. columns: [{ label, value: row => string | Node }]. actions: row => [buttons].
 */
export function table(columns, rows, { actions, empty = 'Nothing here yet.' } = {}) {
  if (!rows || rows.length === 0) {
    return h('div', { class: 'table-wrap' }, h('div', { class: 'empty' }, empty));
  }

  const head = h('tr', {}, columns.map((c) => h('th', {}, c.label)), actions ? h('th', {}, 'Actions') : null);
  const body = rows.map((row) => h('tr', {},
    columns.map((c) => h('td', { class: c.class }, c.value(row))),
    actions ? h('td', { class: 'actions' }, actions(row)) : null));

  return h('div', { class: 'table-wrap' }, h('table', {}, h('thead', {}, head), h('tbody', {}, body)));
}

/** Button helper. */
export const button = (label, onclick, cls = 'small') => h('button', { class: cls, type: 'button', onclick }, label);

// ---------------------------------------------------------------------------------------
// Section 4: forms
// ---------------------------------------------------------------------------------------

/**
 * Builds one form field. spec: { name, label, type, value, options, placeholder, hint, wide }.
 * Types: text, number, checkbox, select, textarea, datetime, file.
 * get() returns null for empty text, numbers as Number, checkboxes as boolean.
 */
export function field(spec) {
  const type = spec.type || 'text';
  let input;

  if (type === 'select') {
    input = h('select', {},
      (spec.options || []).map((o) => {
        const opt = typeof o === 'object' ? o : { value: o, label: o === '' ? '(none)' : o };
        return h('option', { value: opt.value }, opt.label);
      }));
    // Without a preset value keep the browser default (first option); assigning a value
    // that matches no option would leave nothing selected.
    if (spec.value !== undefined && spec.value !== null) {
      input.value = String(spec.value);
    }
  } else if (type === 'textarea') {
    input = h('textarea', { placeholder: spec.placeholder, rows: spec.rows || 6 });
    input.value = spec.value ?? '';
  } else if (type === 'checkbox') {
    input = h('input', { type: 'checkbox', checked: !!spec.value });
  } else if (type === 'file') {
    input = h('input', { type: 'file', accept: spec.accept });
  } else if (type === 'datetime') {
    input = h('input', { type: 'datetime-local' });
    if (spec.value) {
      // datetime-local expects local "YYYY-MM-DDTHH:MM".
      const d = new Date(spec.value);
      input.value = new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    }
  } else {
    input = h('input', { type: type === 'number' ? 'number' : 'text', placeholder: spec.placeholder });
    input.value = spec.value ?? '';
  }

  const el = type === 'checkbox'
    ? h('label', { class: `check ${spec.wide ? 'wide' : ''}` }, input, spec.label)
    : h('label', { class: `field ${spec.wide ? 'wide' : ''}` }, spec.label, input, spec.hint ? h('span', { class: 'hint' }, spec.hint) : null);

  const get = () => {
    switch (type) {
      case 'checkbox': return input.checked;
      case 'number': return input.value === '' ? null : Number(input.value);
      case 'file': return input.files?.[0] ?? null;
      case 'datetime': return input.value ? new Date(input.value).toISOString() : null;
      case 'textarea': return input.value.trim() === '' ? null : input.value;
      default: return input.value.trim() === '' ? null : input.value.trim();
    }
  };

  return { el, input, get, spec };
}

/** Builds a grid of fields; values() returns { name: value }. */
export function form(specs) {
  const fields = specs.map(field);
  const el = h('div', { class: 'form-grid' }, fields.map((f) => f.el));
  const values = () => Object.fromEntries(fields.map((f) => [f.spec.name, f.get()]));
  const get = (name) => fields.find((f) => f.spec.name === name);
  return { el, fields, values, get };
}

// ---------------------------------------------------------------------------------------
// Section 5: dialog
// ---------------------------------------------------------------------------------------

/**
 * Opens the shared dialog. onOk may be async; returning false keeps the dialog open, and a
 * thrown error is shown as a toast while the dialog stays open so the user can fix input.
 * Resolves with the onOk result, or undefined when cancelled.
 */
export function openDialog({ title, body, okText = 'OK', cancelText = 'Cancel', onOk, wide = false }) {
  const dlg = document.getElementById('dialog');
  dlg.replaceChildren();
  dlg.style.width = wide ? 'min(1000px, calc(100vw - 32px))' : '';

  return new Promise((resolve) => {
    const ok = h('button', { class: 'primary', type: 'button' }, okText);
    const cancel = h('button', { type: 'button' }, cancelText);

    const close = (value) => {
      dlg.close();
      resolve(value);
    };

    ok.addEventListener('click', async () => {
      ok.disabled = cancel.disabled = true;
      try {
        const result = onOk ? await onOk() : true;
        if (result !== false) {
          close(result);
        }
      } catch (e) {
        toast(e.message || String(e), 'error', 12000);
      } finally {
        ok.disabled = cancel.disabled = false;
      }
    });
    cancel.addEventListener('click', () => close(undefined));
    dlg.onclose = () => resolve(undefined);

    dlg.append(
      h('div', { class: 'dlg-head' }, title),
      h('div', { class: 'dlg-body' }, body),
      h('div', { class: 'dlg-foot' }, cancelText ? cancel : null, okText ? ok : null));
    dlg.showModal();
  });
}

/** Yes/no confirmation. */
export async function confirmDialog(title, message, okText = 'Delete') {
  return (await openDialog({ title, body: h('p', {}, message), okText, onOk: () => true })) === true;
}

/** Read only dialog showing a value as JSON or text. */
export function showDialog(title, value) {
  return openDialog({ title, body: value instanceof Node ? value : jsonBlock(value), okText: 'Close', cancelText: null, wide: true });
}
