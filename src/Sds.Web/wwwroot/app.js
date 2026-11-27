// SDS Console entry point: signs the user in, then builds the navigation, routes between
// views with the URL hash and keeps the connection indicator up to date.
//
// Steps:
//   1. Register the views (one module per area of the server).
//   2. Sign in: the console starts on the sign-in screen and only opens for an account in the
//      Admin or Reader role. Two ways: user name and password (POST /api/auth/login, which
//      sets an HttpOnly session cookie; works in any browser), or Windows sign-in (the browser
//      sends Windows credentials, GET /api/auth/me). A password session that is still valid
//      is resumed on reload (GET /api/auth/session). The page never stores a password.
//      Before that, a browser that has not accepted the API's self-signed certificate yet is
//      sent to accept it and comes straight back (ensureApiTrusted in lib.js).
//   3. Build the sidebar from the registry, grouped by area.
//   4. Render the view named by location.hash, cleaning up the previous one (timers).
//   5. Show who is signed in and which server we talk to; sign out returns to step 2.

import { API, ApiError, api, ensureApiTrusted, h } from './lib.js';
import { overviewView, settingsView, multicastView, jobsView, clientLogsView } from './views-server.js';
import { imageGroupsView, imagesView } from './views-images.js';
import { driversView, driverGroupsView } from './views-drivers.js';
import { computersView, pendingView } from './views-computers.js';
import { simulatorView, consoleView } from './views-tools.js';

// Step 1: view registry, in menu order.
const views = [
  overviewView, settingsView, jobsView, multicastView,
  imageGroupsView, imagesView,
  driversView, driverGroupsView,
  computersView, pendingView, clientLogsView,
  simulatorView, consoleView,
];

const signin = document.getElementById('signin');
const layout = document.getElementById('layout');
const sidebar = document.getElementById('sidebar');
const content = document.getElementById('content');
const status = document.getElementById('apiStatus');
const account = document.getElementById('account');
const navToggle = document.getElementById('navToggle');

/** Account returned by /api/auth/me while signed in, null otherwise. */
let currentUser = null;
let cleanup = null;

// ---------------------------------------------------------------------------------------
// Step 2: sign in
// ---------------------------------------------------------------------------------------

/** Shows the sign-in screen, optionally with the outcome of the previous attempt. */
function showSignIn(message, kind = 'error') {
  currentUser = null;
  layout.hidden = true;
  account.hidden = true;
  navToggle.hidden = true;
  status.className = 'api-status';
  status.textContent = API;
  status.title = '';
  document.title = 'Sign in | SDS Console';

  // Password sign-in: works in any browser. The server checks the Windows account and issues
  // an HttpOnly session cookie; the password is not kept by the page.
  const userName = h('input', { type: 'text', name: 'username', autocomplete: 'username', placeholder: 'DOMAIN\\user', required: true });
  const password = h('input', { type: 'password', name: 'password', autocomplete: 'current-password', required: true });
  const submit = h('button', { class: 'primary', type: 'submit' }, 'Sign in');
  const passwordForm = h('form', { class: 'signin-form', onsubmit: (e) => { e.preventDefault(); signInWithPassword(userName, password, submit); } },
    h('label', { class: 'field' }, 'User name', userName),
    h('label', { class: 'field' }, 'Password', password),
    submit);

  // Windows sign-in: the browser sends the Windows credentials it already has (Edge, Chrome,
  // or Firefox once the server is in its trusted lists).
  const windows = h('button', { type: 'button', class: 'signin-windows', onclick: () => signInWithWindows(windows) }, 'Sign in with Windows');

  signin.replaceChildren(h('div', { class: 'signin-card' },
    h('h1', {}, 'Sign in to SDS'),
    h('p', { class: 'lead' }, 'Use a Windows account that is a member of Administrators, SDS Administrators or SDS Operators on the SDS server.'),
    message ? h('div', { class: `signin-msg ${kind}`, role: 'alert' }, message) : null,
    passwordForm,
    h('div', { class: 'signin-or' }, 'or'),
    windows,
    h('p', { class: 'hint' },
      `Server: ${API}. For a domain account type DOMAIN\\user or user@domain; a bare name is a local account of the server. ` +
      '"Sign in with Windows" uses the account you are signed in to Windows with, if your browser sends it.')));
  signin.hidden = false;
  userName.focus();
}

/** Checks a user name and password with the server and opens the console if authorized. */
async function signInWithPassword(userName, password, submit) {
  submit.disabled = true;
  submit.textContent = 'Signing in...';
  try {
    const me = await api('POST', '/api/auth/login', { body: { userName: userName.value.trim(), password: password.value } });
    password.value = '';
    openConsole(me);
  } catch (e) {
    const name = userName.value;
    showSignIn(e.message || String(e));
    // Keep the user name so only the password has to be typed again.
    const again = signin.querySelector('input[name="username"]');
    again.value = name;
    signin.querySelector('input[name="password"]').focus();
  }
}

/** Asks the server who the browser is signed in to Windows as and opens the console if authorized. */
async function signInWithWindows(button) {
  button.disabled = true;
  button.textContent = 'Signing in...';

  let me;
  try {
    me = await api('GET', '/api/auth/me');
  } catch (e) {
    if (e instanceof ApiError && e.status === 401) {
      showSignIn('The browser did not send Windows credentials to the SDS server. Sign in with your user name and password instead.');
    } else {
      showSignIn(e.message || String(e));
    }
    return;
  }

  if (!me || !me.isReader) {
    showSignIn(`Signed in as ${me?.name || 'an unknown account'}, but this account is not authorized to manage SDS. ` +
      'Sign in with an authorized account, or ask an administrator to add this one to SDS Administrators or SDS Operators.');
    return;
  }

  openConsole(me);
}

/** Leaves the console and returns to the sign-in screen. */
async function signOut() {
  const method = currentUser?.method;
  leaveConsole();
  if (method === 'Password') {
    try {
      await api('POST', '/api/auth/logout');
    } catch {
      // The session cookie expires on its own; the sign-in screen is shown either way.
    }
    showSignIn('Signed out.', 'info');
  } else {
    // Windows credentials belong to the browser session; the page cannot discard them.
    showSignIn('Signed out. Close the browser to end the Windows session as well.', 'info');
  }
}

/** Stops the current view and clears the console. */
function leaveConsole() {
  if (typeof cleanup === 'function') {
    cleanup();
  }
  cleanup = null;
  currentUser = null;
  content.replaceChildren();
}

// A 401 while signed in means the session ended (expired, or the server restarted with new keys).
window.addEventListener('sds:unauthorized', () => {
  if (currentUser) {
    leaveConsole();
    showSignIn('Your session has ended. Sign in again.', 'info');
  }
});

/**
 * Resumes a password session that is still valid (for example after a page reload). First,
 * when the API uses a certificate this browser has not accepted yet, the browser is sent to
 * accept it and comes back here (ensureApiTrusted).
 */
async function resumeSession() {
  await ensureApiTrusted();
  try {
    const session = await api('GET', '/api/auth/session');
    if (session?.signedIn && session.isReader) {
      openConsole(session);
      return;
    }
  } catch {
    // Server unreachable: the sign-in screen reports it on the first attempt.
  }
  showSignIn();
}

// ---------------------------------------------------------------------------------------
// Step 3: sidebar, with a heading whenever the group changes (built once)
// ---------------------------------------------------------------------------------------
let lastGroup = null;
for (const view of views) {
  if (view.group !== lastGroup) {
    sidebar.append(h('div', { class: 'nav-group' }, view.group));
    lastGroup = view.group;
  }
  sidebar.append(h('a', { href: `#${view.id}`, 'data-view': view.id }, view.title));
}

navToggle.addEventListener('click', () => sidebar.classList.toggle('open'));
document.getElementById('signOut').addEventListener('click', signOut);

// ---------------------------------------------------------------------------------------
// Step 4: routing (only while signed in)
// ---------------------------------------------------------------------------------------
async function route() {
  if (!currentUser) {
    return;
  }

  const id = location.hash.slice(1) || views[0].id;
  const view = views.find((v) => v.id === id) ?? views[0];

  if (typeof cleanup === 'function') {
    cleanup();
  }
  cleanup = null;

  for (const a of sidebar.querySelectorAll('a')) {
    a.classList.toggle('active', a.dataset.view === view.id);
  }
  sidebar.classList.remove('open');
  document.title = `${view.title} | SDS Console`;

  // A fresh container per render, so a slow view that finishes after the user navigated away
  // writes into a detached node instead of the page.
  const container = h('div', {});
  content.replaceChildren(container);
  try {
    cleanup = await view.render(container);
  } catch (e) {
    container.append(h('div', { class: 'toast error' }, e.message || String(e)));
  }
}

window.addEventListener('hashchange', route);

// ---------------------------------------------------------------------------------------
// Step 5: signed in console and connection indicator
// ---------------------------------------------------------------------------------------
function openConsole(me) {
  currentUser = me;
  signin.hidden = true;
  signin.replaceChildren();
  layout.hidden = false;
  navToggle.hidden = false;

  account.hidden = false;
  document.getElementById('accountName').textContent = `${me.name} (${me.role === 'Admin' ? 'Admin' : 'Read only'})`;

  checkConnection();
  route();
}

async function checkConnection() {
  try {
    const info = await api('GET', '/api/server');
    status.className = 'api-status ok';
    status.textContent = `${info.server} (${API})`;
    status.title = '';
  } catch (e) {
    status.className = 'api-status bad';
    status.textContent = `${API}: ${e.status === 401 || e.status === 403 ? e.message.split('.')[0] : 'unreachable'}`;
    status.title = e.message;
  }
}

resumeSession();
