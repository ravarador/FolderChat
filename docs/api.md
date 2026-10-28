# REST API

Your own apps can ask FolderChat questions through a REST API at `/api/v1`. For example, an admin app can answer its users' questions from a folder of manuals, with citations.

The API is for your app's **server**. It uses an API key, and a key in browser JavaScript could be copied by anyone who opens the page, so the API sends no CORS headers and browsers can't call it from another site.

## Set it up

1. Run FolderChat on the same computer as your app, and set up a provider and a knowledge folder as usual.
2. In **Settings > API keys**, give the key a name, such as "Admin app", and click **Create key**. Copy the key: FolderChat keeps only a fingerprint of it, so it can't show it again.
3. Store the key in your app's configuration, not in its source code.
4. Optional: set defaults, so each request can be just a message. In `appsettings.json`:

```json
"FolderChat": {
  "Api": {
    "Defaults": {
      "Model": "claude-sonnet-4-5",
      "Folders": [ "Admin manuals" ],
      "Effort": "Standard",
      "Instructions": "Answer only from the manuals. If they don't cover the question, say so."
    }
  }
}
```

With no keys, every call gets 401. Revoking a key in Settings stops it straight away.

Chats your app starts show in FolderChat's chat list with an **API** badge, so you can read them. Your app can only see the chats it started through the API, never the ones you start in FolderChat.

## Ask a question

```bash
curl http://127.0.0.1:5080/api/v1/chat \
  -H "Authorization: Bearer $FOLDERCHAT_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{"message": "How do I reset a password?", "model": "claude-sonnet-4-5", "folders": ["Admin manuals"], "user": "admin-42"}'
```

The reply:

```json
{
  "conversationId": "d65c77d6-2490-4256-9884-fc44ee41071c",
  "messageId": "01693a97-8496-4a53-b1df-eb38f23d42c0",
  "text": "Open Users, then click Reset password [1].",
  "sources": [
    {
      "number": 1,
      "folderId": "4d024dc7-aa7c-4779-b8b3-05e59e302d88",
      "folder": "Admin manuals",
      "file": "user-guide.md",
      "page": null,
      "text": "To reset a password, open Users, pick the user and click Reset password."
    }
  ],
  "model": { "providerId": "88a1f460-77ba-46e5-84e0-2d1df52e19b9", "provider": "Anthropic", "id": "claude-sonnet-4-5" },
  "usage": { "inputTokens": 321, "outputTokens": 9, "cachedInputTokens": null, "reasoningTokens": null },
  "finishReason": "stop"
}
```

`text` is Markdown. Citations like `[1]` match `number` in `sources`; `file` is the file's path inside the folder, and `text` is the passage the model was given.

### Request fields

Only `message` is required, as long as a model is set in the defaults.

| Field | What it does |
|---|---|
| `message` | The question. |
| `conversationId` | Continues that conversation, so the model sees the earlier messages. Leave it out to start a new one. |
| `model` | The model's ID or name, as `GET /api/v1/models` lists them. |
| `provider` | The provider's name or ID. Needed only when two providers have a model with the same name. |
| `folders` | Knowledge folders to answer from, by name or ID. `[]` answers without folders. |
| `effort` | `Quick`, `Standard` or `Thorough`, as in the chat page. |
| `instructions` | Extra instructions for the model, sent with this message. |
| `user` | Your app's ID for its user. Saved with a new conversation, so you can list one user's chats. |
| `stream` | `true` to get the reply as it's written, as Server-Sent Events. |

A value left out comes from the conversation being continued (its model, folders and effort), then from the defaults in `appsettings.json`.

## Stream the reply

With `"stream": true`, the reply comes as Server-Sent Events, in this order:

| Event | Data |
|---|---|
| `started` | `conversationId`, `title`, `isNewConversation` |
| `activity` | `text`: something done on the way, such as "Searched Admin manuals: 3 passages". Any number. |
| `delta` | `text`: the next piece of the reply. Any number. |
| `sources` | `sources`, as in the JSON reply. Only when folders were used. |
| `completed` | `conversationId`, `messageId`, `model`, `usage`, `finishReason` |
| `error` | `error`, and `conversationId` when the question was saved. Ends the stream instead of `completed`. |

Closing the connection stops the model. Whatever it had written is kept in the conversation.

## Other endpoints

| Method and path | What it does |
|---|---|
| `GET /api/v1/models` | Enabled providers and their models. |
| `GET /api/v1/folders` | Knowledge folders, how many files are searchable, and whether a scan is running. |
| `GET /api/v1/conversations?user=admin-42` | Conversations started through the API, newest first. `user` is optional. |
| `GET /api/v1/conversations/{id}` | One conversation's messages, with their sources and token counts. |
| `DELETE /api/v1/conversations/{id}` | Deletes a conversation. |
| `GET /api/v1/openapi.json` | The OpenAPI description, for Swagger UI, Postman or a client generator. Needs no key. |

## Errors

Errors come back as [problem details](https://www.rfc-editor.org/rfc/rfc9457) JSON, with the reason in `detail`.

| Status | When |
|---|---|
| 400 | The request can't be used: an empty message, no model named and no default, a name that matches two providers or folders. |
| 401 | No API key, or one that isn't valid. |
| 404 | A model, folder or conversation that doesn't exist. |
| 502 | The provider refused or failed, such as a rejected provider key. `conversationId` says where the question was saved. |

## Examples

### C#

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;

var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5080") };
http.DefaultRequestHeaders.Authorization =
    new AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable("FOLDERCHAT_API_KEY"));

var response = await http.PostAsJsonAsync("/api/v1/chat", new
{
    message = "How do I reset a password?",
    user = "admin-42",
});
response.EnsureSuccessStatusCode();
var answer = await response.Content.ReadFromJsonAsync<FolderChatAnswer>();

Console.WriteLine(answer!.Text);
foreach (var source in answer.Sources)
{
    Console.WriteLine($"[{source.Number}] {source.File}{(source.Page is int page ? $", page {page}" : "")}");
}

record FolderChatAnswer(Guid ConversationId, string Text, List<FolderChatSource> Sources);

record FolderChatSource(int Number, string Folder, string File, int? Page, string? Text);
```

This one relies on the defaults for the model and folders. Keep `ConversationId` and send it with the user's next question to continue the chat.

### JavaScript (Node.js 18 or later), streaming

```js
const response = await fetch("http://127.0.0.1:5080/api/v1/chat", {
    method: "POST",
    headers: {
        Authorization: `Bearer ${process.env.FOLDERCHAT_API_KEY}`,
        "Content-Type": "application/json",
    },
    body: JSON.stringify({ message: "How do I reset a password?", effort: "Quick", stream: true }),
});
if (!response.ok) throw new Error((await response.json()).detail);

// Server-Sent Events: blocks separated by a blank line, each with an "event:" and a "data:" line.
let buffer = "";
for await (const chunk of response.body.pipeThrough(new TextDecoderStream())) {
    buffer += chunk;
    let end;
    while ((end = buffer.indexOf("\n\n")) >= 0) {
        const block = buffer.slice(0, end);
        buffer = buffer.slice(end + 2);
        const event = block.match(/^event: (.*)$/m)[1];
        const data = JSON.parse(block.match(/^data: (.*)$/m)[1]);

        if (event === "delta") process.stdout.write(data.text);
        if (event === "sources") data.sources.forEach(s => console.log(`\n[${s.number}] ${s.file}`));
        if (event === "error") throw new Error(data.error);
    }
}
```

To show a streamed reply in your app's own page, have your server pass the events on to the browser, keeping the key on the server.

## Calling from another computer

By default FolderChat listens only on `127.0.0.1`, so only apps on the same computer can call it. If your app runs elsewhere:

1. Add an address that serves only the API, such as `"FolderChat": { "Api": { "Urls": "http://0.0.0.0:5081" } }`. On that address, everything but `/api/v1` is 404: Settings, your chats and the rest of FolderChat stay on `127.0.0.1:5080`.
2. Add the host name or IP your app will use to `AllowedHosts`, for example `localhost;127.0.0.1;[::1];myserver`.
3. The API key travels with every request, so use HTTPS across a network: put FolderChat behind a reverse proxy such as IIS, nginx or Caddy that terminates TLS.
4. Allow the port through the firewall only from the computers that need it.
