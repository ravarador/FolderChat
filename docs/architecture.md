# Architecture

FolderChat is a local-first web app: an ASP.NET Core server runs on your computer and your browser is the UI. That gives it full access to your files (needed for knowledge folders in v0.3) while staying cross-platform and easy to contribute to.

## Projects

```
src/
  FolderChat.Core            Entities: Provider, ProviderModel, Conversation, ConversationMessage, Attachment. No dependencies.
  FolderChat.Application     Use cases (ChatService, ProviderService, ConversationService, AttachmentService) and the ports they need.
  FolderChat.Infrastructure  Adapters: provider SDKs, EF Core + SQLite, key encryption, file readers.
  FolderChat.Web             ASP.NET Core host: Razor pages, JSON API, the streaming endpoint.
tests/
  FolderChat.Tests           Unit, adapter and end-to-end API tests.
```

Dependencies point inward: Web → Infrastructure → Application → Core.

The Application layer defines these ports, and Infrastructure implements them:

| Port | Purpose | Implementation |
|---|---|---|
| `IChatClientFactory` | Build a chat client for a provider and model | `ChatClientFactory`: the four adapters |
| `IModelCatalog` | List a provider's models | `ModelCatalog` |
| `IProviderStore`, `IConversationStore`, `IAttachmentStore` | Persistence | EF Core stores over SQLite |
| `ISecretProtector` | Encrypt API keys | ASP.NET Core Data Protection |
| `IProviderErrorDescriber` | Turn SDK exceptions into readable messages | `ProviderErrorDescriber` |
| `ITextExtractor` | Read the text of an attached file | `TextExtractor`: PdfPig for PDFs, the Open XML SDK for Word, encoding detection for text |

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

1. The browser POSTs to `/api/chat/stream` with the conversation ID (or none, for a new chat), provider, model, text and the IDs of any attached files.
2. `ChatService` checks the input, the provider and the files, then saves the user's message and links the files to it in the same save.
3. It replays the conversation's earlier turns, with their files, ordered by `Sequence`, to the model through `IChatClient.GetStreamingResponseAsync`.
4. Each text chunk is sent to the browser as a Server-Sent Event:
   - `started`: the conversation ID and title
   - `delta`: one chunk of text
   - `completed`: the token counts and finish reason, or `error` instead
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

## Decisions

- **Local web app, not a desktop app.** The server runs locally, so it can read any folder you choose, while the UI stays a normal web page that works on Windows, macOS and Linux. A thin desktop wrapper can come later.
- **Microsoft.Extensions.AI.** This is .NET's standard abstraction for chat models. It keeps provider code at the edges and makes the chat logic easy to test with a fake client.
- **SQLite.** Zero setup for anyone who clones the repo. EF Core migrations apply automatically at startup.
- **Server-Sent Events over a POST.** One request per reply, with plain HTTP and no WebSocket server. Closing the connection cancels the model call.
- **Enums stored as text, times as UTC.** The database file stays readable with any SQLite browser.
- **No mocking library.** The tests use small hand-written fakes and a scripted HTTP handler. Each adapter is tested against its real SDK, offline.
- **Attachments are sent as text.** FolderChat reads files itself rather than passing them to providers. Every provider accepts text, including local models and OpenAI-compatible APIs that take no files, and the user can see exactly what was sent. The same readers can serve the knowledge folders planned for v0.3. Scans and images need a different path, through vision models, which can come later.
- **Upload first, send later.** A file is read when it is attached, not when the message is sent, so a file that can't be read is reported right away and the chat request stays plain JSON.
