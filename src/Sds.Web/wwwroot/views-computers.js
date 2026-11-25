// Known computer (prestaged devices) and pending device views: client authorization.

import {
  api, apiOptional, query,
  h, toast, attempt, fmtDate, badge, table, button, form, field,
  openDialog, confirmDialog, showDialog,
} from './lib.js';

/** Form fields of a known computer (ComputerRequest). */
function computerFields(c = {}) {
  return [
    { name: 'name', label: 'Name (max 15 characters)', value: c.name },
    { name: 'macAddress', label: 'MAC address', value: c.macAddress, placeholder: '00-11-22-33-44-55' },
    { name: 'smbiosGuid', label: 'SMBIOS GUID', value: c.smbiosGuid },
    { name: 'architecture', label: 'Architecture', type: 'select', options: ['', 'x86', 'amd64', 'arm64'], value: c.architecture ?? '' },
    { name: 'description', label: 'Description', value: c.description },
    { name: 'joinDomain', label: 'Join a domain', type: 'checkbox', value: c.joinDomain ?? true },
    { name: 'domain', label: 'Domain', value: c.domain },
    { name: 'organizationalUnit', label: 'OU (distinguished name)', value: c.organizationalUnit },
    { name: 'user', label: 'User with join rights', value: c.user },
    { name: 'joinRights', label: 'Join rights', type: 'select', options: ['JoinOnly', 'Full'], value: c.joinRights ?? 'JoinOnly' },
    { name: 'referralServer', label: 'Referral server', value: c.referralServer },
    { name: 'bootProgram', label: 'Boot program', value: c.bootProgram },
    { name: 'bootImagePath', label: 'Boot image path', value: c.bootImagePath },
    { name: 'imageGroup', label: 'Restrict to image group', value: c.imageGroup },
    { name: 'installImageId', label: 'Preselected image id', type: 'number', value: c.installImageId },
    { name: 'clientUnattend', label: 'Device specific client unattend XML', type: 'textarea', wide: true, rows: 5 },
  ];
}

// ---------------------------------------------------------------------------------------
// Section 1: known computers
// ---------------------------------------------------------------------------------------

export const computersView = {
  id: 'computers',
  title: 'Computers',
  group: 'Clients',
  async render(root) {
    const search = h('input', { type: 'text', placeholder: 'name or MAC', onkeydown: (e) => e.key === 'Enter' && load() });
    const host = h('div', {});
    root.append(
      h('h1', {}, 'Known computers'),
      h('p', { class: 'lead' }, 'Prestaged and approved devices. A known computer is authorized: the server answers it under the "known only" policy.'),
      h('div', { class: 'toolbar' },
        h('label', { class: 'field' }, 'Search', search),
        button('Search', () => load(), ''),
        h('span', { class: 'spacer' }),
        button('Add computer', () => edit(null), 'primary')),
      host);

    const load = async () => {
      const list = await attempt(() => api('GET', `/api/computers${query({ search: search.value.trim() })}`));
      if (!list) return;
      host.replaceChildren(table([
        { label: 'Id', value: (c) => c.id },
        { label: 'Name', value: (c) => [c.name, c.description ? h('div', { class: 'hint' }, c.description) : null] },
        { label: 'MAC', value: (c) => h('span', { class: 'mono' }, c.macAddress ?? '') },
        { label: 'SMBIOS GUID', value: (c) => h('span', { class: 'mono' }, c.smbiosGuid ?? '') },
        { label: 'Arch', value: (c) => c.architecture ?? '' },
        { label: 'Domain / OU', value: (c) => [c.joinDomain ? (c.domain ?? '(default)') : 'no join', c.organizationalUnit ? h('div', { class: 'hint' }, c.organizationalUnit) : null] },
        { label: 'Origin', value: (c) => c.origin },
        { label: 'AD', value: (c) => (c.directorySync === 'Failed' ? badge('Failed', 'bad') : c.directorySync === 'InSync' ? badge('In sync', 'ok') : badge('n/a')) },
        { label: 'Last seen', value: (c) => fmtDate(c.lastSeenUtc) },
      ], list, {
        empty: 'No known computers.',
        actions: (c) => [
          button('Details', () => showDialog(c.name, c)),
          button('Edit', () => edit(c)),
          button('Unattend', () => attempt(async () => {
            const xml = await apiOptional('GET', `/api/computers/${c.id}/unattend`, { text: true });
            showDialog(`Client unattend of ${c.name}`, xml ?? 'No device specific client unattend.');
          })),
          button('AD sync', () => attempt(async () => { await api('POST', `/api/computers/${c.id}/sync`); toast('Synchronized', 'ok'); load(); })),
          button('Delete', () => remove(c)),
        ],
      }));
    };

    const edit = async (c) => {
      // The list does not carry the unattend text; fetch it so editing keeps it.
      const unattend = c?.hasClientUnattend ? await attempt(() => apiOptional('GET', `/api/computers/${c.id}/unattend`, { text: true })) : null;
      const f = form(computerFields(c ?? {}));
      if (unattend) f.get('clientUnattend').input.value = unattend;

      openDialog({
        title: c ? `Edit ${c.name}` : 'Add computer', okText: 'Save', wide: true, body: f.el,
        onOk: async () => {
          const v = f.values();
          if (c) {
            await api('PUT', `/api/computers/${c.id}`, { body: v });
          } else {
            await api('POST', '/api/computers', { body: v });
          }
          toast('Computer saved', 'ok');
          load();
        },
      });
    };

    const remove = (c) => {
      const fromAd = field({ name: 'ad', label: 'Also delete the Active Directory object', type: 'checkbox' });
      openDialog({
        title: `Delete ${c.name}`, okText: 'Delete',
        body: h('div', {}, h('p', {}, 'Deleting the computer removes its authorization.'), fromAd.el),
        onOk: async () => {
          await api('DELETE', `/api/computers/${c.id}${query({ deleteFromDirectory: fromAd.get() || null })}`);
          load();
        },
      });
    };

    await load();
  },
};

// ---------------------------------------------------------------------------------------
// Section 2: pending devices
// ---------------------------------------------------------------------------------------

/** Approval options (ApproveRequest); empty fields use the server defaults. */
const approveFields = () => [
  { name: 'name', label: 'Computer name', placeholder: 'empty: naming policy' },
  { name: 'user', label: 'User (naming and join rights)' },
  { name: 'joinRights', label: 'Join rights', type: 'select', options: ['', 'JoinOnly', 'Full'] },
  { name: 'joinDomain', label: 'Join domain', type: 'select', options: [{ value: '', label: 'Server default' }, { value: 'true', label: 'Yes' }, { value: 'false', label: 'No' }] },
  { name: 'domain', label: 'Domain' },
  { name: 'organizationalUnit', label: 'OU' },
  { name: 'referralServer', label: 'Referral server' },
  { name: 'bootProgram', label: 'Boot program' },
  { name: 'bootImagePath', label: 'Boot image path' },
  { name: 'imageGroup', label: 'Restrict to image group' },
  { name: 'message', label: 'Message to the client', wide: true },
];

/** Converts the approve form values to the request body. */
const approveBody = (v) => ({ ...v, joinDomain: v.joinDomain === null ? null : v.joinDomain === 'true' });

export const pendingView = {
  id: 'pending',
  title: 'Pending devices',
  group: 'Clients',
  async render(root) {
    const status = h('select', { onchange: () => load() },
      h('option', { value: '' }, 'All'), ['Pending', 'Approved', 'Rejected'].map((s) => h('option', { value: s }, s)));
    status.value = 'Pending';
    const host = h('div', {});
    root.append(
      h('h1', {}, 'Pending devices'),
      h('p', { class: 'lead' }, 'Unknown clients waiting for approval (response policy "All" with approval required).'),
      h('div', { class: 'toolbar' },
        h('label', { class: 'field' }, 'Status', status),
        button('Refresh', () => load(), ''),
        h('span', { class: 'spacer' }),
        button('Approve all', () => approve(null), 'primary'),
        button('Delete records', () => deleteAll(), 'danger')),
      host);

    const load = async () => {
      const list = await attempt(() => api('GET', `/api/pending-devices${query({ status: status.value })}`));
      if (!list) return;
      host.replaceChildren(table([
        { label: 'Request', value: (d) => d.requestId },
        { label: 'MAC', value: (d) => h('span', { class: 'mono' }, d.macAddress ?? '') },
        { label: 'SMBIOS GUID', value: (d) => h('span', { class: 'mono' }, d.smbiosGuid ?? '') },
        { label: 'Arch', value: (d) => d.architecture ?? '' },
        { label: 'IP', value: (d) => d.ipAddress ?? '' },
        { label: 'Reported name', value: (d) => d.reportedName ?? '' },
        { label: 'Status', value: (d) => badge(d.status, d.status === 'Approved' ? 'ok' : d.status === 'Rejected' ? 'bad' : 'warn') },
        { label: 'First / last seen', value: (d) => [fmtDate(d.firstSeenUtc), h('div', { class: 'hint' }, fmtDate(d.lastSeenUtc))] },
        { label: 'Decided', value: (d) => (d.decidedUtc ? [fmtDate(d.decidedUtc), h('div', { class: 'hint' }, d.decidedBy ?? '')] : '') },
      ], list, {
        empty: 'No device records.',
        actions: (d) => [
          d.status === 'Pending' ? button('Approve', () => approve(d)) : null,
          d.status === 'Pending' ? button('Reject', () => reject(d)) : null,
          button('Delete', () => attempt(() => api('DELETE', `/api/pending-devices/${d.requestId}`)).then(load)),
        ],
      }));
    };

    // Approve one device, or every pending device when d is null.
    const approve = (d) => {
      const f = form(approveFields());
      openDialog({
        title: d ? `Approve request ${d.requestId}` : 'Approve every pending device', okText: 'Approve', wide: true, body: f.el,
        onOk: async () => {
          const body = approveBody(f.values());
          const result = d
            ? await api('POST', `/api/pending-devices/${d.requestId}/approve`, { body })
            : await api('POST', '/api/pending-devices/approve-all', { body });
          toast(d ? `Approved, computer id ${result.computerId}` : `Approved ${result.length} device(s)`, 'ok');
          load();
        },
      });
    };

    const reject = (d) => {
      const message = field({ name: 'message', label: 'Message to the client', wide: true });
      openDialog({
        title: `Reject request ${d.requestId}`, okText: 'Reject', body: h('div', { class: 'form-grid' }, message.el),
        onOk: async () => {
          await api('POST', `/api/pending-devices/${d.requestId}/reject`, { body: { message: message.get() } });
          load();
        },
      });
    };

    const deleteAll = async () => {
      const label = status.value ? `every ${status.value.toLowerCase()} record` : 'every record';
      if (await confirmDialog('Delete device records', `Delete ${label}?`)) {
        const r = await attempt(() => api('DELETE', `/api/pending-devices${query({ status: status.value })}`));
        if (r) toast(`Deleted ${r.deleted} record(s)`, 'ok');
        load();
      }
    };

    await load();
  },
};
