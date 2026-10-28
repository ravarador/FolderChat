# Architecture

FolderChat is a local-first web app: an ASP.NET Core server runs on your computer and your browser is the UI. That gives it full access to your files, which knowledge folders need, while staying cross-platform and easy to contribute to.

## Projects

```
src/
  FolderChat.Core            Entities: providers, conversations and their attachments, knowledge folders, files and passages. No dependencies.
  FolderChat.Application     Use cases (ChatService, ProviderService, ConversationService, AttachmentService, FolderService, ApiChatService, ApiKeyService) and the ports they need.
  FolderChat.Infrastructure  Adapters: provider SDKs, EF Core + SQLite, key encryption, file readers, the folder indexer and search.
  FolderChat.Web             ASP.NET Core host: Razor pages, the app's JSON API, the streaming endpoint, the public REST API.
tests/
  FolderChat.Tests           Unit, adapter and end-to-end API tests.
```

Dependencies point inward: Web → Infrastructure → Application → Core.

The Application layer defines these ports, and Infrastructure implements them:

| Port | Purpose | Implementation |
|---|---|---|
| `IChatClientFactory` | Build a chat client for a provider and model | `ChatClientFactory`: the four adapters |
| `IModelCatalog` | List a provider's models | `ModelCatalog` |
| `IProviderStore`, `IConversationStore`, `IAttachmentStore`, `IKnowledgeStore`, `IApiKeyStore` | Persistence | EF Core stores over SQLite |
| `ISecretProtector` | Encrypt API keys | ASP.NET Core Data Protection |
| `IProviderErrorDescriber` | Turn SDK exceptions into readable messages | `ProviderErrorDescriber` |
| `ITextExtractor` | Read the text of a file | `TextExtractor`: PdfPig for PDFs, the Open XML SDK for Word, encoding detection for text |
| `IKnowledgeIndex` | Search folder passages and read files back | `KnowledgeIndex`: SQLite FTS5 with BM25 ranking |
| `IFolderScanQueue` | Scan folders in the background and report progress | `FolderIndexer`, a hosted service that runs `FolderScanner` |

## The adapters

Every provider is turned into a `Microsoft.Extensions.AI.IChatClient`:

| Kind | How |
|---|---|
| Anthropic | `AnthropicClient.AsIChatClient(model, maxTokens)` from the official Anthropic SDK |
| OpenAI | `OpenAIClient.GetChatClient(model).AsIChatClient()` |
| OpenAI-compatible | The same OpenAI client with `OpenAIClientOptions.Endpoint` set to the provider's base URL |
| Ollama | `OllamaApiClient` from OllamaSharp, which implements `IChatClient` itself |

Because `ChatService` only sees `IChatClient`, adding a provider kind doesn't touch the chat logic, the database or the UI. See CONTRIBUTING.md for the steps.

## What happens when you send a message

1. The browser POSTs to `/api/chat/stream` with the conversation ID (or none, for a new chat), provider, model, text, the IDs of any attached files, and the IDs of the chat's folders with its effort level.
2. `ChatService` checks the input, the provider, the folders and the files, then saves the user's message and the chat's folders, and links the files to the message, in the same save.
3. It replays the conversation's earlier turns, with their files, ordered by `Sequence`, to the model through `IChatClient.GetStreamingResponseAsync`. With folders, it adds their instructions and passages or tools, as described under [Knowledge folders](#knowledge-folders).
4. Each step is sent to the browser as a Server-Sent Event:
   - `started`: the conversation ID and title
   - `delta`: one chunk of text
   - `activity`: something done on the way, such as a search of the folders
   - `sources`: the numbered folder passages the model was shown
   - `completed`: the token counts (read, written, cached, thinking, and the last round's total) and finish reason, or `error` instead
5. The browser renders Markdown as it arrives, sanitized with DOMPurify.
6. When the stream ends, the reply is saved:
   - Complete when the model finished.
   - Failed when the provider returned an error. Failed replies are left out of later turns.
   - Stopped when the user pressed Stop or closed the page. The partial text is kept, and saved even though the request was cancelled.

## What happens when you attach a file

1. The browser uploads the file to `POST /api/attachments` as soon as it is attached, so problems show before the message is sent.
2. `AttachmentService` checks the size and asks `ITextExtractor` for the text. The file's first bytes decide how it is read (PDF, Word or text), so a renamed file still works. A file that can't be read gets a message saying why and what to do.
3. The text, cut off at 500,000 characters, is saved as an attachment with no message yet. The file itself isn't kept.
4. When the message is sent, `ChatService` links the attachments to it. Attachments that were never sent are deleted a day later, the next time a file is uploaded.
5. `GET /api/attachments/{id}` returns the text, which the chat shows when a file is clicked.

### How files reach the model

Each file's text goes into the user's message, in a document tag, before what the user typed:

```
<document name="report.pdf">
[Page 1]
Quarterly report…
</document>

What does it say about costs?
```

PDFs get a `[Page n]` line per page, so the model can cite pages. Word headings, list items and tables come out as Markdown. When a file was cut off, a note inside its tag tells the model so.

## Knowledge folders

### Reading a folder

`FolderIndexer` is a background service that scans one folder at a time: when a folder is added, when **Check for changes** is clicked, at startup, and every five minutes. Each scan (`FolderScanner`):

1. Lists the files, skipping hidden ones, links and junctions, `node_modules`, `bin` and `obj` folders, and FolderChat's own data folder.
2. Forgets files that are gone.
3. Reads new files, and files whose size or modified time changed, with the same `ITextExtractor` as attachments. Files it can't read are kept with a note saying why, which Settings shows.
4. Splits each file's text into passages of about 1,200 characters, keeping paragraphs whole and never crossing a page. Each file is replaced in one transaction, so a search never sees it half done.
5. Writes summaries for files without one, when the folder has summaries on.

Passages are stored in the `Passages` table. A SQLite FTS5 table, `PassageSearch`, indexes their text without storing a copy ("external content"). Triggers on `Passages` keep it in step, including when deletes cascade from files and folders. The tokenizer stems English words and ignores accents, so "costs" finds "cost" and "cafe" finds "café".

### Choosing a folder

A browser never tells a page where a folder is, and FolderChat needs the path to keep the folder's index up to date. So the server, which runs on the same computer, opens the folder window itself: `POST /api/folders/choose` asks `IFolderPicker` for a path, then adds the folder, or finds it if it was added before.

- **Windows:** `WindowsFolderDialog` shows the Common Item Dialog (`IFileOpenDialog` in folder mode), the window File Explorer uses, on a thread of its own because it needs a single-threaded apartment. An invisible, always-on-top owner window keeps it in front of the browser even when Windows won't give the server the focus, and a hook centers it over the browser before it first shows.
- **Mac:** `osascript` with `choose folder`.
- **Linux:** `zenity` or `kdialog`, when one is installed and there's a display.

The window opens on the computer running FolderChat, so the endpoint, and the buttons for it, are only offered to a browser on that computer (a loopback address), and only where there's a desktop. Elsewhere the user pastes the folder's path. One window opens at a time, and closing the page closes it.

### Answering from folders

Each conversation keeps its own folders (`Conversation.FolderIds`, a JSON list of up to 10) and effort level. They're saved with every message, and `PUT /api/conversations/{id}/folders` saves them when the user changes them in an open chat without sending anything.

`FolderAnswering` leaves out folders that were removed since, checks the rest can be used, and starts a `FolderSession` for the reply. The session holds the instructions for the model, the tools it may call, and the numbered passages it has been shown. A search covers all the chat's folders at once, ranked together. Paths start with the folder's name, as in `Reports/budget.md`, so the model can tell the folders apart and pass a path back to `read_file`. Two folders with the same name become `Docs` and `Docs (2)`.

- **Quick:** FolderChat searches with the words of the user's message and puts the best passages before it, in a `<folder_passages>` block. One model call, no tools.
- **Standard:** the model gets a `search_folder` tool. `ChatService` runs a tool loop: it streams a round, runs the tool calls the model made, sends their results back, and repeats until the model answers.
- **Thorough:** also `read_file`, which returns a whole page (or a slice of about 6,000 characters for files without pages), and `list_files`, with summaries when they're on.

`EffortLimits` caps each level's searches, reads and the folder text sent per question. When a limit is reached, the tools say so and ask for an answer, and the last allowed round sets `ToolMode` to none. A model that fails at once with an error about tools gets a Quick search instead.

Every passage the model is shown gets a number, and the instructions ask it to cite passages as [n]. The passages are sent to the browser in a `sources` event and saved with the reply as JSON, so the UI can link [n] to the exact passage, even after a reload. Search activity is streamed as `activity` events.

## Token counts

Providers report token counts in a `UsageContent` in the stream, some once at the end and some split across the start and end, so `ChatService` keeps the largest value of each count in a round. A reply with tool rounds adds the rounds up, because each round is billed. The last round's input and output together say how full the model's context ended up, which the browser compares with the model's context size when the provider reports it.

The counts are saved on the reply (`InputTokens`, `OutputTokens`, `CachedInputTokens`, `ReasoningTokens`, `ContextTokens`). `GET /api/usage` adds up this month's replies by model for Settings. Summaries add their counts to their folder's `SummaryInputTokens` and `SummaryOutputTokens`.

## Installing as an app

`manifest.webmanifest` and the icons in `wwwroot/icons` make FolderChat installable, and `site.js` registers `sw.js`, a service worker. The worker handles page loads only: when the server doesn't answer, it shows `offline.html`, which says how to start FolderChat. It caches nothing else, so pages, scripts and the API always come from the server and an update can't leave an old script behind. Browsers allow service workers on `localhost` and `127.0.0.1` without HTTPS.

## The REST API

`/api/v1` is for other apps, called from their server. `V1Controller` sits beside the app's own controllers but is guarded differently: `[ApiKeyAuth]` checks `Authorization: Bearer fck_…` against the keys made in Settings, and `[IgnoreAntiforgeryToken]` turns off the cookie-based check, which a server-to-server caller can't pass. The app's own endpoints keep the anti-forgery check, so a key can't be used on them.

`ApiKeyService` makes keys from 32 random bytes and stores only their SHA-256 hash and first 12 characters. A request's key is hashed and looked up, so the stored hashes are never compared character by character.

`ApiChatService` turns a request into the same `ChatRequest` the chat page sends: it resolves provider, model and folder names to IDs, and fills in what's missing from the conversation being continued and then from `FolderChat:Api:Defaults`. The reply comes from `ChatService` unchanged, and is either collected into one JSON reply or passed on as Server-Sent Events. Conversations it starts have `Source = Api` and the caller's `ExternalUser`. The API only finds `Api` conversations, so the owner's own chats can't be read or continued with a key. Extra instructions go to the model as a system message after the folder instructions.

`FolderChat:Api:Urls` adds listening addresses, and `ApiOnlyAddresses` answers only `/api/v1` on their ports. `Microsoft.AspNetCore.OpenApi` describes the v1 endpoints at `/api/v1/openapi.json`, from the XML comments on `V1Controller` and the contracts in `ApiContracts.cs`. Those contracts only gain fields; a breaking change would be `/api/v2`.

## Decisions

- **Local web app, not a desktop app.** The server runs locally, so it can read any folder you choose, while the UI stays a normal web page that works on Windows, macOS and Linux. A thin desktop wrapper can come later.
- **Microsoft.Extensions.AI.** This is .NET's standard abstraction for chat models. It keeps provider code at the edges and makes the chat logic easy to test with a fake client.
- **SQLite.** Zero setup for anyone who clones the repo. EF Core migrations apply automatically at startup.
- **Server-Sent Events over a POST.** One request per reply, with plain HTTP and no WebSocket server. Closing the connection cancels the model call.
- **Enums stored as text, times as UTC.** The database file stays readable with any SQLite browser.
- **No mocking library.** The tests use small hand-written fakes and a scripted HTTP handler. Each adapter is tested against its real SDK, offline.
- **Attachments are sent as text.** FolderChat reads files itself rather than passing them to providers. Every provider accepts text, including local models and OpenAI-compatible APIs that take no files, and the user can see exactly what was sent. The same readers serve knowledge folders. Scans and images need a different path, through vision models, which can come later.
- **Full-text search, not embeddings.** SQLite's FTS5 is built in: no extra model to download or run, nothing sent anywhere to index a folder, and exact names and numbers are found reliably. Its weak spot, different wording, is covered by letting the model search for itself and try other words. Embeddings could be added later behind `IKnowledgeIndex` without changing the chat.
- **FolderChat runs the tool loop itself.** Rather than `FunctionInvokingChatClient`, `ChatService` runs the rounds, so it can enforce each effort level's limits, force an answer on the last round, stream what the model is doing, and keep the numbered sources. Tools are declared with JSON schemas (`AIFunctionFactory.CreateDeclaration`), which every adapter accepts.
- **The server opens the folder window.** Browsers keep folder paths from web pages, and a page could only read the files once, not keep them indexed. The server runs on the user's computer, so it shows the system's own window, as a desktop app would. Pasting a path still works where it can't.
- **Folders belong to the chat.** Each chat chooses its own folders, rather than the app having one current folder, so a chat about contracts and a chat about reports can sit side by side, and reopening a chat brings back what it was answering from.
- **Write-ahead logging.** The database uses WAL mode, so chats can search while a scan writes.
- **Tokens, not money.** Providers report exact token counts with each reply, but not what it cost, and most don't publish prices through their APIs. So FolderChat shows tokens, which are always right, and leaves prices to the providers' own dashboards.
- **A service worker that caches nothing.** The server is always on the same computer, so a cache would only add a way to serve an old script after an update. The worker exists to make FolderChat installable and to explain what to do when the server isn't running.
- **Upload first, send later.** A file is read when it is attached, not when the message is sent, so a file that can't be read is reported right away and the chat request stays plain JSON.
