// The Settings page: add, edit and remove providers, and choose which models show in the picker.
(() => {
    const { api, toast } = window.folderChat;

    const els = {
        list: document.getElementById("provider-list"),
        none: document.getElementById("no-providers"),
        add: document.getElementById("add-provider"),
        modal: document.getElementById("provider-modal"),
        form: document.getElementById("provider-form"),
        title: document.getElementById("provider-modal-title"),
        presetGroup: document.getElementById("preset-group"),
        preset: document.getElementById("provider-preset"),
        name: document.getElementById("provider-name"),
        kind: document.getElementById("provider-kind"),
        baseUrl: document.getElementById("provider-base-url"),
        apiKey: document.getElementById("provider-api-key"),
        apiKeyHelp: document.getElementById("api-key-help"),
        kindHint: document.getElementById("kind-hint"),
        enabled: document.getElementById("provider-enabled"),
        notes: document.getElementById("preset-notes"),
        error: document.getElementById("provider-error"),
        save: document.getElementById("provider-save"),
    };

    const modal = bootstrap.Modal.getOrCreateInstance(els.modal);
    const kindLabels = { Anthropic: "Anthropic", OpenAI: "OpenAI", OpenAICompatible: "OpenAI-compatible", Ollama: "Ollama" };
    const filters = new Map(); // provider id -> model filter text, kept across re-renders

    let presets = [];
    let providers = [];
    let editing = null; // the provider in the editor, or null when adding
    let nextInputId = 0;

    // ---------- Small DOM helpers ----------

    function el(tag, className, text) {
        const element = document.createElement(tag);
        if (className) element.className = className;
        if (text !== undefined) element.textContent = text;
        return element;
    }

    function button(text, classes, onClick, type = "button") {
        const element = el("button", `btn btn-sm ${classes}`, text);
        element.type = type;
        if (onClick) element.addEventListener("click", onClick);
        return element;
    }

    function link(href, text) {
        const element = el("a", "", text);
        element.href = href;
        element.target = "_blank";
        element.rel = "noopener noreferrer";
        return element;
    }

    // ---------- Provider list ----------

    async function load() {
        try {
            [presets, providers] = await Promise.all([api.get("/api/providers/presets"), api.get("/api/providers")]);
        } catch (error) {
            toast(error.message);
            return;
        }

        els.preset.replaceChildren(new Option("Custom", ""), ...presets.map(p => new Option(p.name, p.key)));
        render();
    }

    function render() {
        els.list.replaceChildren(...providers.map(renderProvider));
        els.none.classList.toggle("d-none", providers.length > 0);
    }

    function upsert(provider) {
        const index = providers.findIndex(p => p.id === provider.id);
        if (index >= 0) providers[index] = provider;
        else providers.push(provider);
        providers.sort((a, b) => a.name.localeCompare(b.name));
        render();
    }

    function needsKey(provider) {
        return provider.kind !== "Ollama" && !isLocalUrl(provider.baseUrl);
    }

    function isLocalUrl(url) {
        try {
            const host = new URL(url).hostname;
            return host === "localhost" || host === "127.0.0.1" || host === "[::1]";
        } catch {
            return false;
        }
    }

    /** True for this computer, a home or office network address, or a .local name: places an Ollama server can live. */
    function isLocalNetworkUrl(url) {
        try {
            const host = new URL(url).hostname;
            return isLocalUrl(url)
                || host.endsWith(".local")
                || /^10\./.test(host)
                || /^192\.168\./.test(host)
                || /^172\.(1[6-9]|2\d|3[01])\./.test(host);
        } catch {
            return true; // not a URL yet; the server validates it on save
        }
    }

    function renderProvider(provider) {
        const card = el("div", "card");
        const body = el("div", "card-body");

        const header = el("div", "d-flex align-items-center gap-2 flex-wrap");
        header.append(el("h2", "h6 mb-0", provider.name), el("span", "badge text-bg-light border", kindLabels[provider.kind] ?? provider.kind));
        if (!provider.isEnabled) header.append(el("span", "badge text-bg-warning", "Disabled"));

        const actions = el("div", "ms-auto d-flex gap-2");
        const loadButton = button("Load models", "btn-outline-primary", () => refreshModels(provider, loadButton));
        actions.append(
            loadButton,
            button("Edit", "btn-outline-secondary", () => openEditor(provider)),
            button("Delete", "btn-outline-danger", () => deleteProvider(provider)));
        header.append(actions);

        const keyStatus = provider.hasApiKey ? "API key saved" : needsKey(provider) ? "No API key saved" : "No API key needed";
        const details = el("div", "small text-body-secondary mt-1", `${provider.baseUrl ?? "Default endpoint"} · ${keyStatus}`);

        body.append(header, details, renderModels(provider));
        card.append(body);
        return card;
    }

    function renderModels(provider) {
        const section = el("div", "mt-3");
        const enabled = provider.models.filter(m => m.isEnabled).length;

        const toolbar = el("div", "d-flex align-items-center gap-2 flex-wrap mb-2");
        toolbar.append(el("span", "small fw-semibold me-auto", `Models: ${enabled} of ${provider.models.length} shown in the picker`));

        const list = el("div", "model-list border rounded");
        if (provider.models.length > 0) {
            const filter = el("input", "form-control form-control-sm model-filter");
            filter.type = "search";
            filter.placeholder = "Filter models";
            filter.setAttribute("aria-label", `Filter ${provider.name} models`);
            filter.value = filters.get(provider.id) ?? "";
            filter.addEventListener("input", () => {
                filters.set(provider.id, filter.value);
                applyFilter(list, filter.value);
            });
            toolbar.append(
                filter,
                button("Show all", "btn-outline-secondary", () => mutate(provider, () => api.put(`/api/providers/${provider.id}/models/all`, { isEnabled: true }))),
                button("Hide all", "btn-outline-secondary", () => mutate(provider, () => api.put(`/api/providers/${provider.id}/models/all`, { isEnabled: false }))));
        } else {
            list.append(el("div", "small text-body-secondary p-2", "No models yet. Click Load models, or add a model ID below."));
        }

        for (const model of provider.models) {
            const row = el("div", "form-check model-row");
            row.dataset.search = `${model.modelId} ${model.displayName ?? ""}`.toLowerCase();

            const checkbox = el("input", "form-check-input");
            checkbox.type = "checkbox";
            checkbox.checked = model.isEnabled;
            checkbox.id = `model-${nextInputId++}`;
            checkbox.addEventListener("change", () =>
                mutate(provider, () => api.put(`/api/providers/${provider.id}/models`, { modelId: model.modelId, isEnabled: checkbox.checked })));

            const label = el("label", "form-check-label");
            label.htmlFor = checkbox.id;
            label.append(el("span", "", model.displayName ?? model.modelId));
            if (model.displayName && model.displayName !== model.modelId) label.append(el("span", "text-body-secondary small ms-2", model.modelId));
            if (model.isManual) label.append(el("span", "badge text-bg-light border ms-2", "added by hand"));

            row.append(checkbox, label);
            if (model.isManual) {
                const remove = el("button", "icon-btn", "✕");
                remove.type = "button";
                remove.title = "Remove";
                remove.setAttribute("aria-label", `Remove ${model.modelId}`);
                remove.addEventListener("click", () =>
                    mutate(provider, () => api.post(`/api/providers/${provider.id}/models/remove`, { modelId: model.modelId })));
                row.append(remove);
            }
            list.append(row);
        }

        section.append(toolbar, list);
        applyFilter(list, filters.get(provider.id) ?? "");

        const addForm = el("form", "d-flex gap-2 mt-2");
        const input = el("input", "form-control form-control-sm");
        input.placeholder = "Add a model ID by hand, for example deepseek-chat";
        input.maxLength = 200;
        input.setAttribute("aria-label", `Add a model to ${provider.name}`);
        addForm.append(input, button("Add", "btn-outline-primary", null, "submit"));
        addForm.addEventListener("submit", event => {
            event.preventDefault();
            const modelId = input.value.trim();
            if (modelId) mutate(provider, () => api.post(`/api/providers/${provider.id}/models`, { modelId }));
        });
        section.append(addForm);
        return section;
    }

    function applyFilter(list, text) {
        const needle = text.trim().toLowerCase();
        for (const row of list.querySelectorAll(".model-row")) {
            row.classList.toggle("d-none", needle !== "" && !row.dataset.search.includes(needle));
        }
    }

    async function mutate(provider, action) {
        try {
            upsert(await action());
        } catch (error) {
            toast(error.message);
            render(); // undo the checkbox the user just clicked
        }
    }

    async function refreshModels(provider, trigger, quiet = false) {
        if (trigger) {
            trigger.disabled = true;
            trigger.textContent = "Loading…";
        }

        try {
            const updated = await api.post(`/api/providers/${provider.id}/models/refresh`);
            upsert(updated);
            if (!quiet) toast(`Loaded ${updated.models.length} models from ${updated.name}.`, "success");
        } catch (error) {
            toast(error.message, quiet ? "warning" : "danger");
            if (trigger) {
                trigger.disabled = false;
                trigger.textContent = "Load models";
            }
        }
    }

    async function deleteProvider(provider) {
        if (!confirm(`Delete ${provider.name}? Its saved API key is deleted too. Past conversations stay.`)) return;
        try {
            await api.delete(`/api/providers/${provider.id}`);
            providers = providers.filter(p => p.id !== provider.id);
            render();
        } catch (error) {
            toast(error.message);
        }
    }

    // ---------- Add / edit dialog ----------

    function openEditor(provider) {
        editing = provider ?? null;
        els.title.textContent = provider ? `Edit ${provider.name}` : "Add provider";
        els.presetGroup.classList.toggle("d-none", Boolean(provider));
        els.preset.value = "";
        els.name.value = provider?.name ?? "";
        els.kind.value = provider?.kind ?? "OpenAICompatible";
        els.baseUrl.value = provider?.baseUrl ?? "";
        els.apiKey.value = "";
        els.apiKey.placeholder = provider?.hasApiKey ? "Saved. Type a new key to replace it." : "";
        els.enabled.checked = provider?.isEnabled ?? true;
        els.notes.replaceChildren();
        els.error.classList.add("d-none");
        updateKeyHelp();
        modal.show();
    }

    function updateKeyHelp() {
        const kind = els.kind.value;
        const parts = [];
        if (editing?.hasApiKey) parts.push("Leave blank to keep the saved key, or type a single space to remove it.");
        if (kind === "Ollama") parts.push("Ollama doesn't use an API key.");
        else if (kind === "Anthropic") parts.push("If blank, the ANTHROPIC_API_KEY environment variable is used.");
        else if (kind === "OpenAI") parts.push("If blank, the OPENAI_API_KEY environment variable is used.");
        else parts.push("Local servers such as LM Studio don't need one.");
        els.apiKeyHelp.textContent = parts.join(" ");
        updateKindHint();
    }

    // Catches the easy mix-up of saving a cloud API (such as DeepSeek) with the Ollama type, which never sends a key.
    function updateKindHint() {
        const url = els.baseUrl.value.trim();
        const suspicious = els.kind.value === "Ollama" && url !== "" && !isLocalNetworkUrl(url);
        els.kindHint.textContent = suspicious
            ? "The Ollama type is for Ollama servers and never sends an API key. For a cloud API such as DeepSeek, choose OpenAI-compatible."
            : "";
        els.kindHint.classList.toggle("d-none", !suspicious);
    }

    els.preset.addEventListener("change", () => {
        const preset = presets.find(p => p.key === els.preset.value);
        els.notes.replaceChildren();
        if (!preset) return;

        els.name.value = preset.name;
        els.kind.value = preset.kind;
        els.baseUrl.value = preset.baseUrl ?? "";
        if (preset.notes) els.notes.append(`${preset.notes} `);
        if (preset.apiKeyUrl) els.notes.append(link(preset.apiKeyUrl, "Get an API key"));
        updateKeyHelp();
    });
    els.kind.addEventListener("change", updateKeyHelp);
    els.baseUrl.addEventListener("input", updateKindHint);

    els.form.addEventListener("submit", async event => {
        event.preventDefault();
        els.error.classList.add("d-none");

        // The API treats null as "keep the saved key" and an empty string as "remove it".
        const typedKey = els.apiKey.value;
        let apiKey = typedKey.trim() === "" ? null : typedKey.trim();
        if (editing && typedKey.length > 0 && typedKey.trim() === "") apiKey = "";

        const body = {
            name: els.name.value.trim(),
            kind: els.kind.value,
            baseUrl: els.baseUrl.value.trim() || null,
            apiKey,
            isEnabled: els.enabled.checked,
        };

        els.save.disabled = true;
        try {
            const saved = editing
                ? await api.put(`/api/providers/${editing.id}`, body)
                : await api.post("/api/providers", body);
            const isNew = !editing;
            upsert(saved);
            modal.hide();
            if (isNew) await refreshModels(saved, null, true);
        } catch (error) {
            els.error.textContent = error.message;
            els.error.classList.remove("d-none");
        } finally {
            els.save.disabled = false;
        }
    });

    els.add.addEventListener("click", () => openEditor(null));
    load();
})();
