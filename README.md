# FolderChat

Chat with Claude, ChatGPT, DeepSeek, Kimi, GLM, Gemini and local models from one app that runs on your own computer.

> **Status: v0.5 (early preview).** Chat, file attachments, answers from whole folders of your files with citations, token counts, installing as an app, and a REST API for your own apps work.
> Single-file builds are next on the [roadmap](#roadmap).

## Why FolderChat

- **One app, many models.** Pick a model per message. Switch from Claude to DeepSeek to a local Llama in the same conversation.
- **Ask your own files.** Point FolderChat at a folder of PDFs, Word documents and notes, and ask questions. Answers cite the passages they come from.
- **Runs on your machine.** A small ASP.NET Core server listens on `127.0.0.1` and your browser is the window. Conversations, the folder index and API keys stay in a local SQLite file.
- **Local models too.** Point it at [Ollama](https://ollama.com) or LM Studio and nothing leaves your computer. The chat shows a badge saying where each message goes.
- **Use it from your own apps.** A REST API with API keys lets another app, such as an admin panel, answer its users' questions from your folders.
- **Open source.** MIT licensed. Contributions welcome.

## Features in v0.5

- Model picker grouped by provider, loaded from each provider's own model list.
- Streaming replies with Markdown, syntax-highlighted code and copy buttons. A Stop button keeps whatever arrived so far.
- Conversation history with search, rename and delete.
- Attach PDF, Word (.docx) and text files to a message: click **Attach**, drop them on the chat, or paste them. FolderChat reads the text on your computer and sends it with your message, so attachments work with every model, local ones included. Click a file to see exactly what the model receives.
- Knowledge folders: choose a folder in your computer's own folder window and FolderChat indexes its PDF, Word and text files in the background, keeping up with changes. Each chat answers from the folders you tick under **Folders**, one or several; replies cite passages like [1], and clicking one shows the exact text the model was given.
- Three effort levels for folder answers: **Quick** searches once for the model, **Standard** lets the model search the folders itself, and **Thorough** also lets it read whole pages and look through the files. See [Getting started](docs/getting-started.md#answering-from-a-folder).
- Providers:
  - Anthropic (Claude) through the official Anthropic SDK
  - OpenAI through the official OpenAI SDK
  - Any OpenAI-compatible API, with presets for DeepSeek, Kimi (Moonshot AI), GLM (Z.ai), Google Gemini, OpenRouter, Groq, xAI, Mistral and LM Studio
  - Ollama for local models
- Token counts on every reply, a running total per chat, and this month's usage by model in Settings. FolderChat is free; the tokens are what your providers bill your API keys for.
- Install it as an app, in its own window, from Chrome, Edge or Safari.
- A REST API at `/api/v1` for your own apps, with keys made in Settings: ask a question and get the reply with its sources, as JSON or streamed. Chats it starts show with an **API** badge. See the [API guide](docs/api.md).
- API keys encrypted at rest, and never sent back to the browser.
- Light and dark themes.

## Quick start

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

To open the solution in an IDE, use one of these:

- Visual Studio 2026 (version 18) or later
- JetBrains Rider 2025.3 or later
- VS Code with the C# Dev Kit

Visual Studio 2022 can't load .NET 10 projects.

```bash
git clone https://github.com/ravarador/FolderChat.git
cd FolderChat
dotnet run --project src/FolderChat.Web
```

Then:

1. Open <http://127.0.0.1:5080>.
2. Go to **Settings** and click **Add provider**.
3. Pick a preset, paste your API key, and click **Save**. FolderChat loads that provider's models.
4. Go back to **Chat**, pick a model and send a message.

**No API key?** Install [Ollama](https://ollama.com), run `ollama pull qwen3:4b` (or any model that fits your machine), then add the **Ollama (local)** preset.

More detail: [Getting started](docs/getting-started.md) · [Providers](docs/providers.md) · [REST API](docs/api.md) · [Configuration](docs/configuration.md)

## Where your data lives

| OS | Folder |
|---|---|
| Windows | `%LOCALAPPDATA%\FolderChat` |
| macOS | `~/Library/Application Support/FolderChat` |
| Linux | `~/.local/share/FolderChat` |

The folder holds:

- `folderchat.db`: conversations, the text of attached files, the index of your knowledge folders, providers and encrypted API keys, and hashes of the keys your apps use. The `-wal` and `-shm` files next to it belong to it.
- `keys/`: the encryption key ring (DPAPI-protected on Windows)
- `logs/`: daily log files

To move it, set `FolderChat__DataDirectory` or pass `--FolderChat:DataDirectory=<path>`.

## Security

FolderChat is a local app that holds your API keys, so it locks itself down by default:

- It listens on `127.0.0.1` only. Other devices on your network can't reach it.
- It only answers requests addressed to `localhost`. This blocks DNS-rebinding attacks from websites you visit.
- Every change request needs an anti-forgery token, so other sites can't send requests on your behalf.
- The REST API needs a key made in Settings, and is closed until you make one. A key only opens `/api/v1`, and only the chats started through it.
- API keys are encrypted with ASP.NET Core Data Protection and never returned by the API.
- Model replies are rendered as sanitized Markdown (DOMPurify), so a reply can't run script in the page.

When you chat with a cloud provider, your messages, the text of any files you attach, and the folder passages each question needs go to that provider under its terms. FolderChat reads the files in knowledge folders on your computer, and never changes them. See [SECURITY.md](SECURITY.md) to report a vulnerability.

## How it works

```
Browser (Razor + vanilla JS)  ──SSE──  ASP.NET Core 10 (controllers)
                                            │
                                   Application layer (use cases)
                                            │  IChatClient (Microsoft.Extensions.AI)
               ┌────────────────┬───────────┴────────┬──────────────────┐
           Anthropic SDK    OpenAI SDK      OpenAI SDK + base URL    OllamaSharp
              Claude          GPT          DeepSeek, Kimi, GLM, …    local models

     EF Core + SQLite (conversations, attachment text, providers, folder index with FTS5)
  PdfPig + Open XML SDK (read PDF and Word files locally)   Background service (scans folders)
```

Every provider sits behind the same `IChatClient` interface, so adding one is a small, self-contained change. See [Architecture](docs/architecture.md).

## Roadmap

- **v0.2 Attachments** (done): attach PDF, Word and text files to a message.
- **v0.3 Knowledge folders** (done): index a folder in the background and get answers with citations.
- **v0.4 Polish** (done): token counts per reply, chat and month, and installing as an app.
- **v0.5 REST API** (done): API keys and `/api/v1`, so your own apps can ask questions.
- **Later:** a local-only mode that keeps every message on this computer.
- **v1.0 Releases:** single-file builds for Windows, macOS and Linux, plus a Docker image.

## Contributing

Bug reports, ideas and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the dev setup, including how to add a provider.

## License

[MIT](LICENSE) © 2026 Ravni Arador. Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
