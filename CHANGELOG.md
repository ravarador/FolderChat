# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Changed

- A redesigned look. The top bar is gone: the sidebar holds your chats, then Settings and the theme switch at the bottom. Chats are grouped by day (Today, Yesterday, Previous 7 days, and so on).
- One message box holds the folders, attached files, the model picker and a round Send button.
- Settings lists its sections in the sidebar: Providers, Knowledge folders, API keys and Usage.
- Teal colours in place of Bootstrap blue, in both themes and on the app icon, and icons on the buttons.

### Fixed

- On phones, the chat list was hidden with no way to open it. A menu button now opens it as a drawer.

## [0.5.0] - 2026-10-28

### Added

- A REST API at `/api/v1`, so your own apps can ask FolderChat questions from their server. For example, an admin app can answer from a folder of manuals, with citations. See [the API guide](docs/api.md).
- API keys in **Settings > API keys**. A key is shown once, only its fingerprint is stored, and revoking it stops it straight away. With no keys, the API is closed.
- `POST /api/v1/chat` returns the whole reply as JSON, or streams it as Server-Sent Events with `"stream": true`. Models and folders can be named, not only given by ID, and replies include each cited passage's folder, file, page and text.
- Optional defaults for API requests in `appsettings.json` (`FolderChat:Api:Defaults`): model, folders, effort and instructions, so a request can be just a message.
- `GET /api/v1/models`, `/folders` and `/conversations`, and reading or deleting a conversation. An app only sees the chats it started through the API.
- Chats started through the API show in the chat list with an **API** badge.
- An OpenAPI description at `/api/v1/openapi.json`.
- `FolderChat:Api:Urls` adds an address that serves only the API, for apps on other computers. Settings and your own chats aren't reachable there.

## [0.4.0] - 2026-10-28

### Added

- Token counts on every reply: "1,240 in · 380 out" under the model's name, as the provider reported them. Hover for tokens read from the provider's cache, tokens spent thinking, and how full the model's context was.
- When a reply used 80% or more of the model's context, it says so, since attachments and folder passages count toward it. This needs a provider that reports the model's context size, such as Anthropic.
- A running total for the open chat in the chat header.
- **Usage this month** in Settings: tokens read and written by each model this calendar month, in this computer's time zone.
- Folders with summaries on show how many tokens the summaries used.
- Install FolderChat as an app: **Install app** in Chrome and Edge, or **Add to Dock** in Safari, puts it in its own window with its own icon. When the app is opened while FolderChat isn't running, it says how to start it.

### Changed

- Replies count tokens read and written instead of only the tokens written.

## [0.3.0] - 2026-10-23

### Added

- Knowledge folders: add a folder on your computer, and FolderChat reads its PDF, Word (.docx) and text files in the background.
- Each chat answers from its own folders: tick one or several (up to 10) under **Folders**, next to the message box, or add a new folder from there. A new chat starts with none, and reopening a chat brings back its folders.
- Add a folder by choosing it in your computer's own folder window: File Explorer's on Windows, Finder's on a Mac, and zenity's or kdialog's on Linux. Where the window can't open, such as for a browser on another device, you paste the folder's path instead.
- Three effort levels for folder answers, chosen per chat:
  - **Quick:** FolderChat searches once with your question and sends the best passages. Works with every model.
  - **Standard:** the model searches the folders itself, trying other words if needed.
  - **Thorough:** more searching, and the model can also read whole pages and look through the list of files.
- Replies cite passages as [1], [2] and so on, and list their sources. Click a citation to see the exact passage the model was given, and to show the file in File Explorer or Finder.
- Search uses SQLite's full-text index, with nothing extra to install. It matches words (with "costs" also finding "cost"), not meaning, which is why Standard and Thorough let the model try other words.
- Folders are checked for new, changed and deleted files every five minutes, or when you click **Check for changes**. Only changed files are read again.
- Settings shows each folder's progress and every file found, with the reason any file was skipped: an image, a scan, too large, stored online only, and so on.
- Optional summaries: a model of your choice writes a short summary of each file, and Thorough answers use them to pick files. Off by default, and a local model is suggested so the files stay on your computer.
- Models that can't use tools get a Quick search instead, automatically.

### Changed

- The database now uses write-ahead logging, so chats can search a folder while it's being read. You'll see `folderchat.db-wal` and `folderchat.db-shm` next to `folderchat.db`.

### Fixed

- Code highlighting and copy buttons could disappear from a reply when its last words arrived just before it finished.

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
