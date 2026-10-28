// The Settings page's usage table: tokens used by chat replies this month, by model.
(() => {
    const { api, toast } = window.folderChat;

    const els = {
        refresh: document.getElementById("usage-refresh"),
        since: document.getElementById("usage-since"),
        table: document.getElementById("usage-table"),
        rows: document.getElementById("usage-rows"),
        total: document.getElementById("usage-total"),
        none: document.getElementById("no-usage"),
    };

    const numbers = new Intl.NumberFormat();
    const dates = new Intl.DateTimeFormat(undefined, { dateStyle: "long" });

    function cell(text, className) {
        const element = document.createElement("td");
        if (className) element.className = className;
        element.textContent = text;
        return element;
    }

    function row(model, totals, isTotal) {
        const tr = document.createElement("tr");
        const name = isTotal ? cell("Total", "fw-semibold") : cell("");
        if (!isTotal) {
            const provider = document.createElement("span");
            provider.className = "text-body-secondary";
            provider.textContent = `${model.providerName} · `;
            name.append(provider, model.modelId);
        }
        tr.append(
            name,
            cell(numbers.format(totals.replies), "text-end"),
            cell(numbers.format(totals.inputTokens), "text-end"),
            cell(numbers.format(totals.outputTokens), "text-end"),
            cell(totals.cachedInputTokens ? numbers.format(totals.cachedInputTokens) : "—", "text-end"),
            cell(totals.reasoningTokens ? numbers.format(totals.reasoningTokens) : "—", "text-end"));
        return tr;
    }

    async function load() {
        els.refresh.disabled = true;
        try {
            const report = await api.get("/api/usage");
            els.since.textContent = dates.format(new Date(report.since));
            els.rows.replaceChildren(...report.models.map(model => row(model, model, false)));

            const sum = key => report.models.reduce((total, model) => total + model[key], 0);
            const totals = {
                replies: sum("replies"),
                inputTokens: sum("inputTokens"),
                outputTokens: sum("outputTokens"),
                cachedInputTokens: sum("cachedInputTokens"),
                reasoningTokens: sum("reasoningTokens"),
            };
            els.total.replaceChildren(...(report.models.length > 1 ? [row(null, totals, true)] : []));

            els.table.classList.toggle("d-none", report.models.length === 0);
            els.none.classList.toggle("d-none", report.models.length > 0);
        } catch (error) {
            toast(error.message);
        } finally {
            els.refresh.disabled = false;
        }
    }

    els.refresh.addEventListener("click", load);
    load();
})();
