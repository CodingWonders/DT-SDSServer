// Image group and install image views: everything under IMAGES (WDS "Install Images").

import {
  api, apiOptional, seg, query, upload, trackJob, jobResult, jobFileUrl,
  h, toast, attempt, fmtBytes, fmtDate, badge, table, button, form, field,
  openDialog, confirmDialog, showDialog,
} from './lib.js';

// ---------------------------------------------------------------------------------------
// Section 1: NTFS permission editor (group folders and image files, the WDS user filter)
// ---------------------------------------------------------------------------------------

/**
 * Opens the permission editor. Inherited entries are shown read only; explicit entries can
 * be added, changed or removed. Saving replaces the explicit entries (PUT .../acl).
 */
export async function aclDialog(title, path) {
  const acl = await api('GET', path);
  const inherit = field({ name: 'inherit', label: 'Inherit permissions from the parent folder', type: 'checkbox', value: acl.inheritFromParent });

  const inherited = acl.entries.filter((e) => e.inherited);
  const rowsHost = h('div', {});

  // One editable row per explicit entry.
  const addRow = (entry = { identity: '', access: 'Read', allow: true }) => {
    const identity = h('input', { type: 'text', value: entry.identity, placeholder: 'DOMAIN\\name or S-1-...' });
    const access = h('select', {}, ['Read', 'Modify', 'FullControl'].map((a) => h('option', { value: a }, a)));
    access.value = entry.access;
    const allow = h('select', {}, h('option', { value: 'true' }, 'Allow'), h('option', { value: 'false' }, 'Deny'));
    allow.value = String(entry.allow);
    const row = h('div', { class: 'filter-row' }, identity, access, allow, button('Remove', () => row.remove()));
    row.read = () => ({ identity: identity.value.trim(), access: access.value, allow: allow.value === 'true' });
    rowsHost.append(row);
  };
  acl.entries.filter((e) => !e.inherited).forEach(addRow);

  const body = h('div', {},
    inherit.el,
    h('h2', {}, 'Inherited entries'),
    table([
      { label: 'Identity', value: (e) => e.identity },
      { label: 'Access', value: (e) => e.access },
      { label: 'Type', value: (e) => (e.allow ? 'Allow' : 'Deny') },
    ], inherited, { empty: 'No inherited entries.' }),
    h('h2', {}, 'Explicit entries'),
    h('p', { class: 'hint' }, 'For image files these entries are the WDS user filter: only accounts that can read the file see the image in GET /api/client/images.'),
    rowsHost,
    button('Add entry', () => addRow()));

  return openDialog({
    title, body, okText: 'Save', wide: true,
    onOk: async () => {
      const entries = [...rowsHost.children].map((r) => r.read()).filter((e) => e.identity);
      await api('PUT', path, { body: { inheritFromParent: inherit.get(), entries } });
      toast('Permissions saved', 'ok');
    },
  });
}

/** Opens an XML editor for an unattend file stored behind GET/PUT/DELETE path. */
export async function unattendDialog(title, path) {
  const xml = await apiOptional('GET', path, { text: true });
  const editor = field({ name: 'xml', label: 'Unattend XML (leave empty and save to remove it)', type: 'textarea', value: xml ?? '', rows: 18, wide: true });
  return openDialog({
    title, body: h('div', {}, editor.el), okText: 'Save', wide: true,
    onOk: async () => {
      const value = editor.get();
      if (value) {
        await api('PUT', path, { body: value, contentType: 'application/xml' });
        toast('Unattend saved', 'ok');
      } else {
        await apiOptional('DELETE', path);
        toast('Unattend removed', 'ok');
      }
    },
  });
}

// ---------------------------------------------------------------------------------------
// Section 2: image source picker (server path or browser upload) shared by add and replace
// ---------------------------------------------------------------------------------------

/**
 * Builds the source part of a form. resolve() uploads the file when one was chosen (once)
 * and returns { sourcePath, uploadId } to send to the API.
 */
function sourcePicker() {
  const path = field({ name: 'sourcePath', label: 'Source WIM or ESD path on the server', placeholder: 'D:\\sources\\install.wim', wide: true });
  const file = field({ name: 'file', label: 'or upload a file from this browser', type: 'file', accept: '.wim,.esd,.swm', wide: true });
  const status = h('div', { class: 'hint' });
  let uploaded = null;
  let uploadedFile = null;

  const resolve = async () => {
    const chosen = file.get();
    if (chosen) {
      if (uploadedFile !== chosen) {
        status.textContent = 'Uploading...';
        uploaded = await upload(chosen, (p) => { status.textContent = `Uploading ${p}%`; });
        uploadedFile = chosen;
        status.textContent = `Uploaded ${chosen.name} (${fmtBytes(uploaded.size)})`;
      }
      return { sourcePath: null, uploadId: uploaded.uploadId };
    }
    const p = path.get();
    if (!p) {
      throw new Error('Enter a server path or choose a file.');
    }
    return { sourcePath: p, uploadId: null };
  };

  return { el: [path.el, file.el, status], resolve };
}

// ---------------------------------------------------------------------------------------
// Section 3: image groups view
// ---------------------------------------------------------------------------------------

export const imageGroupsView = {
  id: 'image-groups',
  title: 'Image groups',
  group: 'Images',
  async render(root) {
    const host = h('div', {});
    root.append(
      h('h1', {}, 'Image groups'),
      h('p', { class: 'lead' }, 'Folders under IMAGES. Each group holds one Res.RWM with the file data of all its images.'),
      h('div', { class: 'toolbar' }, button('New group', () => createGroup(), 'primary'), button('Refresh', () => load(), '')),
      host);

    const load = async () => {
      const groups = await attempt(() => api('GET', '/api/image-groups'));
      if (!groups) return;
      host.replaceChildren(table([
        { label: 'Name', value: (g) => g.name },
        { label: 'Description', value: (g) => g.description ?? '' },
        { label: 'Images', value: (g) => g.imageCount },
        { label: 'Res.RWM', value: (g) => fmtBytes(g.resourceFileBytes) },
        { label: 'UNC path', value: (g) => h('span', { class: 'mono' }, g.uncPath) },
        { label: 'Modified', value: (g) => fmtDate(g.modifiedUtc) },
      ], groups, {
        empty: 'No image groups. Create one, then add images to it.',
        actions: (g) => [
          button('Edit', () => editGroup(g)),
          button('Permissions', () => attempt(() => aclDialog(`Permissions of ${g.name}`, `/api/image-groups/${seg(g.name)}/acl`))),
          button('Compact', () => compact(g)),
          button('Delete', () => deleteGroup(g)),
        ],
      }));
    };

    const createGroup = () => {
      const f = form([
        { name: 'name', label: 'Name' },
        { name: 'description', label: 'Description' },
      ]);
      openDialog({
        title: 'New image group', body: f.el, okText: 'Create',
        onOk: async () => {
          await api('POST', '/api/image-groups', { body: f.values() });
          toast('Group created', 'ok');
          load();
        },
      });
    };

    const editGroup = (g) => {
      const f = form([
        { name: 'name', label: 'Name (renames the folder)', value: g.name },
        { name: 'description', label: 'Description', value: g.description },
      ]);
      openDialog({
        title: `Edit ${g.name}`, body: f.el, okText: 'Save',
        onOk: async () => {
          const v = f.values();
          await api('PATCH', `/api/image-groups/${seg(g.name)}`, { body: { name: v.name === g.name ? null : v.name, description: v.description ?? '' } });
          toast('Group saved', 'ok');
          load();
        },
      });
    };

    const compact = async (g) => {
      const job = await attempt(() => api('POST', `/api/image-groups/${seg(g.name)}/compact`));
      trackJob(job, {
        onDone: (j) => {
          const r = jobResult(j);
          if (r) toast(`Compacted ${g.name}: ${fmtBytes(r.bytesReclaimed)} reclaimed`, 'ok');
          load();
        },
      });
    };

    const deleteGroup = (g) => {
      const force = field({ name: 'force', label: 'Also delete the images it contains', type: 'checkbox', value: false });
      openDialog({
        title: `Delete ${g.name}`,
        body: h('div', {}, h('p', {}, `Delete the image group ${g.name} and its folder?`), force.el),
        okText: 'Delete',
        onOk: async () => {
          await api('DELETE', `/api/image-groups/${seg(g.name)}${query({ force: force.get() || null })}`);
          toast('Group deleted', 'ok');
          load();
        },
      });
    };

    await load();
  },
};

// ---------------------------------------------------------------------------------------
// Section 4: install images view
// ---------------------------------------------------------------------------------------

export const imagesView = {
  id: 'images',
  title: 'Install images',
  group: 'Images',
  async render(root) {
    const groupFilter = h('select', { onchange: () => load() });
    const host = h('div', {});
    root.append(
      h('h1', {}, 'Install images'),
      h('p', { class: 'lead' }, 'Each image is a metadata only WIM that references the Res.RWM of its group, like WDS.'),
      h('div', { class: 'toolbar' },
        h('label', { class: 'field' }, 'Group', groupFilter),
        button('Add images', () => addImages(), 'primary'),
        button('Refresh', () => load(), '')),
      host);

    let groups = [];

    const loadGroups = async () => {
      groups = (await attempt(() => api('GET', '/api/image-groups'))) ?? [];
      const current = groupFilter.value;
      // replaceChildren takes nodes as separate arguments, so the array is spread.
      groupFilter.replaceChildren(h('option', { value: '' }, 'All groups'), ...groups.map((g) => h('option', { value: g.name }, g.name)));
      groupFilter.value = current;
    };

    const load = async () => {
      const images = await attempt(() => api('GET', `/api/images${query({ group: groupFilter.value })}`));
      if (!images) return;
      host.replaceChildren(table([
        { label: 'Id', value: (i) => i.id },
        { label: 'Group', value: (i) => i.groupName },
        { label: 'File', value: (i) => h('span', { class: 'mono' }, i.fileName) },
        { label: 'Name', value: (i) => [i.name, i.description ? h('div', { class: 'hint' }, i.description) : null] },
        { label: 'Arch', value: (i) => i.architecture },
        { label: 'Version', value: (i) => [i.version ?? '', i.editionId ? h('div', { class: 'hint' }, i.editionId) : null] },
        { label: 'Languages', value: (i) => (i.languages || []).join(', ') },
        { label: 'Size', value: (i) => fmtBytes(i.totalBytes) },
        { label: 'Priority', value: (i) => i.priority },
        { label: 'State', value: (i) => [
          i.enabled ? badge('Enabled', 'ok') : badge('Disabled', 'warn'), ' ',
          badge(i.status, i.status === 'Ready' ? '' : 'bad'),
          i.hasUnattend ? [' ', badge('Unattend')] : null] },
      ], images, {
        empty: 'No images. Use "Add images" to import a WIM.',
        actions: (i) => [
          button('Details', () => showDialog(`Image ${i.id}`, i)),
          button('Edit', () => editImage(i)),
          button(i.enabled ? 'Disable' : 'Enable', () => toggle(i)),
          button('Export', () => exportImage(i)),
          button('Copy', () => copyImage(i, false)),
          button('Move', () => copyImage(i, true)),
          button('Replace', () => replaceImage(i)),
          button('Unattend', () => attempt(() => unattendDialog(`Unattend of ${i.name}`, `/api/images/${i.id}/unattend`)).then(load)),
          button('Permissions', () => attempt(() => aclDialog(`Permissions of ${i.fileName}`, `/api/images/${i.id}/acl`))),
          button('Delete', () => deleteImage(i)),
        ],
      }));
    };

    // Add images: choose group and source, inspect the WIM, pick indexes, then queue the job.
    const addImages = () => {
      if (groups.length === 0) {
        toast('Create an image group first.', 'error');
        return;
      }
      const group = field({ name: 'group', label: 'Image group', type: 'select', options: groups.map((g) => g.name), value: groupFilter.value || groups[0].name });
      const source = sourcePicker();
      const indexHost = h('div', { class: 'checklist wide' }, h('span', { class: 'hint' }, 'Press "Inspect source" to list the images it contains. With none selected, every image is added.'));
      const opts = form([
        { name: 'fileName', label: 'Metadata WIM file name', placeholder: 'defaults to the source name' },
        { name: 'name', label: 'Image name (single image only)' },
        { name: 'description', label: 'Description (single image only)' },
        { name: 'priority', label: 'Priority', type: 'number' },
        { name: 'enabled', label: 'Enabled', type: 'checkbox', value: true },
        { name: 'unattendXml', label: 'Image unattend XML (optional)', type: 'textarea', wide: true, rows: 4 },
      ]);

      const inspect = async () => {
        const src = await source.resolve();
        const list = await api('POST', `/api/images/inspect${query({ uploadId: src.uploadId })}`, { body: { sourcePath: src.sourcePath } });
        indexHost.replaceChildren(...list.map((img) => h('label', {},
          h('input', { type: 'checkbox', value: img.index }),
          `${img.index}: ${img.name} (${img.architecture}, ${img.editionId ?? ''} ${img.version ?? ''}, ${fmtBytes(img.totalBytes)})`)));
      };

      openDialog({
        title: 'Add install images', okText: 'Add', wide: true,
        body: h('div', { class: 'form-grid' }, group.el, source.el,
          h('div', { class: 'wide' }, button('Inspect source', () => attempt(inspect), '')), indexHost, opts.el),
        onOk: async () => {
          const src = await source.resolve();
          const indexes = [...indexHost.querySelectorAll('input:checked')].map((c) => Number(c.value));
          const body = { ...opts.values(), sourcePath: src.sourcePath, indexes: indexes.length ? indexes : null };
          const job = await api('POST', `/api/image-groups/${seg(group.get())}/images${query({ uploadId: src.uploadId })}`, { body });
          trackJob(job, { onDone: () => { loadGroups(); load(); } });
        },
      });
    };

    const editImage = (i) => {
      const f = form([
        { name: 'name', label: 'Name', value: i.name },
        { name: 'description', label: 'Description', value: i.description },
        { name: 'priority', label: 'Priority', type: 'number', value: i.priority },
        { name: 'enabled', label: 'Enabled', type: 'checkbox', value: i.enabled },
      ]);
      openDialog({
        title: `Edit image ${i.id}`, body: f.el, okText: 'Save',
        onOk: async () => {
          const v = f.values();
          await api('PATCH', `/api/images/${i.id}`, { body: { ...v, description: v.description ?? '' } });
          toast('Image saved', 'ok');
          load();
        },
      });
    };

    const toggle = async (i) => {
      await attempt(() => api('PATCH', `/api/images/${i.id}`, { body: { enabled: !i.enabled } }));
      load();
    };

    // Export: to a server path, or to the staging area and then download from the job.
    const exportImage = (i) => {
      const f = form([
        { name: 'destinationPath', label: 'Destination path on the server', placeholder: 'empty: download through the browser', wide: true },
        { name: 'append', label: 'Append to an existing destination WIM', type: 'checkbox' },
        { name: 'name', label: 'Exported image name', value: i.name },
        { name: 'description', label: 'Exported description', value: i.description },
      ]);
      openDialog({
        title: `Export ${i.name}`, body: f.el, okText: 'Export',
        onOk: async () => {
          const job = await api('POST', `/api/images/${i.id}/export`, { body: f.values() });
          trackJob(job, {
            onDone: (j) => {
              const r = jobResult(j);
              if (j.status !== 'Succeeded' || !r) return;
              if (r.staged) {
                showDialog('Export ready', h('div', {},
                  h('p', {}, `${r.name} exported (${fmtBytes(r.bytes)}).`),
                  h('a', { href: jobFileUrl(j.id), download: '' }, 'Download the WIM'),
                  h('p', { class: 'hint' }, 'The staged file is kept for the configured retention period.')));
              } else {
                toast(`Exported to ${r.path}`, 'ok', 10000);
              }
            },
          });
        },
      });
    };

    const copyImage = (i, move) => {
      const f = form([
        { name: 'targetGroup', label: 'Target group', type: 'select', options: groups.map((g) => g.name).filter((n) => n !== i.groupName) },
        { name: 'fileName', label: 'File name', placeholder: 'defaults to the current name' },
        { name: 'name', label: 'Image name', value: i.name },
        { name: 'description', label: 'Description', value: i.description },
      ]);
      openDialog({
        title: `${move ? 'Move' : 'Copy'} ${i.name}`, body: f.el, okText: move ? 'Move' : 'Copy',
        onOk: async () => {
          const v = f.values();
          if (!v.targetGroup) throw new Error('Another image group is needed.');
          const job = await api('POST', `/api/images/${i.id}/${move ? 'move' : 'copy'}`, { body: v });
          trackJob(job, { onDone: () => { loadGroups(); load(); } });
        },
      });
    };

    const replaceImage = (i) => {
      const source = sourcePicker();
      const index = field({ name: 'index', label: 'Image index in the source', type: 'number', value: 1 });
      openDialog({
        title: `Replace ${i.name}`, okText: 'Replace',
        body: h('div', { class: 'form-grid' }, source.el, index.el),
        onOk: async () => {
          const src = await source.resolve();
          const job = await api('POST', `/api/images/${i.id}/replace${query({ uploadId: src.uploadId })}`, { body: { sourcePath: src.sourcePath, index: index.get() ?? 1 } });
          trackJob(job, { onDone: () => load() });
        },
      });
    };

    const deleteImage = async (i) => {
      if (await confirmDialog('Delete image', `Delete ${i.name} (${i.fileName}) from ${i.groupName}? Its data stays in Res.RWM until the group is compacted.`)) {
        await attempt(() => api('DELETE', `/api/images/${i.id}`));
        load();
      }
    };

    await loadGroups();
    await load();
  },
};
