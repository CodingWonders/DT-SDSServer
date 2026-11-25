// SDS Console entry point: builds the navigation, routes between views with the URL hash
// and keeps the connection indicator up to date.
//
// Steps:
//   1. Register the views (one module per area of the server).
//   2. Build the sidebar from the registry, grouped by area.
//   3. Render the view named by location.hash, cleaning up the previous one (timers).
//   4. Check the API once to show who we talk to and whether sign in works.

import { API, api, h } from './lib.js';
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

const sidebar = document.getElementById('sidebar');
const content = document.getElementById('content');
let cleanup = null;

// Step 2: sidebar, with a heading whenever the group changes.
let lastGroup = null;
for (const view of views) {
  if (view.group !== lastGroup) {
    sidebar.append(h('div', { class: 'nav-group' }, view.group));
    lastGroup = view.group;
  }
  sidebar.append(h('a', { href: `#${view.id}`, 'data-view': view.id }, view.title));
}

document.getElementById('navToggle').addEventListener('click', () => sidebar.classList.toggle('open'));

// Step 3: routing.
async function route() {
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

// Step 4: connection indicator.
async function checkConnection() {
  const status = document.getElementById('apiStatus');
  try {
    const info = await api('GET', '/api/server');
    status.className = 'api-status ok';
    status.textContent = `${info.server} (${API})`;
  } catch (e) {
    status.className = 'api-status bad';
    status.textContent = `${API}: ${e.status === 401 || e.status === 403 ? e.message.split('.')[0] : 'unreachable'}`;
    status.title = e.message;
  }
}

checkConnection();
route();
