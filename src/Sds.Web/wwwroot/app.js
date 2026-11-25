// SDS Console entry point: signs the user in, then builds the navigation, routes between
// views with the URL hash and keeps the connection indicator up to date.
//
// Steps:
//   1. Register the views (one module per area of the server).
//   2. Sign in: the console starts on the sign-in screen and only opens for an account that
//      GET /api/auth/me reports as Admin or Reader. The browser supplies the Windows
//      credentials (Negotiate); the page never sees or stores a password.
//   3. Build the sidebar from the registry, grouped by area.
//   4. Render the view named by location.hash, cleaning up the previous one (timers).
//   5. Show who is signed in and which server we talk to; sign out returns to step 2.

import { API, ApiError, api, h } from './lib.js';
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

  const button = h('button', { class: 'primary', type: 'button', onclick: () => signIn(button) }, 'Sign in with Windows');
  signin.replaceChildren(h('div', { class: 'signin-card' },
    h('h1', {}, 'Sign in to SDS'),
    h('p', { class: 'lead' }, 'Use a Windows account that is a member of Administrators, SDS Administrators or SDS Operators on the SDS server.'),
    message ? h('div', { class: `signin-msg ${kind}`, role: 'alert' }, message) : null,
    button,
    h('p', { class: 'hint' },
      `Server: ${API}. Your browser sends your Windows sign in; if it asks for credentials, enter an authorized account there. ` +
      'If nothing is sent, add the server to the browser\'s intranet zone.')));
  signin.hidden = false;
  button.focus();
}

/** Asks the server who the browser is signed in as and opens the console if authorized. */
async function signIn(button) {
  button.disabled = true;
  button.textContent = 'Signing in...';

  let me;
  try {
    me = await api('GET', '/api/auth/me');
  } catch (e) {
    if (e instanceof ApiError && e.status === 401) {
      showSignIn('Not signed in. The browser did not send Windows credentials to the SDS server.');
    } else {
      showSignIn(e.message || String(e));
    }
    return;
  }

  if (!me || !me.isReader) {
    showSignIn(`Signed in as ${me?.name || 'an unknown account'}, but this account is not authorized to manage SDS. ` +
      'Sign in to Windows with an authorized account, or ask an administrator to add this one to SDS Administrators or SDS Operators.');
    return;
  }

  openConsole(me);
}

/** Leaves the console and returns to the sign-in screen. */
function signOut() {
  if (typeof cleanup === 'function') {
    cleanup();
  }
  cleanup = null;
  content.replaceChildren();
  // The Windows credentials belong to the browser session; the page cannot discard them.
  showSignIn('Signed out. Close the browser to end the Windows session as well.', 'info');
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

showSignIn();
