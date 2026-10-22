# Changelog

All notable changes are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed

- A rejected API key now shows the provider's own reason, which often includes the last characters of the key it received.
- A cloud API saved with the Ollama type now gets a clear hint to switch to OpenAI-compatible. Ollama providers never send an API key. The Settings dialog warns about this before saving, too.
- Expected API errors (invalid input, missing items, provider failures) no longer stop the Visual Studio debugger as "user-unhandled" exceptions.
- Request logs record the status code the browser actually received. Provider failures are logged as warnings instead of errors with stack traces.

## [0.1.0] - 2026-10-21

### Added

- Local web app on ASP.NET Core 10 that listens on `127.0.0.1:5080`, with data stored in SQLite.
- Four provider adapters behind Microsoft.Extensions.AI's `IChatClient`:
  - Anthropic (official SDK)
  - OpenAI (official SDK)
  - OpenAI-compatible APIs
  - Ollama
- Presets for DeepSeek, Kimi, GLM, Gemini, OpenRouter, Groq, xAI, Mistral, Ollama and LM Studio.
- Model lists loaded from each provider, with per-model show and hide, filtering, and manually added model IDs.
- Streaming chat over Server-Sent Events, with Markdown rendering, code highlighting, copy buttons and a Stop button that keeps partial replies.
- Conversation history with search, rename and delete. A conversation can switch models between messages.
- API keys encrypted at rest with ASP.NET Core Data Protection (DPAPI on Windows).
- Host-header allowlist and anti-forgery tokens on all change requests.
- Readable provider errors, including the explanation in Ollama's error responses.
- Light and dark themes.
