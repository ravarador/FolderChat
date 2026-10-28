// The Settings page's API keys: create a key for another app, see when each was last used, and revoke them.
(() => {
    const { api, toast } = window.folderChat;

    const els = {
        form: document.getElementById("api-key-form"),
        name: document.getElementById("api-key-name"),
        create: document.getElementById("api-key-create"),
        created: document.getElementById("api-key-new"),
        secret: document.getElementById("api-key-secret"),
        copy: document.getElementById("api-key-copy"),
        table: document.getElementById("api-key-table"),
        rows: document.getElementById("api-key-rows"),
        none: document.getElementById("no-api-keys"),
    };

    const dates = new Intl.DateTimeFormat(undefined, { dateStyle: "medium", timeStyle: "short" });
    let keys = [];

    function cell(text, className) {
        const element = document.createElement("td");
        if (className) element.className = className;
        element.textContent = text;
        return element;
    }

    function row(key) {
        const tr = document.createElement("tr");
        const revoke = document.createElement("button");
        revoke.type = "button";
        revoke.className = "btn btn-sm btn-outline-danger";
        revoke.textContent = "Revoke";
        revoke.addEventListener("click", () => revokeKey(key));
        const actions = cell("", "text-end");
        actions.append(revoke);

        tr.append(
            cell(key.name),
            cell(`${key.prefix}…`, "font-monospace small"),
            cell(dates.format(new Date(key.createdAt)), "small"),
            cell(key.lastUsedAt ? dates.format(new Date(key.lastUsedAt)) : "Never", "small"),
            actions);
        return tr;
    }

    function render() {
        els.rows.replaceChildren(...keys.map(row));
        els.table.classList.toggle("d-none", keys.length === 0);
        els.none.classList.toggle("d-none", keys.length > 0);
    }

    async function load() {
        try {
            keys = await api.get("/api/api-keys");
            render();
        } catch (error) {
            toast(error.message);
        }
    }

    async function revokeKey(key) {
        if (!confirm(`Revoke "${key.name}"? Apps using it stop working until they get a new key.`)) return;
        try {
            await api.delete(`/api/api-keys/${key.id}`);
            keys = keys.filter(k => k.id !== key.id);
            render();
        } catch (error) {
            toast(error.message);
        }
    }

    els.form.addEventListener("submit", async event => {
        event.preventDefault();
        els.create.disabled = true;
        try {
            const created = await api.post("/api/api-keys", { name: els.name.value });
            keys.unshift(created.key);
            render();
            els.name.value = "";
            els.secret.value = created.secret;
            els.created.classList.remove("d-none");
            els.secret.select();
        } catch (error) {
            toast(error.message);
        } finally {
            els.create.disabled = false;
        }
    });

    els.copy.addEventListener("click", async () => {
        try {
            await navigator.clipboard.writeText(els.secret.value);
            toast("Key copied.", "success");
        } catch {
            els.secret.select();
            toast("Couldn't copy. The key is selected: press Ctrl+C or Command+C.", "warning");
        }
    });

    load();
})();
