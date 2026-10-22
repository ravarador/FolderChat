// Renders model replies as Markdown. Everything passes through DOMPurify, because a
// reply can contain any text, including HTML the model was tricked into writing.
(() => {
    marked.setOptions({ gfm: true, breaks: false });
    hljs.configure({ ignoreUnescapedHTML: true });

    DOMPurify.addHook("afterSanitizeAttributes", node => {
        if (node.tagName === "A") {
            node.setAttribute("target", "_blank");
            node.setAttribute("rel", "noopener noreferrer");
        }
    });

    function render(element, markdownText) {
        element.innerHTML = DOMPurify.sanitize(marked.parse(markdownText ?? ""));
    }

    /** Highlights code blocks and adds copy buttons. Call once a reply is complete. */
    function enhance(element) {
        for (const block of element.querySelectorAll("pre code")) {
            hljs.highlightElement(block);

            const pre = block.parentElement;
            if (pre.querySelector(".copy-code")) continue;

            const button = document.createElement("button");
            button.type = "button";
            button.className = "btn btn-sm btn-secondary copy-code";
            button.textContent = "Copy";
            button.addEventListener("click", async () => {
                try {
                    await navigator.clipboard.writeText(block.innerText);
                    button.textContent = "Copied";
                } catch {
                    button.textContent = "Copy failed";
                }
                setTimeout(() => (button.textContent = "Copy"), 1500);
            });
            pre.append(button);
        }
    }

    window.folderChatMarkdown = { render, enhance };
})();
