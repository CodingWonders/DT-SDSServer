// Test tools: a client simulator that walks the deployment client protocol step by step,
// and a raw API console for any request.

import { api, apiOptional, query, API, h, button, form, field, jsonBlock, attempt } from './lib.js';

// ---------------------------------------------------------------------------------------
// Section 1: client simulator
// ---------------------------------------------------------------------------------------

export const simulatorView = {
  id: 'simulator',
  title: 'Client simulator',
  group: 'Tools',
  async render(root) {
    const out = h('div', {});

    /** Runs one call and shows the request and the answer. */
    const run = async (label, action) => {
      const started = new Date().toLocaleTimeString();
      try {
        const result = await action();
        out.prepend(h('div', { class: 'panel' }, h('strong', {}, `${started}  ${label}`), jsonBlock(result ?? '(no content)')));
        return result;
      } catch (e) {
        out.prepend(h('div', { class: 'panel' }, h('strong', { style: 'color:var(--danger)' }, `${started}  ${label} failed`), jsonBlock(e.message)));
        return undefined;
      }
    };

    // Identity of the simulated machine, shared by every step.
    const id = form([
      { name: 'macAddress', label: 'MAC address', value: '00-15-5D-00-00-01' },
      { name: 'smbiosGuid', label: 'SMBIOS GUID', value: '4C4C4544-0042-3010-8058-B2C04F4A3332' },
      { name: 'architecture', label: 'Architecture', type: 'select', options: ['amd64', 'x86', 'arm64'] },
      { name: 'reportedName', label: 'Reported name' },
    ]);

    // Step 1 and 2: registration and polling (anonymous in SDS, as in WDS).
    const requestId = field({ name: 'requestId', label: 'Pending request id', type: 'number' });
    const register = async () => {
      const r = await run('POST /api/client/register', () => api('POST', '/api/client/register', { body: id.values() }));
      if (r?.requestId) requestId.input.value = r.requestId;
    };
    const poll = () => run(`GET /api/client/requests/${requestId.get()}`, () => api('GET', `/api/client/requests/${requestId.get()}`));

    // Step 3: WDS client unattend.
    const unattend = () => {
      const v = id.values();
      return run('GET /api/client/unattend', async () =>
        (await apiOptional('GET', `/api/client/unattend/${v.architecture}${query({ mac: v.macAddress, guid: v.smbiosGuid })}`, { text: true })) ?? 'No client unattend for this client (404).');
    };

    // Step 4: images visible to the signed in user (NTFS user filter applies).
    const imageGroup = field({ name: 'group', label: 'Image group (optional)' });
    const images = () => run('GET /api/client/images', () => api('GET', `/api/client/images${query({ architecture: id.values().architecture, group: imageGroup.get() })}`));

    // Step 5: name generation with the naming policy.
    const naming = form([
      { name: 'username', label: 'Username', placeholder: 'empty: signed in user' },
      { name: 'first', label: 'First name' },
      { name: 'last', label: 'Last name' },
    ]);
    const name = () => run('POST /api/client/name', () => api('POST', '/api/client/name', { body: { ...naming.values(), macAddress: id.values().macAddress } }));

    // Step 6: driver query (driver group evaluation).
    const hw = form([
      { name: 'manufacturer', label: 'Manufacturer', value: 'Contoso' },
      { name: 'model', label: 'Model', value: 'Laptop 5000' },
      { name: 'chassisType', label: 'Chassis type', value: 'Laptop' },
      { name: 'biosVendor', label: 'BIOS vendor' },
      { name: 'biosVersion', label: 'BIOS version' },
      { name: 'installImageId', label: 'Install image id', type: 'number' },
      { name: 'osVersion', label: 'OS version' },
      { name: 'osEdition', label: 'OS edition' },
      { name: 'osLanguage', label: 'OS language' },
      { name: 'hardwareIds', label: 'Hardware ids (one per line)', type: 'textarea', wide: true, rows: 4, value: 'PCI\\VEN_8086&DEV_15B8\nPCI\\VEN_8086&CC_0200' },
    ]);
    const drivers = () => {
      const v = hw.values();
      const body = { ...v, architecture: id.values().architecture, hardwareIds: (v.hardwareIds || '').split(/\r?\n/).map((s) => s.trim()).filter(Boolean) };
      return run('POST /api/client/drivers/query', () => api('POST', '/api/client/drivers/query', { body }));
    };

    // Step 7: client log entry.
    const log = form([
      { name: 'level', label: 'Level', type: 'select', options: ['Info', 'Warnings', 'Errors'] },
      { name: 'phase', label: 'Phase', value: 'ApplyImage' },
      { name: 'imageName', label: 'Image name' },
      { name: 'computerName', label: 'Computer name' },
      { name: 'message', label: 'Message', value: 'Test entry from the SDS console', wide: true },
    ]);
    const sendLog = () => run('POST /api/client/logs', () => api('POST', '/api/client/logs', { body: { ...log.values(), macAddress: id.values().macAddress } }));

    // Step 8: multicast join and progress.
    const mc = form([
      { name: 'imageId', label: 'Image id to join', type: 'number' },
      { name: 'computerName', label: 'Computer name' },
      { name: 'transmissionId', label: 'Transmission id', type: 'number' },
      { name: 'clientId', label: 'Client id', type: 'number' },
      { name: 'percent', label: 'Percent complete', type: 'number', value: 50 },
      { name: 'leave', label: 'Leave the transmission', type: 'checkbox' },
    ]);
    const join = async () => {
      const v = mc.values();
      const r = await run(`POST /api/client/multicast/${v.imageId}/join`, () =>
        api('POST', `/api/client/multicast/${v.imageId}/join`, { body: { macAddress: id.values().macAddress, computerName: v.computerName } }));
      if (r) {
        mc.get('transmissionId').input.value = r.id;
        const me = (r.clients || []).filter((c) => !c.leftUtc).at(-1);
        if (me) mc.get('clientId').input.value = me.id;
      }
    };
    const progress = () => {
      const v = mc.values();
      return run('POST multicast progress', () =>
        api('POST', `/api/client/multicast/${v.transmissionId}/clients/${v.clientId}${query({ percent: v.percent, leave: v.leave || null })}`));
    };

    const step = (title, ...content) => h('div', { class: 'panel' }, h('h2', { style: 'margin-top:0' }, title), ...content);

    root.append(
      h('h1', {}, 'Client simulator'),
      h('p', { class: 'lead' }, 'Plays the part of a deployment client booted from PXECORE. Steps marked "signed in" use your Windows account as the deploying user.'),
      step('Client identity', id.el),
      step('1. Register and poll (anonymous)', h('div', { class: 'toolbar' }, button('Register', register, 'primary'), requestId.el, button('Poll', poll, ''))),
      step('2. WDS client unattend (anonymous)', h('div', { class: 'toolbar' }, button('Get unattend', unattend, ''))),
      step('3. Image list (signed in)', h('div', { class: 'toolbar' }, imageGroup.el, button('List images', images, ''))),
      step('4. Computer name (signed in)', naming.el, h('div', { class: 'toolbar', style: 'margin-top:8px' }, button('Generate name', name, ''))),
      step('5. Driver query (signed in)', hw.el, h('div', { class: 'toolbar', style: 'margin-top:8px' }, button('Query drivers', drivers, ''))),
      step('6. Client log (anonymous)', log.el, h('div', { class: 'toolbar', style: 'margin-top:8px' }, button('Send log entry', sendLog, ''))),
      step('7. Multicast (signed in)', mc.el, h('div', { class: 'toolbar', style: 'margin-top:8px' }, button('Join', join, ''), button('Report progress', progress, ''))),
      h('h2', {}, 'Responses (newest first)'),
      out);
  },
};

// ---------------------------------------------------------------------------------------
// Section 2: raw API console
// ---------------------------------------------------------------------------------------

export const consoleView = {
  id: 'console',
  title: 'API console',
  group: 'Tools',
  async render(root) {
    const method = field({ name: 'method', label: 'Method', type: 'select', options: ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'] });
    const path = field({ name: 'path', label: 'Path', value: '/api/server', wide: true });
    const type = field({ name: 'type', label: 'Body type', type: 'select', options: ['application/json', 'application/xml', 'text/plain'] });
    const body = field({ name: 'body', label: 'Body', type: 'textarea', wide: true, rows: 8 });
    const out = h('div', {});

    const send = async () => {
      let payload = body.get();
      if (payload && type.get() === 'application/json') {
        // Parse first so a typo is reported here instead of as a 400 from the server.
        payload = await attempt(async () => JSON.parse(payload));
        if (payload === undefined) return;
      }
      const started = performance.now();
      try {
        const result = await api(method.get(), path.get(), { body: payload ?? undefined, contentType: type.get() });
        out.replaceChildren(h('div', { class: 'hint' }, `OK in ${Math.round(performance.now() - started)} ms`), jsonBlock(result ?? '(204 No Content)'));
      } catch (e) {
        out.replaceChildren(h('div', { class: 'hint', style: 'color:var(--danger)' }, `Failed (${e.status ?? 'network'})`), jsonBlock(e.problem ?? e.message));
      }
    };

    root.append(
      h('h1', {}, 'API console'),
      h('p', { class: 'lead' }, ['Sends any request to ', h('span', { class: 'mono' }, API), ' with your Windows credentials. The OpenAPI document is at ', h('a', { href: `${API}/openapi/v1.json`, target: '_blank', rel: 'noopener' }, '/openapi/v1.json'), '.']),
      h('div', { class: 'panel' }, h('div', { class: 'form-grid' }, method.el, type.el, path.el, body.el),
        h('div', { class: 'toolbar', style: 'margin-top:10px' }, button('Send', send, 'primary'))),
      out);
  },
};
