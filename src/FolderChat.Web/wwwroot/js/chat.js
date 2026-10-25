// The chat page: model picker, conversation list, each chat's folders, attached files, and streamed replies with their sources.
(() => {
    const { api, toast, csrfToken, readError } = window.folderChat;
    const markdown = window.folderChatMarkdown;
    const MODEL_KEY = "folderchat.model";

    const els = {
        list: document.getElementById("conversation-list"),
        search: document.getElementById("conversation-search"),
        newChat: document.getElementById("new-chat"),
        main: document.getElementById("chat-main"),
        picker: document.getElementById("model-picker"),
        privacy: document.getElementById("privacy-badge"),
        title: document.getElementById("chat-title"),
        messages: document.getElementById("messages"),
        empty: document.getElementById("empty-state"),
        emptyText: document.getElementById("empty-state-text"),
        dropZone: document.getElementById("drop-zone"),
        composer: document.getElementById("composer"),
        folderBar: document.getElementById("folder-bar"),
        folderChips: document.getElementById("folder-chips"),
        effortPicker: document.getElementById("effort-picker"),
        foldersButton: document.getElementById("folders-button"),
        folderMenu: document.getElementById("folder-menu"),
        newFolderModal: document.getElementById("new-folder-modal"),
        newFolderForm: document.getElementById("new-folder-form"),
        newFolderPath: document.getElementById("new-folder-path"),
        newFolderName: document.getElementById("new-folder-name"),
        newFolderError: document.getElementById("new-folder-error"),
        newFolderSave: document.getElementById("new-folder-save"),
        tray: document.getElementById("attachment-tray"),
        attach: document.getElementById("attach"),
        fileInput: document.getElementById("file-input"),
        prompt: document.getElementById("prompt"),
        send: document.getElementById("send"),
        stop: document.getElementById("stop"),
        previewModal: document.getElementById("preview-modal"),
        previewTitle: document.getElementById("preview-title"),
        previewInfo: document.getElementById("preview-info"),
        previewText: document.getElementById("preview-text"),
        previewFooter: document.getElementById("preview-footer"),
        previewReveal: document.getElementById("preview-reveal"),
    };

    // The server's limits, rendered into the page.
    const limits = {
        fileSize: Number(els.composer.dataset.maxFileSize),
        files: Number(els.composer.dataset.maxFiles),
        folders: Number(els.composer.dataset.maxFolders),
    };

    // Whether the server can open the computer's own folder window for this browser. If not, folders are added by path.
    const canChooseFolders = els.composer.dataset.chooseFolders === "true";

    const state = {
        conversations: [],
        currentId: null, // null means a new, unsaved chat
        providers: [], // providers with enabled models, from /api/providers/picker
        folders: [], // every knowledge folder, from /api/folders
        chatFolders: [], // the IDs of the folders the open chat answers from
        chatEffort: "Standard", // how hard the open chat searches them
        choosingFolder: false, // the computer's folder window is open
        streaming: null, // the AbortController of the reply being streamed
        attachments: [], // files in the composer: { name, status: "uploading" | "ready" | "failed", attachment, error }
    };

    const previewModal = bootstrap.Modal.getOrCreateInstance(els.previewModal);
    const newFolderModal = bootstrap.Modal.getOrCreateInstance(els.newFolderModal);
    const compactNumber = new Intl.NumberFormat(undefined, { notation: "compact", maximumFractionDigits: 1 });
    let previewRequest = 0;
    let previewReveal = null; // what the preview's "Show in folder" button does, when it's shown

    // ---------- Model picker ----------

    async function loadPicker() {
        state.providers = await api.get("/api/providers/picker");
        els.picker.replaceChildren();

        for (const provider of state.providers) {
            const group = document.createElement("optgroup");
            group.label = provider.isLocal ? `${provider.name} (local)` : provider.name;
            for (const model of provider.models) {
                group.append(new Option(model.label, `${provider.id}|${model.modelId}`));
            }
            els.picker.append(group);
        }

        els.picker.disabled = state.providers.length === 0;
        if (state.providers.length === 0) {
            els.picker.append(new Option("No models yet", ""));
        } else {
            selectModel(readStored(MODEL_KEY));
        }

        updatePrivacyBadge();
        updateEmptyState();
    }

    function selectModel(value) {
        if (value && [...els.picker.options].some(option => option.value === value)) {
            els.picker.value = value;
        } else if (els.picker.options.length > 0) {
            els.picker.selectedIndex = 0;
        }
    }

    function currentSelection() {
        const value = els.picker.value;
        const separator = value ? value.indexOf("|") : -1;
        if (separator < 0) return null;
        return { providerId: value.slice(0, separator), modelId: value.slice(separator + 1) };
    }

    function updatePrivacyBadge() {
        const selection = currentSelection();
        const provider = selection && state.providers.find(p => p.id === selection.providerId);
        if (!provider) {
            els.privacy.className = "badge rounded-pill d-none";
            return;
        }

        els.privacy.className = `badge rounded-pill ${provider.isLocal ? "text-bg-success" : "text-bg-secondary"}`;
        els.privacy.textContent = provider.isLocal ? "Stays on this computer" : `Sent to ${provider.name}`;
        els.privacy.title = provider.isLocal
            ? "Your messages, and any files and folder passages with them, stay on this computer."
            : `Your messages, and any files and folder passages with them, are sent to ${provider.name}.`;
    }

    els.picker.addEventListener("change", () => {
        remember(MODEL_KEY, els.picker.value);
        updatePrivacyBadge();
    });

    function remember(key, value) {
        try {
            localStorage.setItem(key, value);
        } catch {
            // Not remembered; harmless.
        }
    }

    function readStored(key) {
        try {
            return localStorage.getItem(key);
        } catch {
            return null;
        }
    }

    // ---------- This chat's folders ----------

    async function loadFolders() {
        state.folders = await api.get("/api/folders");
        renderFolderControls();
    }

    /** Sets the folders the open chat answers from. Folders removed since are left out. */
    function setChatFolders(folderIds, effort) {
        state.chatFolders = (folderIds ?? []).filter(id => state.folders.some(f => f.id === id));
        state.chatEffort = effort ?? "Standard";
        renderFolderControls();
    }

    function renderFolderControls() {
        const chosen = state.chatFolders.map(id => state.folders.find(f => f.id === id)).filter(Boolean);
        els.folderChips.replaceChildren(...chosen.map(folderChip));
        els.folderBar.classList.toggle("d-none", chosen.length === 0);
        els.effortPicker.value = state.chatEffort;
        els.effortPicker.title = els.effortPicker.selectedOptions[0]?.title ?? "";
        els.foldersButton.textContent = chosen.length ? `Folders (${chosen.length})` : "Folders";
        renderFolderMenu();
        watchFolders();
    }

    function isReading(folder) {
        return Boolean(folder.scan) && folder.indexedFiles === 0;
    }

    function describeFolder(folder) {
        return isReading(folder) ? "reading…" : `${compactNumber.format(folder.indexedFiles)} ${folder.indexedFiles === 1 ? "file" : "files"}`;
    }

    function folderChip(folder) {
        const chip = document.createElement("span");
        chip.className = "folder-chip";
        chip.title = folder.path;
        const name = document.createElement("span");
        name.className = "folder-chip-name";
        name.textContent = folder.name;
        const detail = document.createElement("span");
        detail.className = "folder-chip-detail";
        detail.textContent = describeFolder(folder);
        const remove = iconButton(`Stop answering from ${folder.name}`, "✕", () => toggleFolder(folder.id, false));
        remove.title = "Remove from this chat";
        remove.disabled = Boolean(state.streaming);
        chip.append(name, detail, remove);
        return chip;
    }

    /** The Folders menu: a checkbox for every folder, then ways to add and manage folders. */
    function renderFolderMenu() {
        const header = document.createElement("h6");
        header.className = "dropdown-header";
        header.textContent = "Choose folders for this chat to answer from";
        const items = [header];

        if (state.folders.length === 0) {
            const none = document.createElement("div");
            none.className = "dropdown-item-text small text-body-secondary";
            none.textContent = "No folders yet.";
            items.push(none);
        } else if (state.chatFolders.length === 0) {
            const none = document.createElement("div");
            none.className = "dropdown-item-text small text-body-secondary";
            none.textContent = "None chosen yet. Tick a folder to use it.";
            items.push(none);
        }

        for (const folder of state.folders) {
            const option = document.createElement("label");
            option.className = "dropdown-item folder-option";
            option.title = folder.path;
            const checkbox = document.createElement("input");
            checkbox.type = "checkbox";
            checkbox.className = "form-check-input";
            checkbox.checked = state.chatFolders.includes(folder.id);
            checkbox.addEventListener("change", () => toggleFolder(folder.id, checkbox.checked));
            const name = document.createElement("span");
            name.className = "folder-option-name";
            name.textContent = folder.name;
            const detail = document.createElement("span");
            detail.className = "folder-option-detail";
            detail.textContent = describeFolder(folder);
            option.append(checkbox, name, detail);
            items.push(option);
        }

        const divider = document.createElement("hr");
        divider.className = "dropdown-divider";
        const add = document.createElement("button");
        add.type = "button";
        add.className = "dropdown-item";
        add.textContent = "Add a folder…";
        add.addEventListener("click", canChooseFolders ? chooseFolder : openNewFolder);
        const manage = document.createElement("a");
        manage.className = "dropdown-item";
        manage.href = "/Home/Settings#folders-heading";
        manage.textContent = "Manage folders";
        items.push(divider, add, manage);

        els.folderMenu.replaceChildren(...items);
    }

    async function toggleFolder(folderId, on) {
        if (on && !state.chatFolders.includes(folderId)) {
            if (state.chatFolders.length >= limits.folders) {
                toast(`A chat can answer from up to ${limits.folders} folders.`, "warning");
                renderFolderControls();
                return;
            }
            state.chatFolders = [...state.chatFolders, folderId];
        } else if (!on) {
            state.chatFolders = state.chatFolders.filter(id => id !== folderId);
        }
        renderFolderControls();
        await saveChatFolders();
    }

    /** Saves the open chat's folders and effort. A new chat saves them with its first message instead. */
    async function saveChatFolders() {
        if (!state.currentId) return;
        try {
            await api.put(`/api/conversations/${state.currentId}/folders`, { folderIds: state.chatFolders, effort: state.chatEffort });
        } catch (error) {
            toast(error.message);
        }
    }

    els.effortPicker.addEventListener("change", () => {
        state.chatEffort = els.effortPicker.value;
        renderFolderControls();
        saveChatFolders();
    });

    // While a folder this chat uses is still being read, refresh the list so its chip shows when it's ready.
    let folderWatch = 0;
    function watchFolders() {
        clearTimeout(folderWatch);
        if (!state.folders.some(f => f.scan && state.chatFolders.includes(f.id))) return;
        folderWatch = setTimeout(async () => {
            try {
                state.folders = await api.get("/api/folders");
            } catch {
                // Try again on the next round.
            }
            renderFolderControls();
        }, 2000);
    }

    /** Opens the computer's own folder window, File Explorer's on Windows, and has this chat use the folder chosen. */
    async function chooseFolder() {
        bootstrap.Dropdown.getOrCreateInstance(els.foldersButton).hide();
        state.choosingFolder = true;
        els.foldersButton.disabled = true;
        try {
            const chosen = await api.post("/api/folders/choose");
            if (!chosen) return; // cancelled

            const { folder, added } = chosen;
            state.folders = [...state.folders.filter(f => f.id !== folder.id), folder].sort((a, b) => a.name.localeCompare(b.name));
            await toggleFolder(folder.id, true);
            if (added && state.chatFolders.includes(folder.id)) {
                toast(`Added ${folder.name}. FolderChat is reading its files, so this chat can use them in a moment.`, "success");
            }
        } catch (error) {
            toast(error.message);
        } finally {
            state.choosingFolder = false;
            els.foldersButton.disabled = Boolean(state.streaming);
        }
    }

    /** Adds a folder by its path, where the server can't open a folder window for this browser. */
    function openNewFolder() {
        bootstrap.Dropdown.getOrCreateInstance(els.foldersButton).hide();
        els.newFolderPath.value = "";
        els.newFolderName.value = "";
        els.newFolderError.classList.add("d-none");
        newFolderModal.show();
    }

    els.newFolderModal.addEventListener("shown.bs.modal", () => els.newFolderPath.focus());

    els.newFolderForm.addEventListener("submit", async event => {
        event.preventDefault();
        els.newFolderError.classList.add("d-none");
        els.newFolderSave.disabled = true;
        try {
            const folder = await api.post("/api/folders", { path: els.newFolderPath.value, name: els.newFolderName.value.trim() || null });
            state.folders = [...state.folders, folder].sort((a, b) => a.name.localeCompare(b.name));
            newFolderModal.hide();
            await toggleFolder(folder.id, true);
            toast(`Added ${folder.name}. FolderChat is reading its files, so this chat can use them in a moment.`, "success");
        } catch (error) {
            els.newFolderError.textContent = error.message;
            els.newFolderError.classList.remove("d-none");
        } finally {
            els.newFolderSave.disabled = false;
        }
    });

    // ---------- Conversation list ----------

    async function loadConversations() {
        state.conversations = await api.get("/api/conversations");
        renderConversationList();
    }

    function renderConversationList() {
        const filter = els.search.value.trim().toLowerCase();
        els.list.replaceChildren();

        for (const conversation of state.conversations) {
            if (filter && !conversation.title.toLowerCase().includes(filter)) continue;

            const item = document.createElement("li");
            item.className = "conversation-item" + (conversation.id === state.currentId ? " active" : "");
            item.title = conversation.title;

            const title = document.createElement("span");
            title.className = "title";
            title.textContent = conversation.title;

            const actions = document.createElement("span");
            actions.className = "actions";
            actions.append(
                iconButton("Rename", "✎", () => renameConversation(conversation)),
                iconButton("Delete", "✕", () => deleteConversation(conversation)));

            item.append(title, actions);
            item.addEventListener("click", () => openConversation(conversation.id));
            els.list.append(item);
        }
    }

    function iconButton(label, glyph, onClick) {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "icon-btn";
        button.title = label;
        button.setAttribute("aria-label", label);
        button.textContent = glyph;
        button.addEventListener("click", event => {
            event.stopPropagation();
            onClick();
        });
        return button;
    }

    function moveToTop(conversationId) {
        const index = state.conversations.findIndex(c => c.id === conversationId);
        if (index > 0) {
            const [conversation] = state.conversations.splice(index, 1);
            state.conversations.unshift(conversation);
        }
    }

    async function openConversation(id) {
        if (state.streaming) return; // finish or stop the current reply first

        try {
            const conversation = await api.get(`/api/conversations/${id}`);
            state.currentId = conversation.id;
            els.title.textContent = conversation.title;
            els.messages.replaceChildren();
            for (const message of conversation.messages) appendMessage(message);

            if (conversation.providerId && conversation.modelId) {
                selectModel(`${conversation.providerId}|${conversation.modelId}`);
                updatePrivacyBadge();
            }
            setChatFolders(conversation.folderIds, conversation.effort);

            history.replaceState(null, "", `#${conversation.id}`);
            renderConversationList();
            updateEmptyState();
            scrollToBottom(true);
        } catch (error) {
            toast(error.message);
        }
    }

    function newChat() {
        if (state.streaming) return;
        state.currentId = null;
        setChatFolders([], "Standard"); // each chat chooses its own folders
        els.title.textContent = "";
        els.messages.replaceChildren();
        history.replaceState(null, "", location.pathname);
        renderConversationList();
        updateEmptyState();
        els.prompt.focus();
    }

    async function renameConversation(conversation) {
        const title = prompt("Rename chat", conversation.title)?.trim();
        if (!title || title === conversation.title) return;

        try {
            const updated = await api.patch(`/api/conversations/${conversation.id}`, { title });
            conversation.title = updated.title;
            if (state.currentId === conversation.id) els.title.textContent = updated.title;
            renderConversationList();
        } catch (error) {
            toast(error.message);
        }
    }

    async function deleteConversation(conversation) {
        if (!confirm(`Delete "${conversation.title}"? This can't be undone.`)) return;

        try {
            await api.delete(`/api/conversations/${conversation.id}`);
            state.conversations = state.conversations.filter(c => c.id !== conversation.id);
            if (state.currentId === conversation.id) newChat();
            else renderConversationList();
        } catch (error) {
            toast(error.message);
        }
    }

    // ---------- Messages ----------

    function appendMessage(message) {
        const wrapper = document.createElement("div");
        wrapper.className = `message message-${message.role === "User" ? "user" : "assistant"}`;

        if (message.role === "User") {
            if (message.attachments?.length) {
                const files = document.createElement("div");
                files.className = "message-attachments";
                files.append(...message.attachments.map(attachment => attachmentChip({ name: attachment.fileName, status: "ready", attachment })));
                wrapper.append(files);
            }
            if (message.content) {
                const bubble = document.createElement("div");
                bubble.className = "bubble";
                bubble.textContent = message.content;
                wrapper.append(bubble);
            }
        } else {
            const meta = document.createElement("div");
            meta.className = "message-meta";
            meta.textContent = describeModel(message);

            const content = document.createElement("div");
            content.className = "content markdown";
            wrapper.append(meta, content);

            if (message.content) {
                markdown.render(content, message.content);
                markdown.enhance(content);
            }
            renderSources(wrapper, message.sources, message.content);
            if (message.status === "Stopped") addNote(wrapper, "Stopped before the reply finished.");
            if (message.status === "Failed") addError(wrapper, message.error || "The reply failed.");
        }

        els.messages.append(wrapper);
        return wrapper;
    }

    function describeModel(message) {
        let text = [message.providerName, message.modelId].filter(Boolean).join(" · ");
        if (message.outputTokens) text += ` · ${message.outputTokens.toLocaleString()} tokens`;
        return text;
    }

    function addNote(wrapper, text) {
        const note = document.createElement("div");
        note.className = "message-note";
        note.textContent = text;
        wrapper.append(note);
    }

    function addError(wrapper, text) {
        const alert = document.createElement("div");
        alert.className = "alert alert-danger py-2 px-3 mt-2 mb-0 small";
        alert.textContent = text;
        wrapper.append(alert);
    }

    function updateEmptyState() {
        els.empty.classList.toggle("d-none", els.messages.children.length > 0);
        if (state.providers.length === 0) {
            const link = document.createElement("a");
            link.href = "/Home/Settings";
            link.textContent = "Add a provider in Settings";
            els.emptyText.replaceChildren("No models are set up yet. ", link, ".");
        } else {
            els.emptyText.textContent = "Pick a model above and send a message. Attach files, or choose Folders to answer from your own documents.";
        }
    }

    function isNearBottom() {
        const list = els.messages;
        return list.scrollHeight - list.scrollTop - list.clientHeight < 80;
    }

    function scrollToBottom(force) {
        if (force || isNearBottom()) els.messages.scrollTop = els.messages.scrollHeight;
    }

    // ---------- Attached files ----------

    /** Uploads files as soon as they're picked, so a file that can't be read is reported before sending. */
    function addFiles(files) {
        for (const file of files) {
            if (state.attachments.length >= limits.files) {
                toast(`You can attach up to ${limits.files} files to a message.`, "warning");
                break;
            }
            if (file.size > limits.fileSize) {
                toast(`${file.name} is larger than ${limits.fileSize / (1024 * 1024)} MB.`, "warning");
                continue;
            }

            const item = { name: file.name, status: "uploading", attachment: null, error: null };
            state.attachments.push(item);
            upload(item, file);
        }
        renderTray();
    }

    async function upload(item, file) {
        const form = new FormData();
        form.append("file", file, file.name);
        try {
            item.attachment = await api.upload("/api/attachments", form);
            item.status = "ready";
        } catch (error) {
            item.status = "failed";
            item.error = error.message;
        }

        if (state.attachments.includes(item)) renderTray();
        else discard(item); // removed while it was uploading
    }

    function removeAttachment(item) {
        state.attachments = state.attachments.filter(i => i !== item);
        if (item.status !== "uploading") discard(item);
        renderTray();
        els.prompt.focus();
    }

    /** Deletes the server's copy of a file that won't be sent. If this fails, the server deletes it after a day anyway. */
    function discard(item) {
        if (item.attachment) api.delete(`/api/attachments/${item.attachment.id}`).catch(() => { });
    }

    function renderTray() {
        els.tray.replaceChildren(...state.attachments.map(item => attachmentChip(item, () => removeAttachment(item))));
        els.tray.classList.toggle("d-none", state.attachments.length === 0);

        const uploading = state.attachments.some(item => item.status === "uploading");
        els.send.disabled = uploading;
        els.send.title = uploading ? "Waiting for the attached files to be read" : "";
    }

    /** A file's name and details. Clicking it shows the text read from it; onRemove adds a remove button. */
    function attachmentChip(item, onRemove) {
        const chip = document.createElement("div");
        chip.className = "attachment-chip";
        chip.classList.toggle("is-failed", item.status === "failed");
        chip.classList.toggle("is-truncated", Boolean(item.attachment?.isTruncated));

        const open = document.createElement("button");
        open.type = "button";
        open.className = "attachment-open";
        open.disabled = item.status !== "ready";
        open.title = item.status === "ready" ? `Show the text read from ${item.name}` : item.error ?? item.name;
        open.addEventListener("click", () => showAttachment(item.attachment));

        const name = document.createElement("span");
        name.className = "attachment-name";
        name.textContent = item.name;

        const detail = document.createElement("span");
        detail.className = "attachment-detail";
        if (item.status === "uploading") {
            const spinner = document.createElement("span");
            spinner.className = "spinner-border spinner-border-sm me-1";
            spinner.setAttribute("aria-hidden", "true");
            detail.append(spinner, "Reading…");
        } else {
            detail.textContent = item.status === "failed" ? item.error : describeAttachment(item.attachment);
        }

        open.append(name, detail);
        chip.append(open);
        if (onRemove) {
            const remove = iconButton(`Remove ${item.name}`, "✕", onRemove);
            remove.title = "Remove";
            chip.append(remove);
        }
        return chip;
    }

    function describeAttachment(attachment) {
        const parts = [];
        if (attachment.pageCount) parts.push(`${attachment.pageCount.toLocaleString()} ${attachment.pageCount === 1 ? "page" : "pages"}`);
        const characters = `${compactNumber.format(attachment.characterCount)} characters`;
        parts.push(attachment.isTruncated ? `only the first ${characters}` : characters);
        return parts.join(" · ");
    }

    /** Shows exactly what the model is sent for a file. */
    function showAttachment(summary) {
        showPreview(summary.fileName, async () => {
            const attachment = await api.get(`/api/attachments/${summary.id}`);
            const info = attachment.isTruncated
                ? `This is the text FolderChat read from the file. The file holds more than FolderChat sends, so the model gets only these first ${attachment.characterCount.toLocaleString()} characters.`
                : `This is the text FolderChat read from the file (${describeAttachment(attachment)}). The model gets exactly this with the message.`;
            return { info, text: attachment.text };
        });
    }

    /** Opens the preview dialog and fills it from load(). reveal, when given, backs a "Show in folder" button. */
    async function showPreview(title, load, reveal) {
        const request = ++previewRequest;
        els.previewTitle.textContent = title;
        els.previewInfo.textContent = "Loading…";
        els.previewText.textContent = "";
        els.previewFooter.classList.toggle("d-none", !reveal);
        previewReveal = reveal ?? null;
        previewModal.show();

        try {
            const { info, text } = await load();
            if (request !== previewRequest) return; // something else was opened meanwhile
            els.previewInfo.textContent = info;
            els.previewText.textContent = text;
        } catch (error) {
            if (request === previewRequest) els.previewInfo.textContent = error.message;
        }
    }

    els.previewReveal.addEventListener("click", async () => {
        try {
            await previewReveal?.();
        } catch (error) {
            toast(error.message);
        }
    });

    // ---------- Folder sources ----------

    /** Lines under the model's name saying what it did on the way, such as searching the folder. */
    function addActivity(wrapper, text) {
        let list = wrapper.querySelector(".message-activity");
        if (!list) {
            list = document.createElement("div");
            list.className = "message-activity";
            wrapper.querySelector(".message-meta").after(list);
        }
        const line = document.createElement("div");
        line.textContent = text;
        list.append(line);
    }

    /**
     * Lists the passages a reply cited under it, and turns its [n] markers into links to them. When a reply cites
     * nothing, it lists everything it was shown instead, so the user can still see where it looked.
     */
    function renderSources(wrapper, sources, replyText) {
        wrapper.querySelector(".message-sources")?.remove();
        if (!sources?.length) return;

        const content = wrapper.querySelector(".content");
        const cited = new Set([...(replyText ?? "").matchAll(/\[(\d{1,3})\]/g)].map(match => Number(match[1])));
        const citedSources = sources.filter(source => cited.has(source.number));
        const others = citedSources.length > 0 ? sources.filter(source => !cited.has(source.number)) : [];

        const box = document.createElement("div");
        box.className = "message-sources";
        const label = document.createElement("span");
        label.className = "message-sources-label";
        label.textContent = citedSources.length > 0 ? "Sources" : "Looked at";
        box.append(label, ...(citedSources.length > 0 ? citedSources : sources).map(source => sourceChip(source)));
        if (others.length > 0) {
            const more = document.createElement("button");
            more.type = "button";
            more.className = "btn btn-link btn-sm p-0 source-more";
            more.textContent = `+${others.length} more read`;
            more.addEventListener("click", () => more.replaceWith(...others.map(source => sourceChip(source, true))));
            box.append(more);
        }
        content.after(box);
        linkCitations(content, sources);
    }

    function sourceChip(source, uncited = false) {
        const chip = document.createElement("button");
        chip.type = "button";
        chip.className = `source-chip${uncited ? " is-uncited" : ""}`;
        chip.title = describeSource(source);
        const number = document.createElement("span");
        number.className = "source-number";
        number.textContent = source.number;
        chip.append(number, `${source.path.split("/").pop()}${source.page ? ` · p. ${source.page}` : ""}`);
        chip.addEventListener("click", () => showSource(source));
        return chip;
    }

    /** Turns [n] markers in a rendered reply into buttons that open passage n. Code and links are left alone. */
    function linkCitations(content, sources) {
        const byNumber = new Map(sources.map(source => [source.number, source]));
        const walker = document.createTreeWalker(content, NodeFilter.SHOW_TEXT, {
            acceptNode: node => node.parentElement.closest("pre, code, a, button") ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT,
        });
        const nodes = [];
        while (walker.nextNode()) {
            if (/\[\d{1,3}\]/.test(walker.currentNode.nodeValue)) nodes.push(walker.currentNode);
        }

        for (const node of nodes) {
            const fragment = document.createDocumentFragment();
            for (const part of node.nodeValue.split(/(\[\d{1,3}\])/)) {
                const source = byNumber.get(Number(/^\[(\d{1,3})\]$/.exec(part)?.[1]));
                if (!source) {
                    if (part) fragment.append(part);
                    continue;
                }
                const link = document.createElement("button");
                link.type = "button";
                link.className = "citation";
                link.textContent = source.number;
                link.title = describeSource(source);
                link.setAttribute("aria-label", `Source ${source.number}: ${describeSource(source)}`);
                link.addEventListener("click", () => showSource(source));
                fragment.append(link);
            }
            node.replaceWith(fragment);
        }
    }

    function describeSource(source) {
        const folder = state.folders.find(f => f.id === source.folderId);
        const path = folder ? `${folder.name} › ${source.path}` : source.path;
        return source.page ? `${path}, page ${source.page}` : path;
    }

    function showSource(source) {
        const folder = state.folders.find(f => f.id === source.folderId);
        showPreview(
            describeSource(source),
            async () => {
                const passage = await api.get(`/api/folders/passages/${source.passageId}`);
                const where = folder ? ` from ${folder.name}` : "";
                return { info: `Passage [${source.number}]${where}, as the model was given it.`, text: passage.text };
            },
            () => api.post(`/api/folders/files/${source.fileId}/reveal`));
    }

    // ---------- Sending and streaming ----------

    function setStreaming(on) {
        els.send.classList.toggle("d-none", on);
        els.stop.classList.toggle("d-none", !on);
        els.picker.disabled = on || state.providers.length === 0;
        els.foldersButton.disabled = on || state.choosingFolder;
        els.effortPicker.disabled = on;
        renderFolderControls(); // the chips' remove buttons too
        els.newChat.disabled = on;
    }

    async function send() {
        if (state.streaming) return;
        if (state.attachments.some(item => item.status === "uploading")) {
            toast("Wait a moment: the attached files are still being read.", "warning");
            return;
        }

        const text = els.prompt.value.trim();
        const ready = state.attachments.filter(item => item.status === "ready");
        if (!text && ready.length === 0) return;

        const selection = currentSelection();
        if (!selection) {
            toast("Pick a model first. If the list is empty, add a provider in Settings.", "warning");
            return;
        }

        // Files that failed to read are dropped. Their error was shown on the chip.
        const files = ready.map(item => item.attachment);
        const provider = state.providers.find(p => p.id === selection.providerId);
        els.prompt.value = "";
        state.attachments = [];
        renderTray();
        autosize();

        const userBubble = appendMessage({ role: "User", content: text, attachments: files });
        const reply = appendMessage({ role: "Assistant", content: "", providerName: provider?.name, modelId: selection.modelId });
        const content = reply.querySelector(".content");
        reply.classList.add("streaming");
        updateEmptyState();
        scrollToBottom(true);

        const controller = new AbortController();
        state.streaming = controller;
        setStreaming(true);

        let replyText = "";
        let started = false;
        let finished = false;
        let renderQueued = false;
        const renderSoon = () => {
            if (renderQueued) return;
            renderQueued = true;
            requestAnimationFrame(() => {
                renderQueued = false;

                // A frame left over from streaming would undo the final render's highlighting and links.
                if (finished || controller.signal.aborted) return;
                markdown.render(content, replyText);
                scrollToBottom(false);
            });
        };

        // Nothing was saved, so put the message and its files back to try again.
        const restoreComposer = () => {
            userBubble.remove();
            reply.remove();
            els.prompt.value = text;
            state.attachments = [...ready, ...state.attachments];
            renderTray();
            autosize();
        };

        const body = {
            conversationId: state.currentId,
            providerId: selection.providerId,
            modelId: selection.modelId,
            message: text,
            attachmentIds: files.map(file => file.id),
            folderIds: state.chatFolders,
            effort: state.chatEffort,
        };
        let replySources = [];
        try {
            await streamChat(body, controller.signal, {
                started: event => {
                    started = true;
                    if (event.isNewConversation) {
                        state.currentId = event.conversationId;
                        state.conversations.unshift({ id: event.conversationId, title: event.title, modelId: selection.modelId });
                        els.title.textContent = event.title;
                        history.replaceState(null, "", `#${event.conversationId}`);
                    } else {
                        moveToTop(event.conversationId);
                    }
                    renderConversationList();
                },
                delta: event => {
                    replyText += event.text;
                    renderSoon();
                },
                activity: event => {
                    addActivity(reply, event.text);
                    scrollToBottom(false);
                },
                sources: event => {
                    replySources = event.sources;
                },
                completed: event => {
                    finished = true;
                    markdown.render(content, replyText);
                    markdown.enhance(content);
                    renderSources(reply, replySources, replyText);
                    if (event.outputTokens) {
                        reply.querySelector(".message-meta").textContent += ` · ${event.outputTokens.toLocaleString()} tokens`;
                    }
                    if (event.finishReason === "length") addNote(reply, "The reply hit the model's length limit.");
                },
                error: event => {
                    finished = true;
                    if (!started) {
                        restoreComposer();
                        toast(event.error);
                        return;
                    }
                    markdown.render(content, replyText);
                    renderSources(reply, replySources, replyText);
                    addError(reply, event.error);
                },
            });

            if (!finished) addNote(reply, "The connection closed before the reply finished.");
        } catch (error) {
            if (error.name === "AbortError") {
                markdown.render(content, replyText);
                markdown.enhance(content);
                addNote(reply, "Stopped.");
            } else if (!started) {
                restoreComposer();
                toast(error.message || "The request failed.");
            } else {
                addError(reply, error.message || "The request failed.");
            }
        } finally {
            reply.classList.remove("streaming");
            state.streaming = null;
            setStreaming(false);
            updateEmptyState();
            scrollToBottom(false);
            els.prompt.focus();
        }
    }

    /** Posts the message and reads the Server-Sent Events stream, calling handlers[eventName](data). */
    async function streamChat(body, signal, handlers) {
        const response = await fetch("/api/chat/stream", {
            method: "POST",
            headers: { "Content-Type": "application/json", Accept: "text/event-stream", "X-CSRF-TOKEN": csrfToken },
            body: JSON.stringify(body),
            signal,
        });
        if (!response.ok || !response.body) throw new Error(await readError(response));

        const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
        let buffer = "";
        while (true) {
            const { value, done } = await reader.read();
            if (done) break;

            buffer += value.replaceAll("\r\n", "\n");
            let boundary;
            while ((boundary = buffer.indexOf("\n\n")) !== -1) {
                const raw = buffer.slice(0, boundary);
                buffer = buffer.slice(boundary + 2);

                let eventName = "message";
                const data = [];
                for (const line of raw.split("\n")) {
                    if (line.startsWith("event:")) eventName = line.slice(6).trim();
                    else if (line.startsWith("data:")) data.push(line.slice(5).trimStart());
                }
                if (data.length > 0) handlers[eventName]?.(JSON.parse(data.join("\n")));
            }
        }
    }

    function autosize() {
        els.prompt.style.height = "auto";
        els.prompt.style.height = `${els.prompt.scrollHeight + 2}px`;
    }

    // ---------- Wiring ----------

    els.composer.addEventListener("submit", event => {
        event.preventDefault();
        send();
    });
    els.prompt.addEventListener("keydown", event => {
        if (event.key === "Enter" && !event.shiftKey && !event.isComposing) {
            event.preventDefault();
            send();
        }
    });
    els.prompt.addEventListener("input", autosize);
    els.stop.addEventListener("click", () => state.streaming?.abort());
    els.newChat.addEventListener("click", newChat);
    els.search.addEventListener("input", renderConversationList);

    els.attach.addEventListener("click", () => els.fileInput.click());
    els.fileInput.addEventListener("change", () => {
        addFiles(els.fileInput.files);
        els.fileInput.value = ""; // so picking the same file again still fires "change"
    });

    // Pasting a copied file attaches it. Rich text from Word also carries a picture of itself, so only
    // clipboards without plain text count.
    els.prompt.addEventListener("paste", event => {
        const clipboard = event.clipboardData;
        if (!clipboard || clipboard.files.length === 0 || clipboard.types.includes("text/plain")) return;
        event.preventDefault();
        addFiles(clipboard.files);
    });

    // Dropping files anywhere on the chat attaches them.
    const hasFiles = event => event.dataTransfer?.types.includes("Files") ?? false;
    let dragDepth = 0; // dragenter and dragleave fire for every child element crossed
    els.main.addEventListener("dragenter", event => {
        if (!hasFiles(event)) return;
        dragDepth++;
        els.dropZone.classList.remove("d-none");
    });
    els.main.addEventListener("dragleave", event => {
        if (!hasFiles(event)) return;
        dragDepth = Math.max(0, dragDepth - 1);
        if (dragDepth === 0) els.dropZone.classList.add("d-none");
    });
    els.main.addEventListener("drop", event => {
        if (!hasFiles(event)) return;
        dragDepth = 0;
        els.dropZone.classList.add("d-none");
        addFiles(event.dataTransfer.files);
    });

    // Without this, a file dropped anywhere on the page opens in the tab and the chat is lost.
    for (const type of ["dragover", "drop"]) {
        window.addEventListener(type, event => {
            if (!hasFiles(event)) return;
            event.preventDefault();
            if (type === "dragover") event.dataTransfer.dropEffect = els.main.contains(event.target) ? "copy" : "none";
        });
    }

    (async () => {
        try {
            await Promise.all([loadPicker(), loadConversations(), loadFolders()]);
        } catch (error) {
            toast(error.message);
        }

        const id = location.hash.slice(1);
        if (id && state.conversations.some(c => c.id === id)) await openConversation(id);
        else updateEmptyState();
        els.prompt.focus();
    })();
})();
