// Server level views: overview, policy settings, server client unattend, multicast,
// background jobs and client logs.

import {
  api, query, trackJob, jobResult, jobFileUrl,
  h, toast, attempt, fmtDate, badge, table, button, form, field,
  openDialog, confirmDialog, showDialog, jsonBlock,
} from './lib.js';
import { unattendDialog } from './views-images.js';

// ---------------------------------------------------------------------------------------
// Section 1: overview
// ---------------------------------------------------------------------------------------

export const overviewView = {
  id: 'overview',
  title: 'Overview',
  group: 'Server',
  async render(root) {
    const info = await attempt(() => api('GET', '/api/server'));
    root.append(h('h1', {}, 'Overview'));
    if (!info) {
      root.append(h('p', { class: 'lead' }, 'The server could not be reached. See the error message.'));
      return;
    }

    const c = info.counts;
    const tile = (num, lbl, view) => h('a', { class: 'tile', href: `#${view}`, style: 'text-decoration:none;color:inherit' }, h('div', { class: 'num' }, num), h('div', { class: 'lbl' }, lbl));
    const report = h('div', {});

    root.append(
      h('p', { class: 'lead' }, `Connected to ${info.server}, SDS ${info.version ?? ''}`),
      h('div', { class: 'tiles' },
        tile(c.imageGroups, 'Image groups', 'image-groups'),
        tile(c.images, 'Install images', 'images'),
        tile(c.driverPackages, 'Driver packages', 'drivers'),
        tile(c.driverGroups, 'Driver groups', 'driver-groups'),
        tile(c.computers, 'Known computers', 'computers'),
        tile(c.pendingDevices, 'Pending devices', 'pending'),
        tile(c.multicastTransmissions, 'Multicast', 'multicast')),
      h('div', { class: 'panel' },
        h('dl', { class: 'kv' },
          h('dt', {}, 'Root'), h('dd', { class: 'mono' }, info.root),
          h('dt', {}, 'Images share'), h('dd', { class: 'mono' }, info.shares?.images ?? ''),
          h('dt', {}, 'Drivers share'), h('dd', { class: 'mono' }, info.shares?.drivers ?? ''),
          h('dt', {}, 'PXECORE'), h('dd', { class: 'mono' }, `${info.folders.pxeCore} (not shared, SharpPXE)`),
          h('dt', {}, 'Data directory'), h('dd', { class: 'mono' }, info.dataDirectory),
          h('dt', {}, 'Active Directory'), h('dd', {}, info.activeDirectory ? 'Enabled' : 'Disabled (local database only)'))),
      h('div', { class: 'toolbar' },
        button('Run consistency check', async () => {
          const r = await attempt(() => api('POST', '/api/server/reconcile'));
          if (r) report.replaceChildren(h('h2', {}, 'Consistency report'), jsonBlock(r));
        }, 'primary')),
      report);
  },
};

// ---------------------------------------------------------------------------------------
// Section 2: settings
// ---------------------------------------------------------------------------------------

/** Settings form layout, grouped like the WDS server property tabs. */
const SETTINGS = [
  ['Client response', [
    { name: 'responsePolicy', label: 'Response policy', type: 'select', options: ['RespondToNone', 'KnownOnly', 'All'] },
    { name: 'requireApprovalForUnknown', label: 'Require approval for unknown clients', type: 'checkbox' },
    { name: 'pendingPollIntervalSeconds', label: 'Pending poll interval (s)', type: 'number' },
    { name: 'pendingMaxRetries', label: 'Pending max retries', type: 'number' },
    { name: 'pendingRecordRetentionDays', label: 'Keep decided records (days)', type: 'number' },
    { name: 'pendingMessage', label: 'Pending message', wide: true },
  ]],
  ['Naming and domain', [
    { name: 'namingPolicy', label: 'Naming policy', hint: '%First %Last %Username %MAC %n# %0n#' },
    { name: 'accountLocation', label: 'Computer account location', type: 'select', options: ['ServerDomain', 'UserDomain', 'UserOrganizationalUnit', 'Specific'] },
    { name: 'domain', label: 'Domain' },
    { name: 'organizationalUnit', label: 'OU (for Specific)' },
    { name: 'joinDomain', label: 'Join clients to the domain', type: 'checkbox' },
    { name: 'defaultJoinRights', label: 'Default join rights', type: 'select', options: ['JoinOnly', 'Full'] },
    { name: 'defaultJoinUser', label: 'Default join user' },
    { name: 'defaultBootProgram', label: 'Default boot program' },
  ]],
  ['Client logging', [
    { name: 'clientLoggingEnabled', label: 'Client logging enabled', type: 'checkbox' },
    { name: 'clientLoggingLevel', label: 'Level', type: 'select', options: ['None', 'Errors', 'Warnings', 'Info'] },
    { name: 'clientLogRetentionDays', label: 'Retention (days)', type: 'number' },
  ]],
  ['Multicast', [
    { name: 'multicastAddressStart', label: 'First address' },
    { name: 'multicastAddressEnd', label: 'Last address' },
    { name: 'multicastPortStart', label: 'First UDP port', type: 'number' },
    { name: 'multicastPortEnd', label: 'Last UDP port', type: 'number' },
    { name: 'multicastTransferMode', label: 'Transfer mode', type: 'select', options: ['SingleSession', 'TwoStreams', 'ThreeStreams'] },
    { name: 'multicastSlowClientThresholdKbps', label: 'Slow client threshold (KBps, 0 off)', type: 'number' },
    { name: 'multicastFallbackToUnicast', label: 'Fall back to unicast', type: 'checkbox' },
  ]],
];

export const settingsView = {
  id: 'settings',
  title: 'Settings',
  group: 'Server',
  async render(root) {
    const current = await attempt(() => api('GET', '/api/server/settings'));
    root.append(h('h1', {}, 'Server settings'), h('p', { class: 'lead' }, 'Run time policy stored in the database (WDS server properties).'));
    if (!current) return;

    const forms = SETTINGS.map(([title, specs]) => {
      const f = form(specs.map((s) => ({ ...s, value: current[s.name] })));
      root.append(h('div', { class: 'panel' }, h('h2', { style: 'margin-top:0' }, title), f.el));
      return f;
    });

    root.append(h('div', { class: 'toolbar' }, button('Save settings', async () => {
      // Start from the loaded object so fields this page does not show are kept.
      const merged = { ...current };
      for (const f of forms) {
        for (const [k, v] of Object.entries(f.values())) {
          merged[k] = v;
        }
      }
      const saved = await attempt(() => api('PUT', '/api/server/settings', { body: merged }));
      if (saved) {
        Object.assign(current, saved);
        toast('Settings saved', 'ok');
      }
    }, 'primary')));

    // Server wide WDS client unattend, one per architecture.
    const arch = field({ name: 'arch', label: 'Architecture', type: 'select', options: ['x86', 'amd64', 'arm64'] });
    root.append(h('div', { class: 'panel' },
      h('h2', { style: 'margin-top:0' }, 'Server client unattend'),
      h('p', { class: 'hint' }, 'Default WDS client unattend sent to clients of an architecture that have no device specific one.'),
      h('div', { class: 'toolbar' }, arch.el,
        button('Edit', () => attempt(() => unattendDialog(`Client unattend (${arch.get()})`, `/api/server/client-unattend/${arch.get()}`)), ''))));
  },
};

// ---------------------------------------------------------------------------------------
// Section 3: multicast
// ---------------------------------------------------------------------------------------

export const multicastView = {
  id: 'multicast',
  title: 'Multicast',
  group: 'Server',
  async render(root) {
    const host = h('div', {});
    root.append(
      h('h1', {}, 'Multicast transmissions'),
      h('p', { class: 'lead' }, 'Definitions, state and clients only: SDS does not send multicast traffic itself.'),
      h('div', { class: 'toolbar' }, button('New transmission', () => create(), 'primary'), button('Refresh', () => load(), '')),
      host);

    const load = async () => {
      const list = await attempt(() => api('GET', '/api/multicast'));
      if (!list) return;
      host.replaceChildren(table([
        { label: 'Id', value: (t) => t.id },
        { label: 'Name', value: (t) => t.name },
        { label: 'Image', value: (t) => `${t.imageName ?? ''} (${t.installImageId})` },
        { label: 'Type', value: (t) => t.type },
        { label: 'State', value: (t) => badge(t.state, t.state === 'Active' ? 'ok' : t.state === 'Stopped' ? 'bad' : 'warn') },
        { label: 'Start criteria', value: (t) => [t.startClientThreshold ? `${t.startClientThreshold} clients` : '', t.startTimeUtc ? h('div', {}, fmtDate(t.startTimeUtc)) : null] },
        { label: 'Clients', value: (t) => t.connectedClients },
        { label: 'Activated', value: (t) => fmtDate(t.activatedUtc) },
      ], list, {
        empty: 'No transmissions.',
        actions: (t) => [
          button('Clients', () => clients(t)),
          t.state !== 'Active' ? button('Start', () => attempt(() => api('POST', `/api/multicast/${t.id}/start`)).then(load)) : null,
          t.state === 'Active' ? button('Stop', () => attempt(() => api('POST', `/api/multicast/${t.id}/stop`)).then(load)) : null,
          button('Delete', async () => {
            if (await confirmDialog('Delete transmission', `Delete ${t.name}?`)) {
              await attempt(() => api('DELETE', `/api/multicast/${t.id}`));
              load();
            }
          }),
        ],
      }));
    };

    const create = async () => {
      const images = (await attempt(() => api('GET', '/api/images'))) ?? [];
      if (images.length === 0) {
        toast('A transmission needs an install image; add one first.', 'error');
        return;
      }
      const f = form([
        { name: 'name', label: 'Name' },
        { name: 'installImageId', label: 'Install image', type: 'select', options: images.map((i) => ({ value: String(i.id), label: `${i.groupName} / ${i.name} (${i.architecture})` })) },
        { name: 'type', label: 'Type', type: 'select', options: ['AutoCast', 'ScheduledCast'] },
        { name: 'startClientThreshold', label: 'Start at client count (ScheduledCast)', type: 'number' },
        { name: 'startTimeUtc', label: 'Start time (ScheduledCast)', type: 'datetime' },
      ]);
      openDialog({
        title: 'New multicast transmission', okText: 'Create', body: f.el,
        onOk: async () => {
          const v = f.values();
          await api('POST', '/api/multicast', { body: { ...v, installImageId: Number(v.installImageId) } });
          load();
        },
      });
    };

    const clients = async (t) => {
      const full = await attempt(() => api('GET', `/api/multicast/${t.id}`));
      if (!full) return;
      const body = h('div', {});
      const render = (rows) => body.replaceChildren(table([
        { label: 'Id', value: (c) => c.id },
        { label: 'MAC', value: (c) => c.macAddress ?? '' },
        { label: 'IP', value: (c) => c.ipAddress ?? '' },
        { label: 'Computer', value: (c) => c.computerName ?? '' },
        { label: 'Progress', value: (c) => `${c.percentComplete}%` },
        { label: 'Joined', value: (c) => fmtDate(c.joinedUtc) },
        { label: 'Left', value: (c) => fmtDate(c.leftUtc) },
      ], rows, {
        empty: 'No clients.',
        actions: (c) => (c.leftUtc ? null : button('Disconnect', async () => {
          await attempt(() => api('DELETE', `/api/multicast/${t.id}/clients/${c.id}`));
          render((await api('GET', `/api/multicast/${t.id}`)).clients);
          load();
        })),
      }));
      render(full.clients);
      showDialog(`Clients of ${t.name}`, body);
    };

    await load();
  },
};

// ---------------------------------------------------------------------------------------
// Section 4: jobs
// ---------------------------------------------------------------------------------------

export const jobsView = {
  id: 'jobs',
  title: 'Jobs',
  group: 'Server',
  async render(root) {
    const host = h('div', {});
    const auto = h('input', { type: 'checkbox', checked: true });
    root.append(
      h('h1', {}, 'Background jobs'),
      h('p', { class: 'lead' }, 'Add, export, copy, move, replace, compact and driver import run as jobs.'),
      h('div', { class: 'toolbar' }, button('Refresh', () => load(), ''), h('label', { class: 'check' }, auto, 'Refresh every 2 seconds')),
      host);

    const load = async () => {
      const list = await attempt(() => api('GET', `/api/jobs${query({ take: 100 })}`));
      if (!list) return;
      host.replaceChildren(table([
        { label: 'Created', value: (j) => fmtDate(j.createdUtc) },
        { label: 'Type', value: (j) => j.type },
        { label: 'Description', value: (j) => [j.description ?? '', j.error ? h('div', { class: 'hint' }, j.error) : null] },
        { label: 'Status', value: (j) => badge(j.status, j.status === 'Succeeded' ? 'ok' : j.status === 'Failed' ? 'bad' : 'warn') },
        { label: 'Progress', value: (j) => h('div', { class: 'progress' }, h('div', { style: `width:${j.progress ?? 0}%` })) },
        { label: 'By', value: (j) => j.requestedBy ?? '' },
      ], list, {
        empty: 'No jobs yet.',
        actions: (j) => {
          const r = jobResult(j);
          return [
            button('Details', () => showDialog(`Job ${j.id}`, { ...j, result: r })),
            j.status === 'Succeeded' && j.type === 'ExportImage' && r?.staged
              ? h('a', { class: 'btn small', href: jobFileUrl(j.id), download: '' }, 'Download')
              : null,
          ];
        },
      }));
    };

    await load();
    const timer = setInterval(() => auto.checked && load(), 2000);
    return () => clearInterval(timer);
  },
};

// ---------------------------------------------------------------------------------------
// Section 5: client logs
// ---------------------------------------------------------------------------------------

export const clientLogsView = {
  id: 'client-logs',
  title: 'Client logs',
  group: 'Clients',
  async render(root) {
    const f = form([
      { name: 'from', label: 'From', type: 'datetime' },
      { name: 'to', label: 'To', type: 'datetime' },
      { name: 'mac', label: 'MAC address' },
      { name: 'computer', label: 'Computer' },
      { name: 'level', label: 'Level', type: 'select', options: [{ value: '', label: 'Any' }, 'Errors', 'Warnings', 'Info'] },
      { name: 'take', label: 'Max entries', type: 'number', value: 200 },
    ]);
    const host = h('div', {});
    root.append(
      h('h1', {}, 'Client logs'),
      h('p', { class: 'lead' }, 'Entries sent by deployment clients (WDS client logging), newest first.'),
      h('div', { class: 'panel' }, f.el),
      h('div', { class: 'toolbar' }, button('Query', () => load(), 'primary')),
      host);

    const load = async () => {
      const list = await attempt(() => api('GET', `/api/client-logs${query(f.values())}`));
      if (!list) return;
      host.replaceChildren(table([
        { label: 'Time', value: (e) => fmtDate(e.timestampUtc) },
        { label: 'Level', value: (e) => badge(e.level, e.level === 'Errors' ? 'bad' : e.level === 'Warnings' ? 'warn' : '') },
        { label: 'MAC', value: (e) => h('span', { class: 'mono' }, e.macAddress ?? '') },
        { label: 'IP', value: (e) => e.ipAddress ?? '' },
        { label: 'Computer', value: (e) => e.computerName ?? '' },
        { label: 'Phase', value: (e) => e.phase ?? '' },
        { label: 'Image', value: (e) => e.imageName ?? '' },
        { label: 'Message', value: (e) => e.message },
      ], list, { empty: 'No entries.' }));
    };

    await load();
  },
};
