# FolderChat

Chat with Claude, ChatGPT, DeepSeek, Kimi, GLM, Gemini and local models from one app that runs on your own computer.

> **Status: v0.1 (early preview).** Chat, the model picker and conversation history work.
> Answering from your own files and folders is next on the [roadmap](#roadmap).

## Why FolderChat

- **One app, many models.** Pick a model per message. Switch from Claude to DeepSeek to a local Llama in the same conversation.
- **Runs on your machine.** A small ASP.NET Core server listens on `127.0.0.1` and your browser is the window. Conversations and API keys stay in a local SQLite file.
- **Local models too.** Point it at [Ollama](https://ollama.com) or LM Studio and nothing leaves your computer. The chat shows a badge saying where each message goes.
- **Open source.** MIT licensed. Contributions welcome.

## Features in v0.1

- Model picker grouped by provider, loaded from each provider's own model list.
- Streaming replies with Markdown, syntax-highlighted code and copy buttons. A Stop button keeps whatever arrived so far.
- Conversation history with search, rename and delete.
- Providers:
  - Anthropic (Claude) through the official Anthropic SDK
  - OpenAI through the official OpenAI SDK
  - Any OpenAI-compatible API, with presets for DeepSeek, Kimi (Moonshot AI), GLM (Z.ai), Google Gemini, OpenRouter, Groq, xAI, Mistral and LM Studio
  - Ollama for local models
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

More detail: [Getting started](docs/getting-started.md) · [Providers](docs/providers.md) · [Configuration](docs/configuration.md)

## Where your data lives

| OS | Folder |
|---|---|
| Windows | `%LOCALAPPDATA%\FolderChat` |
| macOS | `~/Library/Application Support/FolderChat` |
| Linux | `~/.local/share/FolderChat` |

The folder holds:

- `folderchat.db`: conversations, providers and encrypted API keys
- `keys/`: the encryption key ring (DPAPI-protected on Windows)
- `logs/`: daily log files

To move it, set `FolderChat__DataDirectory` or pass `--FolderChat:DataDirectory=<path>`.

## Security

FolderChat is a local app that holds your API keys, so it locks itself down by default:

- It listens on `127.0.0.1` only. Other devices on your network can't reach it.
- It only answers requests addressed to `localhost`. This blocks DNS-rebinding attacks from websites you visit.
- Every change request needs an anti-forgery token, so other sites can't send requests on your behalf.
- API keys are encrypted with ASP.NET Core Data Protection and never returned by the API.
- Model replies are rendered as sanitized Markdown (DOMPurify), so a reply can't run script in the page.

When you chat with a cloud provider, your messages go to that provider under its terms. See [SECURITY.md](SECURITY.md) to report a vulnerability.

## How it works

```
Browser (Razor + vanilla JS)  ──SSE──  ASP.NET Core 10 (controllers)
                                            │
                                   Application layer (use cases)
                                            │  IChatClient (Microsoft.Extensions.AI)
               ┌────────────────┬───────────┴────────┬──────────────────┐
           Anthropic SDK    OpenAI SDK      OpenAI SDK + base URL    OllamaSharp
              Claude          GPT          DeepSeek, Kimi, GLM, …    local models

                         EF Core + SQLite (conversations, providers)
```

Every provider sits behind the same `IChatClient` interface, so adding one is a small, self-contained change. See [Architecture](docs/architecture.md).

## Roadmap

- **v0.2 Attachments:** attach PDF, Word and text files to a message.
- **v0.3 Knowledge folders:** index a folder in the background and get answers with citations.
- **v0.4 Polish:** token and cost display, a local-only mode, PWA install.
- **v1.0 Releases:** single-file builds for Windows, macOS and Linux, plus a Docker image.

## Contributing

Bug reports, ideas and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for the dev setup, including how to add a provider.

## License

[MIT](LICENSE) © 2026 Ravni Arador. Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
