# Configuration

FolderChat reads settings from `src/FolderChat.Web/appsettings.json`, environment variables and command-line arguments. Later sources win.

| Setting | Default | What it does |
|---|---|---|
| `Urls` | `http://127.0.0.1:5080` | The address the server listens on. |
| `AllowedHosts` | `localhost;127.0.0.1;[::1]` | Host names the server answers to. Requests for any other host get a 400. |
| `FolderChat:DataDirectory` | your OS's app-data folder | Where the database, keys and logs live. |
| `FolderChat:OpenBrowserOnStart` | `false` | Opens the chat in your default browser when the server starts. |
| `FolderChat:Api:Urls` | empty | Extra addresses, separated by `;`, that serve only the [REST API](api.md), such as `http://0.0.0.0:5081`. |
| `FolderChat:Api:Defaults:Provider` | empty | The provider, by name or ID, for API requests that don't name one. |
| `FolderChat:Api:Defaults:Model` | empty | The model for API requests that don't name one. |
| `FolderChat:Api:Defaults:Folders` | empty | Knowledge folders, by name or ID, for API requests that don't list any. |
| `FolderChat:Api:Defaults:Effort` | `Standard` | `Quick`, `Standard` or `Thorough`, for API requests that don't say. |
| `FolderChat:Api:Defaults:Instructions` | empty | Instructions sent to the model with API requests that don't send their own. |
| `Serilog:MinimumLevel:Default` | `Information` | How much is written to the console and log files. |

## Setting values

On the command line (note the `--` before app arguments when using `dotnet run`):

```bash
dotnet run --project src/FolderChat.Web -- --urls http://127.0.0.1:5090 --FolderChat:OpenBrowserOnStart=true
```

As environment variables, with `__` (two underscores) in place of `:`:

```bash
export FolderChat__DataDirectory="$HOME/folderchat-data"
export ASPNETCORE_URLS="http://127.0.0.1:5090"
```

## Letting apps on other computers use the API

The REST API doesn't need FolderChat itself to be opened up: `FolderChat:Api:Urls` adds an address that serves only `/api/v1`, with API keys. See [Calling from another computer](api.md#calling-from-another-computer).

## Opening FolderChat to other devices (not recommended yet)

FolderChat has no sign-in yet. Anyone who can reach the port can use your saved API keys and read your conversations. If you still want to reach it from another device on a network you trust:

1. Listen on all interfaces: `--urls http://0.0.0.0:5080`.
2. Add the host name or IP you'll use to `AllowedHosts`, for example `localhost;127.0.0.1;192.168.1.20`.

A sign-in for network access is on the roadmap.
