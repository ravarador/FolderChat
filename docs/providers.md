# Providers

FolderChat talks to providers through four adapters. Each one plugs a provider's official SDK into the same interface (`IChatClient` from Microsoft.Extensions.AI), so the rest of the app treats them all the same.

| Type | Used for | API key |
|---|---|---|
| **Anthropic** | Claude models, through the official Anthropic SDK | Required. If left blank, `ANTHROPIC_API_KEY` is used. |
| **OpenAI** | OpenAI's own models, through the official OpenAI SDK | Required. If left blank, `OPENAI_API_KEY` is used. |
| **OpenAI-compatible** | Any API that speaks OpenAI's chat-completions format | Usually required. Local servers don't need one. |
| **Ollama** | Local models served by Ollama | Not used |

## Presets

| Preset | Type | Base URL | Get a key |
|---|---|---|---|
| Anthropic (Claude) | Anthropic | default | <https://platform.claude.com/settings/keys> |
| OpenAI | OpenAI | default | <https://platform.openai.com/api-keys> |
| DeepSeek | OpenAI-compatible | `https://api.deepseek.com` | <https://platform.deepseek.com/api_keys> |
| Kimi (Moonshot AI) | OpenAI-compatible | `https://api.moonshot.ai/v1` | <https://platform.kimi.ai/console/api-keys> |
| GLM (Z.ai) | OpenAI-compatible | `https://api.z.ai/api/paas/v4` | <https://z.ai/manage-apikey/apikey-list> |
| Google Gemini | OpenAI-compatible | `https://generativelanguage.googleapis.com/v1beta/openai` | <https://aistudio.google.com/apikey> |
| OpenRouter | OpenAI-compatible | `https://openrouter.ai/api/v1` | <https://openrouter.ai/settings/keys> |
| Groq | OpenAI-compatible | `https://api.groq.com/openai/v1` | <https://console.groq.com/keys> |
| xAI (Grok) | OpenAI-compatible | `https://api.x.ai/v1` | <https://console.x.ai> |
| Mistral | OpenAI-compatible | `https://api.mistral.ai/v1` | <https://console.mistral.ai/api-keys> |
| Ollama (local) | Ollama | `http://localhost:11434` | none |
| LM Studio (local) | OpenAI-compatible | `http://localhost:1234/v1` | none |

Notes:

- **GLM in mainland China:** use `https://open.bigmodel.cn/api/paas/v4` instead.
- **DeepSeek, Kimi and GLM** are run by companies based in China. Read their data policies before you send anything sensitive. Their open-weight models can also run locally through Ollama.
- **OpenRouter** gives one key for hundreds of models, including Kimi, GLM, DeepSeek and Qwen. Its model IDs contain a slash, such as `deepseek/deepseek-chat`.

## Adding a provider that isn't listed

If a provider offers an "OpenAI-compatible" API, it works with FolderChat:

1. Choose **Custom** under **Start from**.
2. Set **Type** to **OpenAI-compatible**.
3. Enter its base URL: the part before `/chat/completions`.
4. Paste the key and save.

If the provider doesn't support listing models (`GET /models`), add model IDs by hand on the provider's card.

## How keys are stored

API keys are encrypted with ASP.NET Core Data Protection before they are saved:

- On Windows, the encryption key ring is itself protected with DPAPI, so only your Windows account can decrypt it.
- The API never returns a key. The browser only learns whether one is saved.
- To remove a key, edit the provider and type a single space in the API key box.
