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

## Answering from a folder

For more files than you'd attach to one message, add a knowledge folder. FolderChat reads the PDF, Word (.docx) and text files in it, including subfolders, and keeps an index of their text on your computer.

1. In a chat, click **Folders** next to the message box, then **Add a folder…**. Your computer's own folder window opens (File Explorer's on Windows, Finder's on a Mac). Choose the folder and click **Select Folder**, and the chat starts using it. You can also click **Choose folder…** in **Settings**, under **Knowledge folders**.
2. FolderChat reads the files in the background. In Settings, click **Show files** to see each file, and why any were skipped.
3. Tick the folders this chat should answer from under **Folders**. They show above the message box, where you can remove one or choose an effort level. Then ask.

Each chat keeps its own folders, up to 10, and a new chat starts with none. Any chat can use a folder you've added, but a chat only answers from the folders ticked in it.

The folder window opens on the computer running FolderChat. Where it can't open, as on a computer without a desktop or for a browser on another device, paste the folder's full path instead, in Settings or in the chat's **Add a folder…** box. On Windows, right-click the folder in File Explorer and choose **Copy as path**. On a Mac, select it in Finder and press Option+Command+C. On Linux, the window needs zenity or kdialog, which most desktops include.

Replies cite the passages they use as [1], [2] and so on, and list the sources below. Click a citation to see the exact passage the model was given, and **Show in folder** to find the file.

### Effort levels

| Level | What happens | Best for |
|---|---|---|
| **Quick** | FolderChat searches once with the words of your question and sends the best passages. Fastest, and works with every model. | Looking up a fact, a name or a number |
| **Standard** | The model searches the folders itself, a few times, trying other words when a search finds nothing. | Most questions |
| **Thorough** | More searches, and the model can read whole pages and look through the list of files. Slower, and uses more tokens. | Comparing files, or finding every mention of something |

Search matches words, not meaning: a question about "revenue" won't find a passage that only says "sales". Standard and Thorough get around this by letting the model try other words. Search works best for languages that put spaces between words; Chinese and Japanese text isn't split into words yet.

Only the passages a question needs are sent to the model, not whole files. Standard and Thorough need a model that can use tools, which most cloud models can. If a model can't, FolderChat searches once for it instead, as Quick does.

### Keeping up with changes

FolderChat checks every folder for new, changed and deleted files when it starts and every five minutes, and you can click **Check for changes** at any time. Only files that changed are read again. Hidden files and folders, and `node_modules`, `bin` and `obj` folders, are skipped. Files stored only online, such as OneDrive files that aren't downloaded, are skipped too, so FolderChat doesn't download them.

Limits: 20,000 files per folder, 50 MB per file, and about 3 million characters of text per file.

### Summaries (optional)

In a folder's **Edit** dialog, you can have a model write a short summary of each file. Thorough answers use the summaries to pick which files to read. Writing them means the model reads the start of every file, which takes a while for big folders. With a cloud model, every file's start is sent to that provider, so a local model is suggested.

## Token counts

Each reply shows how many tokens the model read and wrote, as the provider reported them, such as "1,240 in · 380 out". Providers bill your API key by tokens, so these numbers are what your chats cost you. FolderChat itself is free, and doesn't know your providers' prices, so it shows tokens rather than money.

- **Hover a reply's token count** to see the tokens read from the provider's cache (usually billed at a discount), the tokens the model spent thinking, and how much of the model's context the conversation used. A reply that used 80% or more of the context says so: attached files and folder passages count toward it, so starting a new chat leaves the model more room.
- **The chat header** shows the open chat's total.
- **Settings → Usage this month** adds up every model's tokens since the first of the month. Deleting a chat removes its replies from the totals.
- A reply answered from folders at Standard or Thorough effort can take several rounds with the model. Its counts add the rounds up, because each round is billed.
- Some OpenAI-compatible servers don't report tokens. Their replies show no count.

## Installing as an app

FolderChat can sit in your Start menu, Dock or app launcher and open in its own window:

- **Chrome or Edge:** click **Install app** in FolderChat's top bar, or the install icon in the address bar.
- **Safari on a Mac:** choose **File → Add to Dock**.

The app is a window onto the server, so FolderChat still has to be running. If it isn't, the app shows how to start it. The app remembers the address it was installed from: if you start FolderChat on another port, open the new address in your browser and install it again.

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

Database changes are applied automatically when the app starts. An installed app picks up the new version the next time it opens.

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
| "FolderChat can't find that folder" | Check the path, and that its drive is connected. Paste the full path, such as `C:\Users\you\Documents\Contracts`. |
| A folder answer misses something you know is there | Search matches words. Ask using the words the document uses, or choose Standard or Thorough so the model can try other words. Check that the file wasn't skipped under **Show files**. |
| "This model can't search by itself" | The model doesn't support tools, so FolderChat searched once for it. Pick Quick to skip the attempt, or a model that supports tools. |
| Visual Studio says "One or more projects in the solution were not loaded correctly" | You're on Visual Studio 2022. .NET 10 needs Visual Studio 2026 (version 18) or later, which installs alongside 2022. Rider 2025.3+, VS Code and the `dotnet` command line also work. |

Logs are in the `logs` folder inside your data folder.
