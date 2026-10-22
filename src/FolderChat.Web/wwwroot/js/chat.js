// The chat page: model picker, conversation list and streamed replies.
(() => {
    const { api, toast, csrfToken, readError } = window.folderChat;
    const markdown = window.folderChatMarkdown;
    const MODEL_KEY = "folderchat.model";

    const els = {
        list: document.getElementById("conversation-list"),
        search: document.getElementById("conversation-search"),
        newChat: document.getElementById("new-chat"),
        picker: document.getElementById("model-picker"),
        privacy: document.getElementById("privacy-badge"),
        title: document.getElementById("chat-title"),
        messages: document.getElementById("messages"),
        empty: document.getElementById("empty-state"),
        emptyText: document.getElementById("empty-state-text"),
        composer: document.getElementById("composer"),
        prompt: document.getElementById("prompt"),
        send: document.getElementById("send"),
        stop: document.getElementById("stop"),
    };

    const state = {
        conversations: [],
        currentId: null, // null means a new, unsaved chat
        providers: [], // providers with enabled models, from /api/providers/picker
        streaming: null, // the AbortController of the reply being streamed
    };

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
            selectModel(readStoredModel());
        }

        updatePrivacyBadge();
        updateEmptyState();
    }

    function readStoredModel() {
        try {
            return localStorage.getItem(MODEL_KEY);
        } catch {
            return null;
        }
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
    }

    els.picker.addEventListener("change", () => {
        try {
            localStorage.setItem(MODEL_KEY, els.picker.value);
        } catch {
            // Not remembered; harmless.
        }
        updatePrivacyBadge();
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
            const bubble = document.createElement("div");
            bubble.className = "bubble";
            bubble.textContent = message.content;
            wrapper.append(bubble);
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
            els.emptyText.textContent = "Pick a model above and send a message.";
        }
    }

    function isNearBottom() {
        const list = els.messages;
        return list.scrollHeight - list.scrollTop - list.clientHeight < 80;
    }

    function scrollToBottom(force) {
        if (force || isNearBottom()) els.messages.scrollTop = els.messages.scrollHeight;
    }

    // ---------- Sending and streaming ----------

    function setStreaming(on) {
        els.send.classList.toggle("d-none", on);
        els.stop.classList.toggle("d-none", !on);
        els.picker.disabled = on || state.providers.length === 0;
        els.newChat.disabled = on;
    }

    async function send() {
        const text = els.prompt.value.trim();
        if (!text || state.streaming) return;

        const selection = currentSelection();
        if (!selection) {
            toast("Pick a model first. If the list is empty, add a provider in Settings.", "warning");
            return;
        }

        const provider = state.providers.find(p => p.id === selection.providerId);
        els.prompt.value = "";
        autosize();

        const userBubble = appendMessage({ role: "User", content: text });
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
                markdown.render(content, replyText);
                scrollToBottom(false);
            });
        };

        const body = { conversationId: state.currentId, providerId: selection.providerId, modelId: selection.modelId, message: text };
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
                completed: event => {
                    finished = true;
                    markdown.render(content, replyText);
                    markdown.enhance(content);
                    if (event.outputTokens) {
                        reply.querySelector(".message-meta").textContent += ` · ${event.outputTokens.toLocaleString()} tokens`;
                    }
                    if (event.finishReason === "length") addNote(reply, "The reply hit the model's length limit.");
                },
                error: event => {
                    finished = true;
                    if (!started) {
                        // Nothing was saved. Put the message back so it isn't lost.
                        userBubble.remove();
                        reply.remove();
                        els.prompt.value = text;
                        autosize();
                        toast(event.error);
                        return;
                    }
                    markdown.render(content, replyText);
                    addError(reply, event.error);
                },
            });

            if (!finished) addNote(reply, "The connection closed before the reply finished.");
        } catch (error) {
            if (error.name === "AbortError") {
                markdown.render(content, replyText);
                markdown.enhance(content);
                addNote(reply, "Stopped.");
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

    (async () => {
        try {
            await Promise.all([loadPicker(), loadConversations()]);
        } catch (error) {
            toast(error.message);
        }

        const id = location.hash.slice(1);
        if (id && state.conversations.some(c => c.id === id)) await openConversation(id);
        else updateEmptyState();
        els.prompt.focus();
    })();
})();
