# Architecture

FolderChat is a local-first web app: an ASP.NET Core server runs on your computer and your browser is the UI. That gives it full access to your files (needed for knowledge folders in v0.3) while staying cross-platform and easy to contribute to.

## Projects

```
src/
  FolderChat.Core            Entities: Provider, ProviderModel, Conversation, ConversationMessage. No dependencies.
  FolderChat.Application     Use cases (ChatService, ProviderService, ConversationService) and the ports they need.
  FolderChat.Infrastructure  Adapters: provider SDKs, EF Core + SQLite, key encryption.
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
| `IProviderStore`, `IConversationStore` | Persistence | EF Core stores over SQLite |
| `ISecretProtector` | Encrypt API keys | ASP.NET Core Data Protection |
| `IProviderErrorDescriber` | Turn SDK exceptions into readable messages | `ProviderErrorDescriber` |

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

1. The browser POSTs to `/api/chat/stream` with the conversation ID (or none, for a new chat), provider, model and text.
2. `ChatService` checks the input and the provider, then saves the user's message.
3. It replays the conversation's earlier turns, ordered by `Sequence`, to the model through `IChatClient.GetStreamingResponseAsync`.
4. Each text chunk is sent to the browser as a Server-Sent Event:
   - `started`: the conversation ID and title
   - `delta`: one chunk of text
   - `completed`: the token counts and finish reason, or `error` instead
5. The browser renders Markdown as it arrives, sanitized with DOMPurify.
6. When the stream ends, the reply is saved:
   - Complete when the model finished.
   - Failed when the provider returned an error. Failed replies are left out of later turns.
   - Stopped when the user pressed Stop or closed the page. The partial text is kept, and saved even though the request was cancelled.

## Decisions

- **Local web app, not a desktop app.** The server runs locally, so it can read any folder you choose, while the UI stays a normal web page that works on Windows, macOS and Linux. A thin desktop wrapper can come later.
- **Microsoft.Extensions.AI.** This is .NET's standard abstraction for chat models. It keeps provider code at the edges and makes the chat logic easy to test with a fake client.
- **SQLite.** Zero setup for anyone who clones the repo. EF Core migrations apply automatically at startup.
- **Server-Sent Events over a POST.** One request per reply, with plain HTTP and no WebSocket server. Closing the connection cancels the model call.
- **Enums stored as text, times as UTC.** The database file stays readable with any SQLite browser.
- **No mocking library.** The tests use small hand-written fakes and a scripted HTTP handler. Each adapter is tested against its real SDK, offline.
