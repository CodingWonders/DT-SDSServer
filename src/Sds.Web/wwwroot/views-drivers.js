// Driver package and driver group views: everything under DRIVERS.

import {
  api, seg, query, upload, trackJob, jobResult,
  h, toast, attempt, fmtBytes, fmtDate, badge, table, button, form, field,
  openDialog, confirmDialog, showDialog,
} from './lib.js';

const ARCHITECTURES = ['x86', 'amd64', 'arm64'];
const FILTER_TYPES = ['Manufacturer', 'Model', 'ChassisType', 'BiosVendor', 'BiosVersion', 'InstallImage', 'OsVersion', 'OsEdition', 'OsLanguage'];

/** Splits "a, b, c" into ["a", "b", "c"] (empty input gives null). */
const list = (text) => {
  const items = (text || '').split(',').map((s) => s.trim()).filter(Boolean);
  return items.length ? items : null;
};

// ---------------------------------------------------------------------------------------
// Section 1: driver packages
// ---------------------------------------------------------------------------------------

export const driversView = {
  id: 'drivers',
  title: 'Driver packages',
  group: 'Drivers',
  async render(root) {
    const filters = form([
      { name: 'architecture', label: 'Architecture', type: 'select', options: [{ value: '', label: 'Any' }, ...ARCHITECTURES] },
      { name: 'class', label: 'Class', placeholder: 'Net, SCSIAdapter...' },
      { name: 'group', label: 'Driver group' },
      { name: 'search', label: 'Search', placeholder: 'name, provider or hardware id' },
      { name: 'enabled', label: 'Enabled', type: 'select', options: [{ value: '', label: 'Any' }, { value: 'true', label: 'Enabled' }, { value: 'false', label: 'Disabled' }] },
    ]);
    const host = h('div', {});
    root.append(
      h('h1', {}, 'Driver packages'),
      h('p', { class: 'lead' }, 'One package per INF and architecture, stored in DRIVERS\\<drvname>_<arch>_{GUID}.'),
      h('div', { class: 'panel' }, filters.el),
      h('div', { class: 'toolbar' }, button('Import drivers', () => importDrivers(), 'primary'), button('Apply filters', () => load(), '')),
      host);

    let groups = [];

    const load = async () => {
      groups = (await attempt(() => api('GET', '/api/driver-groups'))) ?? [];
      const packages = await attempt(() => api('GET', `/api/drivers${query(filters.values())}`));
      if (!packages) return;
      host.replaceChildren(table([
        { label: 'Name', value: (p) => [p.displayName, h('div', { class: 'hint mono' }, p.folderName)] },
        { label: 'INF', value: (p) => p.infName },
        { label: 'Class', value: (p) => p.className ?? '' },
        { label: 'Provider', value: (p) => p.provider ?? '' },
        { label: 'Version', value: (p) => [p.version ?? '', p.driverDate ? h('div', { class: 'hint' }, p.driverDate) : null] },
        { label: 'Arch', value: (p) => p.architecture },
        { label: 'HW ids', value: (p) => p.hardwareIdCount },
        { label: 'Size', value: (p) => fmtBytes(p.sizeBytes) },
        { label: 'Groups', value: (p) => (p.groups || []).join(', ') },
        { label: 'State', value: (p) => [
          p.enabled ? badge('Enabled', 'ok') : badge('Disabled', 'warn'),
          p.signed ? [' ', badge('Signed')] : [' ', badge('Unsigned', 'warn')],
          p.bootCritical ? [' ', badge('Boot critical')] : null] },
      ], packages, {
        empty: 'No driver packages match.',
        actions: (p) => [
          button('Details', () => attempt(async () => showDialog(p.displayName, await api('GET', `/api/drivers/${p.id}`)))),
          button('Rename', () => rename(p)),
          button(p.enabled ? 'Disable' : 'Enable', () => attempt(() => api('PATCH', `/api/drivers/${p.id}`, { body: { enabled: !p.enabled } })).then(load)),
          button('Add to group', () => addToGroup(p)),
          button('Delete', () => remove(p)),
        ],
      }));
    };

    // Import from a server folder, INF or ZIP, or from an uploaded ZIP.
    const importDrivers = () => {
      const f = form([
        { name: 'sourcePath', label: 'Folder, INF or ZIP on the server', placeholder: 'D:\\drivers\\nic', wide: true },
        { name: 'file', label: 'or upload a ZIP from this browser', type: 'file', accept: '.zip', wide: true },
        { name: 'architectures', label: 'Architectures (comma separated, empty for all)', placeholder: 'amd64, arm64' },
        { name: 'enabled', label: 'Enabled', type: 'checkbox', value: true },
      ]);
      const groupChecks = h('div', { class: 'checklist wide' },
        groups.length ? groups.map((g) => h('label', {}, h('input', { type: 'checkbox', value: g.name }), g.name)) : h('span', { class: 'hint' }, 'No driver groups yet.'));
      const status = h('div', { class: 'hint wide' });

      openDialog({
        title: 'Import driver packages', okText: 'Import', wide: true,
        body: h('div', { class: 'form-grid' }, f.el, h('div', { class: 'wide' }, h('div', { class: 'hint' }, 'Add to driver groups'), groupChecks), status),
        onOk: async () => {
          const v = f.values();
          let uploadId = null;
          if (v.file) {
            const up = await upload(v.file, (p) => { status.textContent = `Uploading ${p}%`; });
            uploadId = up.uploadId;
          } else if (!v.sourcePath) {
            throw new Error('Enter a server path or choose a ZIP file.');
          }
          const body = {
            sourcePath: uploadId ? null : v.sourcePath,
            architectures: list(v.architectures),
            driverGroups: [...groupChecks.querySelectorAll('input:checked')].map((c) => c.value),
            enabled: v.enabled,
          };
          const job = await api('POST', `/api/drivers/import${query({ uploadId })}`, { body });
          trackJob(job, {
            onDone: (j) => {
              const r = jobResult(j);
              if (r) {
                showDialog('Driver import result', {
                  imported: r.imported.map((p) => `${p.folderName} (${p.displayName})`),
                  duplicates: r.duplicates.map((p) => p.folderName),
                  skipped: r.skipped,
                });
              }
              load();
            },
          });
        },
      });
    };

    const rename = (p) => {
      const name = field({ name: 'displayName', label: 'Display name', value: p.displayName, wide: true });
      openDialog({
        title: 'Rename driver package', body: h('div', { class: 'form-grid' }, name.el), okText: 'Save',
        onOk: async () => {
          await api('PATCH', `/api/drivers/${p.id}`, { body: { displayName: name.get() } });
          load();
        },
      });
    };

    const addToGroup = (p) => {
      if (groups.length === 0) {
        toast('Create a driver group first.', 'error');
        return;
      }
      const g = field({ name: 'group', label: 'Driver group', type: 'select', options: groups.map((x) => x.name) });
      openDialog({
        title: `Add ${p.displayName} to a group`, body: h('div', { class: 'form-grid' }, g.el), okText: 'Add',
        onOk: async () => {
          await api('POST', `/api/driver-groups/${seg(g.get())}/packages`, { body: { packageIds: [p.id] } });
          toast('Added', 'ok');
          load();
        },
      });
    };

    const remove = async (p) => {
      if (await confirmDialog('Delete driver package', `Delete ${p.folderName} and its folder?`)) {
        await attempt(() => api('DELETE', `/api/drivers/${p.id}`));
        load();
      }
    };

    await load();
  },
};

// ---------------------------------------------------------------------------------------
// Section 2: driver groups
// ---------------------------------------------------------------------------------------

/** Filter editor rows: type, include/exclude and comma separated values. */
function filterEditor(filters) {
  const host = h('div', {});
  const addRow = (f = { type: 'Manufacturer', policy: 'Include', values: [] }) => {
    const type = h('select', {}, FILTER_TYPES.map((t) => h('option', { value: t }, t)));
    type.value = f.type;
    const policy = h('select', {}, h('option', { value: 'Include' }, 'Include'), h('option', { value: 'Exclude' }, 'Exclude'));
    policy.value = f.policy;
    const values = h('input', { type: 'text', value: (f.values || []).join(', '), placeholder: 'values, comma separated' });
    const row = h('div', { class: 'filter-row' }, type, policy, values, button('Remove', () => row.remove()));
    row.read = () => ({ type: type.value, policy: policy.value, values: list(values.value) ?? [] });
    host.append(row);
  };
  (filters || []).forEach(addRow);
  return {
    el: h('div', { class: 'wide' }, h('div', { class: 'hint' }, 'Filters (all must match for the group to apply)'), host, button('Add filter', () => addRow())),
    read: () => [...host.children].map((r) => r.read()),
  };
}

export const driverGroupsView = {
  id: 'driver-groups',
  title: 'Driver groups',
  group: 'Drivers',
  async render(root) {
    const host = h('div', {});
    root.append(
      h('h1', {}, 'Driver groups'),
      h('p', { class: 'lead' }, 'Groups decide which packages a client receives, with WDS style filters and applicability.'),
      h('div', { class: 'toolbar' }, button('New group', () => edit(null), 'primary'), button('Refresh', () => load(), '')),
      host);

    const load = async () => {
      const groups = await attempt(() => api('GET', '/api/driver-groups'));
      if (!groups) return;
      host.replaceChildren(table([
        { label: 'Name', value: (g) => [g.name, g.description ? h('div', { class: 'hint' }, g.description) : null] },
        { label: 'Enabled', value: (g) => (g.enabled ? badge('Enabled', 'ok') : badge('Disabled', 'warn')) },
        { label: 'Applicability', value: (g) => g.applicability },
        { label: 'Filters', value: (g) => (g.filters.length ? g.filters.map((f) => h('div', {}, `${f.type} ${f.policy}: ${f.values.join(', ')}`)) : 'none') },
        { label: 'Packages', value: (g) => g.packageCount },
        { label: 'Created', value: (g) => fmtDate(g.createdUtc) },
      ], groups, {
        empty: 'No driver groups.',
        actions: (g) => [
          button('Edit', () => edit(g)),
          button('Packages', () => packages(g)),
          button('Delete', () => remove(g)),
        ],
      }));
    };

    const edit = (g) => {
      const f = form([
        { name: 'name', label: 'Name', value: g?.name },
        { name: 'description', label: 'Description', value: g?.description },
        { name: 'applicability', label: 'Applicability', type: 'select', options: ['Matching', 'All'], value: g?.applicability ?? 'Matching' },
        { name: 'enabled', label: 'Enabled', type: 'checkbox', value: g?.enabled ?? true },
      ]);
      const filters = filterEditor(g?.filters);
      openDialog({
        title: g ? `Edit ${g.name}` : 'New driver group', okText: 'Save', wide: true,
        body: h('div', { class: 'form-grid' }, f.el, filters.el),
        onOk: async () => {
          const body = { ...f.values(), filters: filters.read() };
          if (g) {
            await api('PUT', `/api/driver-groups/${seg(g.name)}`, { body });
          } else {
            await api('POST', '/api/driver-groups', { body });
          }
          toast('Driver group saved', 'ok');
          load();
        },
      });
    };

    // Membership: current packages can be removed, other packages can be added.
    const packages = async (g) => {
      const [members, all] = await Promise.all([
        attempt(() => api('GET', `/api/driver-groups/${seg(g.name)}/packages`)),
        attempt(() => api('GET', '/api/drivers')),
      ]);
      if (!members || !all) return;
      const memberIds = new Set(members.map((p) => p.id));
      const label = (p) => `${p.displayName} (${p.architecture}, ${p.infName})`;

      const current = h('div', { class: 'checklist' }, members.length
        ? members.map((p) => h('label', {}, h('input', { type: 'checkbox', value: p.id }), label(p)))
        : h('span', { class: 'hint' }, 'No packages.'));
      const others = all.filter((p) => !memberIds.has(p.id));
      const candidates = h('div', { class: 'checklist' }, others.length
        ? others.map((p) => h('label', {}, h('input', { type: 'checkbox', value: p.id }), label(p)))
        : h('span', { class: 'hint' }, 'Every package is already in the group.'));

      openDialog({
        title: `Packages of ${g.name}`, okText: 'Apply', wide: true,
        body: h('div', {}, h('h2', {}, 'Tick to remove'), current, h('h2', {}, 'Tick to add'), candidates),
        onOk: async () => {
          const ids = (el) => [...el.querySelectorAll('input:checked')].map((c) => c.value);
          const toRemove = ids(current);
          const toAdd = ids(candidates);
          if (toRemove.length) await api('POST', `/api/driver-groups/${seg(g.name)}/packages/remove`, { body: { packageIds: toRemove } });
          if (toAdd.length) await api('POST', `/api/driver-groups/${seg(g.name)}/packages`, { body: { packageIds: toAdd } });
          toast('Membership updated', 'ok');
          load();
        },
      });
    };

    const remove = async (g) => {
      if (await confirmDialog('Delete driver group', `Delete ${g.name}? Its packages are kept.`)) {
        await attempt(() => api('DELETE', `/api/driver-groups/${seg(g.name)}`));
        load();
      }
    };

    await load();
  },
};
