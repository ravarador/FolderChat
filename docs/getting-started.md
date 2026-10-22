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
| Visual Studio says "One or more projects in the solution were not loaded correctly" | You're on Visual Studio 2022. .NET 10 needs Visual Studio 2026 (version 18) or later, which installs alongside 2022. Rider 2025.3+, VS Code and the `dotnet` command line also work. |

Logs are in the `logs` folder inside your data folder.
