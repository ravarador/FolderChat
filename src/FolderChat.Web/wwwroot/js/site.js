// Shared by every page: theme switching, API calls and toast messages.
(() => {
    const root = document.documentElement;

    function applyCodeTheme() {
        const dark = root.dataset.bsTheme === "dark";
        const light = document.getElementById("hljs-light");
        const darkSheet = document.getElementById("hljs-dark");
        if (light) light.disabled = dark;
        if (darkSheet) darkSheet.disabled = !dark;
    }

    applyCodeTheme();
    document.getElementById("theme-toggle")?.addEventListener("click", () => {
        const next = root.dataset.bsTheme === "dark" ? "light" : "dark";
        root.dataset.bsTheme = next;
        try {
            localStorage.setItem("folderchat.theme", next);
        } catch {
            // Storage is unavailable (private window); the theme just won't be remembered.
        }
        applyCodeTheme();
    });

    // Sent with every change request. The server rejects changes without it.
    const csrfToken = document.querySelector('meta[name="csrf-token"]')?.content ?? "";

    async function readError(response) {
        try {
            const problem = await response.json();
            if (problem.detail) return problem.detail;
            if (problem.errors) return Object.values(problem.errors).flat().join(" ");
            if (problem.title) return problem.title;
        } catch {
            // Not JSON; fall through to the status code.
        }
        return `Request failed (${response.status}).`;
    }

    async function request(method, url, body) {
        const init = { method, headers: { Accept: "application/json" } };
        if (method !== "GET") init.headers["X-CSRF-TOKEN"] = csrfToken;
        if (body instanceof FormData) {
            init.body = body; // the browser sets the multipart Content-Type, with its boundary
        } else if (body !== undefined) {
            init.headers["Content-Type"] = "application/json";
            init.body = JSON.stringify(body);
        }

        const response = await fetch(url, init);
        if (!response.ok) throw new Error(await readError(response));
        return response.status === 204 ? null : response.json();
    }

    function toast(message, kind = "danger") {
        const element = document.createElement("div");
        element.className = `toast align-items-center text-bg-${kind} border-0`;
        element.setAttribute("role", kind === "danger" ? "alert" : "status");

        const row = document.createElement("div");
        row.className = "d-flex";
        const text = document.createElement("div");
        text.className = "toast-body";
        text.textContent = message;
        const close = document.createElement("button");
        close.type = "button";
        close.className = `btn-close ${["warning", "info"].includes(kind) ? "" : "btn-close-white"} me-2 m-auto`; // dark on light colors
        close.setAttribute("data-bs-dismiss", "toast");
        close.setAttribute("aria-label", "Close");
        row.append(text, close);
        element.append(row);

        document.getElementById("toasts").append(element);
        element.addEventListener("hidden.bs.toast", () => element.remove());
        new bootstrap.Toast(element, { delay: kind === "danger" ? 8000 : 4000 }).show();
    }

    // Installing as an app. Browsers only allow it on localhost or HTTPS, so on another address nothing shows.
    if ("serviceWorker" in navigator) {
        navigator.serviceWorker.register("/sw.js").catch(error => console.warn("FolderChat can't be installed as an app here.", error));
    }

    // Chrome and Edge offer installing through this event. Other browsers have their own menu item, or none.
    const installButton = document.getElementById("install-app");
    let installPrompt = null;
    window.addEventListener("beforeinstallprompt", event => {
        event.preventDefault();
        installPrompt = event;
        installButton?.classList.remove("d-none");
    });
    installButton?.addEventListener("click", async () => {
        if (!installPrompt) return;
        installPrompt.prompt();
        await installPrompt.userChoice;
        installPrompt = null;
        installButton.classList.add("d-none");
    });
    window.addEventListener("appinstalled", () => {
        installPrompt = null;
        installButton?.classList.add("d-none");
    });

    window.folderChat = {
        csrfToken,
        readError,
        toast,
        api: {
            get: url => request("GET", url),
            post: (url, body) => request("POST", url, body ?? {}),
            put: (url, body) => request("PUT", url, body),
            patch: (url, body) => request("PATCH", url, body),
            delete: url => request("DELETE", url),
            upload: (url, formData) => request("POST", url, formData),
        },
    };
})();
