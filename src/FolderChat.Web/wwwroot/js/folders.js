// The Settings page's knowledge folders: add and remove folders, see what FolderChat read in them, and set up summaries.
(() => {
    const { api, toast } = window.folderChat;

    const els = {
        choose: document.getElementById("folder-choose"), // only there when the server can open the computer's folder window
        form: document.getElementById("folder-form"),
        path: document.getElementById("folder-path"),
        name: document.getElementById("folder-name"),
        add: document.getElementById("folder-add"),
        list: document.getElementById("folder-list"),
        none: document.getElementById("no-folders"),
        modal: document.getElementById("folder-modal"),
        editForm: document.getElementById("folder-edit-form"),
        editTitle: document.getElementById("folder-modal-title"),
        editName: document.getElementById("folder-edit-name"),
        summaries: document.getElementById("folder-summaries"),
        summaryModelGroup: document.getElementById("folder-summary-model-group"),
        summaryModel: document.getElementById("folder-summary-model"),
        summaryPrivacy: document.getElementById("folder-summary-privacy"),
        editError: document.getElementById("folder-edit-error"),
        editSave: document.getElementById("folder-edit-save"),
    };

    const modal = bootstrap.Modal.getOrCreateInstance(els.modal);
    const numbers = new Intl.NumberFormat();
    const compactNumbers = new Intl.NumberFormat(undefined, { notation: "compact", maximumFractionDigits: 1 });
    const relativeTime = new Intl.RelativeTimeFormat(undefined, { numeric: "auto" });
    const MAX_SHOWN_FILES = 300;

    let folders = [];
    let pickerProviders = []; // providers and their models, for choosing who writes summaries
    let editing = null;
    let pollTimer = 0;
    const fileLists = new Map(); // folder id -> { files, filter, loading } for folders showing their files

    // ---------- Small DOM helpers ----------

    function el(tag, className, text) {
        const element = document.createElement(tag);
        if (className) element.className = className;
        if (text !== undefined) element.textContent = text;
        return element;
    }

    function button(text, classes, onClick) {
        const element = el("button", `btn btn-sm ${classes}`, text);
        element.type = "button";
        element.addEventListener("click", onClick);
        return element;
    }

    const format = n => numbers.format(n);
    const count = (n, noun) => `${format(n)} ${n === 1 ? noun : noun + "s"}`;

    function timeAgo(iso) {
        const seconds = (new Date(iso) - Date.now()) / 1000;
        for (const [unit, size] of [["year", 31536000], ["month", 2592000], ["day", 86400], ["hour", 3600], ["minute", 60]]) {
            if (Math.abs(seconds) >= size) return relativeTime.format(Math.round(seconds / size), unit);
        }
        return "just now";
    }

    // ---------- Folder list ----------

    async function load() {
        try {
            [folders, pickerProviders] = await Promise.all([api.get("/api/folders"), api.get("/api/providers/picker")]);
        } catch (error) {
            toast(error.message);
            return;
        }
        render();
    }

    /** Polled while a folder is being read, so its progress stays current. */
    async function refresh() {
        const wasScanning = new Set(folders.filter(f => f.scan).map(f => f.id));
        try {
            folders = await api.get("/api/folders");
        } catch {
            // Try again on the next poll.
        }

        // A scan that just finished may have changed the files being shown.
        for (const folder of folders) {
            const list = fileLists.get(folder.id);
            if (list && wasScanning.has(folder.id) && !folder.scan) loadFiles(folder.id, list);
        }
        render();
    }

    function render() {
        // Re-rendering replaces the filter boxes, so put the cursor back where it was.
        const focused = document.activeElement?.dataset?.focusKey;
        const selection = focused ? [document.activeElement.selectionStart, document.activeElement.selectionEnd] : null;

        els.list.replaceChildren(...folders.map(renderFolder));
        els.none.classList.toggle("d-none", folders.length > 0);

        if (focused) {
            const input = els.list.querySelector(`[data-focus-key="${focused}"]`);
            input?.focus();
            input?.setSelectionRange(...selection);
        }

        clearTimeout(pollTimer);
        if (folders.some(f => f.scan)) pollTimer = setTimeout(refresh, 1500);
    }

    function upsert(folder) {
        const index = folders.findIndex(f => f.id === folder.id);
        if (index >= 0) folders[index] = folder;
        else folders.push(folder);
        folders.sort((a, b) => a.name.localeCompare(b.name));
        render();
    }

    function renderFolder(folder) {
        const card = el("div", "card");
        const body = el("div", "card-body");

        const header = el("div", "d-flex align-items-center gap-2 flex-wrap");
        const state = describeState(folder);
        const badge = el("span", `badge ${state.badge}`);
        if (state.busy) {
            const spinner = el("span", "spinner-border spinner-border-sm me-1");
            spinner.setAttribute("aria-hidden", "true");
            badge.append(spinner);
        }
        badge.append(state.text);
        header.append(el("h3", "h6 mb-0", folder.name), badge);

        const actions = el("div", "ms-auto d-flex gap-2");
        actions.append(
            button("Check for changes", "btn-outline-primary", () => rescan(folder)),
            button("Edit", "btn-outline-secondary", () => openEditor(folder)),
            button("Remove", "btn-outline-danger", () => removeFolder(folder)));
        header.append(actions);

        body.append(
            header,
            el("div", "small text-body-secondary mt-1 folder-path", folder.path),
            el("div", "small mt-1", describeCounts(folder)));
        if (folder.scanError) body.append(el("div", "alert alert-warning py-2 px-3 mt-2 mb-0 small", folder.scanError));
        body.append(renderFiles(folder));
        card.append(body);
        return card;
    }

    function describeState(folder) {
        const scan = folder.scan;
        if (scan?.phase === "Waiting") return { text: "Waiting", badge: "text-bg-light border", busy: true };
        if (scan?.phase === "Reading") {
            const text = scan.total ? `Reading ${format(scan.done)} of ${count(scan.total, "file")}` : "Looking for files";
            return { text, badge: "text-bg-primary", busy: true };
        }
        if (scan?.phase === "Summarizing") return { text: `Summarizing ${format(scan.done)} of ${format(scan.total)}`, badge: "text-bg-primary", busy: true };
        if (!folder.lastScannedAt) return { text: "Not read yet", badge: "text-bg-light border", busy: false };
        return { text: "Ready", badge: "text-bg-success", busy: false };
    }

    function describeCounts(folder) {
        const parts = [`${count(folder.indexedFiles, "file")} can be searched`];
        if (folder.skippedFiles) parts.push(`${format(folder.skippedFiles)} skipped`);
        if (folder.summariesEnabled) parts.push(`${format(folder.summarizedFiles)} summarized`);
        if (folder.lastScannedAt) parts.push(`checked ${timeAgo(folder.lastScannedAt)}`);
        return parts.join(" · ");
    }

    // ---------- Files ----------

    function renderFiles(folder) {
        const section = el("div", "mt-2");
        const list = fileLists.get(folder.id);
        section.append(button(list ? "Hide files" : "Show files", "btn-link px-0", () => toggleFiles(folder)));
        if (!list) return section;
        if (list.loading) {
            section.append(el("div", "small text-body-secondary", "Loading…"));
            return section;
        }

        const filter = el("input", "form-control form-control-sm mb-2 file-filter");
        filter.type = "search";
        filter.placeholder = "Filter files";
        filter.value = list.filter;
        filter.dataset.focusKey = `filter-${folder.id}`;
        filter.setAttribute("aria-label", `Filter files in ${folder.name}`);

        const rows = el("div", "file-list border rounded");
        const fill = () => {
            list.filter = filter.value;
            const needle = filter.value.trim().toLowerCase();
            const matching = list.files.filter(f => !needle || f.path.toLowerCase().includes(needle) || (f.note ?? "").toLowerCase().includes(needle));
            rows.replaceChildren(...matching.slice(0, MAX_SHOWN_FILES).map(renderFile));
            if (matching.length > MAX_SHOWN_FILES) {
                rows.append(el("div", "small text-body-secondary p-2", `…and ${format(matching.length - MAX_SHOWN_FILES)} more. Filter to find the others.`));
            }
            if (matching.length === 0) {
                rows.append(el("div", "small text-body-secondary p-2", list.files.length ? "No files match." : "No files found yet."));
            }
        };
        filter.addEventListener("input", fill);
        fill();
        section.append(filter, rows);
        return section;
    }

    function renderFile(file) {
        const row = el("div", "file-row");
        const indexed = file.status === "Indexed";
        const details = indexed
            ? [file.pageCount ? count(file.pageCount, "page") : null, `${compactNumbers.format(file.characterCount)} characters`, file.note]
            : [file.note ?? "Skipped"];
        row.append(
            el("span", "file-path", file.path),
            el("span", `file-detail${indexed ? "" : " text-warning-emphasis"}`, details.filter(Boolean).join(" · ")));
        if (file.summary) row.append(el("span", "file-summary", file.summary));
        return row;
    }

    async function toggleFiles(folder) {
        if (fileLists.has(folder.id)) {
            fileLists.delete(folder.id);
            render();
            return;
        }

        const list = { files: [], filter: "", loading: true };
        fileLists.set(folder.id, list);
        render();
        await loadFiles(folder.id, list);
    }

    async function loadFiles(folderId, list) {
        try {
            list.files = await api.get(`/api/folders/${folderId}/files`);
        } catch (error) {
            toast(error.message);
            fileLists.delete(folderId);
        }
        list.loading = false;
        render();
    }

    // ---------- Adding, checking and removing ----------

    // Opens the computer's own folder window, File Explorer's on Windows, and adds the folder chosen.
    els.choose?.addEventListener("click", async () => {
        els.choose.disabled = true;
        try {
            const chosen = await api.post("/api/folders/choose");
            if (!chosen) return; // cancelled

            upsert(chosen.folder);
            if (chosen.added) toast(`Added ${chosen.folder.name}. FolderChat is reading its files.`, "success");
            else toast(`${chosen.folder.name} is already added.`, "info");
        } catch (error) {
            toast(error.message);
        } finally {
            els.choose.disabled = false;
        }
    });

    els.form.addEventListener("submit", async event => {
        event.preventDefault();
        els.add.disabled = true;
        try {
            const folder = await api.post("/api/folders", { path: els.path.value, name: els.name.value.trim() || null });
            els.path.value = "";
            els.name.value = "";
            upsert(folder);
            toast(`Added ${folder.name}. FolderChat is reading its files.`, "success");
        } catch (error) {
            toast(error.message);
        } finally {
            els.add.disabled = false;
        }
    });

    async function rescan(folder) {
        try {
            upsert(await api.post(`/api/folders/${folder.id}/rescan`));
        } catch (error) {
            toast(error.message);
        }
    }

    async function removeFolder(folder) {
        if (!confirm(`Remove ${folder.name}? FolderChat forgets what it read there. The files themselves aren't touched, and past chats stay.`)) return;
        try {
            await api.delete(`/api/folders/${folder.id}`);
            folders = folders.filter(f => f.id !== folder.id);
            fileLists.delete(folder.id);
            render();
        } catch (error) {
            toast(error.message);
        }
    }

    // ---------- Edit dialog ----------

    function openEditor(folder) {
        editing = folder;
        els.editTitle.textContent = `Edit ${folder.name}`;
        els.editName.value = folder.name;
        els.summaries.checked = folder.summariesEnabled;
        fillModels(folder);
        els.editError.classList.add("d-none");
        updateSummaryFields();
        modal.show();
    }

    function fillModels(folder) {
        els.summaryModel.replaceChildren();
        for (const provider of pickerProviders) {
            const group = document.createElement("optgroup");
            group.label = provider.isLocal ? `${provider.name} (local)` : provider.name;
            for (const model of provider.models) group.append(new Option(model.label, `${provider.id}|${model.modelId}`));
            els.summaryModel.append(group);
        }

        const saved = folder.summaryProviderId ? `${folder.summaryProviderId}|${folder.summaryModelId}` : null;
        if (saved && ![...els.summaryModel.options].some(option => option.value === saved)) {
            els.summaryModel.append(new Option(folder.summaryModelId, saved)); // no longer in the picker, but still the choice
        }

        // Default to a local model: summaries read every file, and local models keep them on this computer.
        const local = pickerProviders.find(p => p.isLocal && p.models.length > 0);
        els.summaryModel.value = saved ?? (local ? `${local.id}|${local.models[0].modelId}` : els.summaryModel.options[0]?.value ?? "");
    }

    function selectedModel() {
        const value = els.summaryModel.value ?? "";
        const separator = value.indexOf("|");
        return separator < 0 ? null : { providerId: value.slice(0, separator), modelId: value.slice(separator + 1) };
    }

    function updateSummaryFields() {
        els.summaryModelGroup.classList.toggle("d-none", !els.summaries.checked);
        const selection = selectedModel();
        const provider = selection && pickerProviders.find(p => p.id === selection.providerId);
        els.summaryPrivacy.textContent = !selection
            ? "Add a provider in the section above first."
            : provider?.isLocal
                ? "This model runs on this computer, so the files stay here."
                : `${provider?.name ?? "This provider"} receives the start of every file in the folder.`;
    }

    els.summaries.addEventListener("change", updateSummaryFields);
    els.summaryModel.addEventListener("change", updateSummaryFields);

    els.editForm.addEventListener("submit", async event => {
        event.preventDefault();
        els.editError.classList.add("d-none");
        const selection = els.summaries.checked ? selectedModel() : null;
        const body = {
            name: els.editName.value.trim(),
            summariesEnabled: els.summaries.checked,
            summaryProviderId: selection?.providerId ?? null,
            summaryModelId: selection?.modelId ?? null,
        };

        els.editSave.disabled = true;
        try {
            upsert(await api.put(`/api/folders/${editing.id}`, body));
            modal.hide();
        } catch (error) {
            els.editError.textContent = error.message;
            els.editError.classList.remove("d-none");
        } finally {
            els.editSave.disabled = false;
        }
    });

    load();
})();
