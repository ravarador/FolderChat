# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [0.2.0] - 2026-10-23

### Added

- Attach PDF, Word (.docx) and text files to a message: click **Attach**, drop files on the chat, or paste a copied file. FolderChat reads the text on your computer and sends it with your message, so attachments work with every provider, local models included.
- Text files cover anything readable as text, such as Markdown, CSV, JSON, logs and source code, in UTF-8, UTF-16 or Windows-1252.
- Files are read as soon as they're attached. A file that can't be read (a scan, an image, a password-protected file, an old .doc) is flagged before sending, with what to do about it.
- Click an attached file to see exactly the text the model receives.
- Up to 10 files per message and 20 MB per file. A file with more than 500,000 characters of text is cut off, and both you and the model are told.
- Attached files stay part of the conversation: later messages send them to the model again. Deleting the conversation deletes them, and files attached but never sent are deleted after a day.
- A message can be just files, with no text. A new chat is then named after the first file.

### Fixed

- A rejected API key now shows the provider's own reason, which often includes the last characters of the key it received.
- A cloud API saved with the Ollama type now gets a clear hint to switch to OpenAI-compatible. Ollama providers never send an API key. The Settings dialog warns about this before saving, too.
- Expected API errors (invalid input, missing items, provider failures) no longer stop the Visual Studio debugger as "user-unhandled" exceptions.
- Request logs record the status code the browser actually received. Provider failures are logged as warnings instead of errors with stack traces.

## [0.1.0] - 2026-10-21

### Added

- Local web app on ASP.NET Core 10 that listens on `127.0.0.1:5080`, with data stored in SQLite.
- Four provider adapters behind Microsoft.Extensions.AI's `IChatClient`:
  - Anthropic (official SDK)
  - OpenAI (official SDK)
  - OpenAI-compatible APIs
  - Ollama
- Presets for DeepSeek, Kimi, GLM, Gemini, OpenRouter, Groq, xAI, Mistral, Ollama and LM Studio.
- Model lists loaded from each provider, with per-model show and hide, filtering, and manually added model IDs.
- Streaming chat over Server-Sent Events, with Markdown rendering, code highlighting, copy buttons and a Stop button that keeps partial replies.
- Conversation history with search, rename and delete. A conversation can switch models between messages.
- API keys encrypted at rest with ASP.NET Core Data Protection (DPAPI on Windows).
- Host-header allowlist and anti-forgery tokens on all change requests.
- Readable provider errors, including the explanation in Ollama's error responses.
- Light and dark themes.
