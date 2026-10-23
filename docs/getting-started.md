# Getting started

## 1. Install .NET 10

Download the **.NET 10 SDK** from <https://dotnet.microsoft.com/download>, or use a package manager:

```bash
winget install Microsoft.DotNet.SDK.10
```

```bash
brew install --cask dotnet-sdk
```

Check it worked:

```bash
dotnet --version
```

## 2. Run FolderChat

```bash
git clone https://github.com/ravarador/FolderChat.git
cd FolderChat
dotnet run --project src/FolderChat.Web
```

Open <http://127.0.0.1:5080>. The first run creates the database in your data folder (see the README) and applies any updates automatically.

## 3. Add a provider

1. Click **Settings**, then **Add provider**.
2. Choose a preset under **Start from**. It fills in the type and base URL.
3. Paste your API key. Each preset links to the page where you create one.
4. Click **Save**. FolderChat loads the provider's models straight away.

Then go back to **Chat**, pick a model, and send a message.

### Use a local model instead (no API key)

1. Install [Ollama](https://ollama.com).
2. Download a model that fits your computer's memory, for example:

   ```bash
   ollama pull qwen3:4b
   ```

3. In FolderChat, add the **Ollama (local)** preset.

The chat shows a green **Stays on this computer** badge when a local model is selected.

## Attaching files

You can send PDF, Word (.docx) and text files with a message. Text files include Markdown, CSV, JSON, logs and source code.

1. Click **Attach** next to the message box, drag files onto the chat, or paste a file you copied.
2. FolderChat reads each file straight away. If it can't read one, the file turns red and says why.
3. Type your question, or nothing at all, and send.

Click a file to see the text FolderChat read from it. That text is exactly what the model receives: FolderChat sends the text, not the file, so attachments work with every provider, local models included.

Limits:

- Up to 10 files per message, and 20 MB per file.
- About 500,000 characters of text per file, roughly 125,000 tokens of English. Longer files are cut off, and both you and the model are told.
- Scanned PDFs hold pictures of text, which FolderChat can't read yet. The same goes for images.

Attached files stay part of the conversation. Every later message sends them to the model again, so it can answer follow-up questions, but they also count toward each message's input tokens. Deleting the conversation deletes its files.

**Local models** often have a small context window. A long document may not fit, and the model then sees only part of it. With Ollama, raise the context length in its settings, or with the `OLLAMA_CONTEXT_LENGTH` environment variable, if the model seems to ignore your file.

## Choosing which models appear

Each provider card in Settings lists its models:

- Tick the models you want in the chat's model picker.
- Providers with more than 30 models, such as OpenRouter, start with every model hidden, so the picker stays short. Use the filter box and tick the ones you want.
- If a provider doesn't list its models, type a model ID in **Add a model ID by hand**.

## Updating

```bash
git pull
dotnet run --project src/FolderChat.Web
```

Database changes are applied automatically when the app starts.

## Troubleshooting

| Problem | What to try |
|---|---|
| "Couldn't connect" when loading Ollama models | Make sure Ollama is running (`ollama list` should work), and check the base URL. |
| "The API key was rejected (401)" | Edit the provider and paste the key again. Check it hasn't been revoked. |
| "Model requires more system memory…" | The local model is too big for your free RAM or VRAM. Pull a smaller one. |
| "Not found (404)" | Check the base URL and model ID. OpenAI-compatible base URLs usually end in `/v1`, or `/v4` for GLM. |
| Port 5080 is already in use | Run with `--urls http://127.0.0.1:5090`, or see [Configuration](configuration.md). |
| "…has no text FolderChat can read" for a PDF | It's a scan: its pages are pictures of text. Run it through OCR (many scanner apps and PDF editors can), then attach the result. |
| The model ignores an attached file | The file may not fit in the model's context window. Ask about a shorter file, pick a model with a larger window, or raise Ollama's context length (see [Attaching files](#attaching-files)). |
| Visual Studio says "One or more projects in the solution were not loaded correctly" | You're on Visual Studio 2022. .NET 10 needs Visual Studio 2026 (version 18) or later, which installs alongside 2022. Rider 2025.3+, VS Code and the `dotnet` command line also work. |

Logs are in the `logs` folder inside your data folder.
