# Contributing to FolderChat

Thanks for helping! Bug reports, ideas and pull requests are all welcome.

## Dev setup

You need the .NET 10 SDK, and one of these:

- Visual Studio 2026 (version 18) or later
- JetBrains Rider 2025.3 or later
- VS Code with the C# Dev Kit

Visual Studio 2022 can't open the solution. Its MSBuild 17.x can't load the .NET 10 SDK, so every project fails to load.

```bash
git clone https://github.com/ravarador/FolderChat.git
cd FolderChat
dotnet tool restore
dotnet build
dotnet test
dotnet run --project src/FolderChat.Web
```

`dotnet tool restore` installs the EF Core command-line tool at the version this repo uses.

To keep test data away from your real conversations, point the app at another folder while developing:

```bash
dotnet run --project src/FolderChat.Web -- --FolderChat:DataDirectory=./.data
```

## Project layout

See [docs/architecture.md](docs/architecture.md). In short:

- **Core:** entities.
- **Application:** use cases and ports.
- **Infrastructure:** the SDK adapters and storage.
- **Web:** the ASP.NET Core host and front end. The front end is plain JavaScript with Bootstrap 5, with no build step.

## Before you open a pull request

- `dotnet build` shows no warnings and `dotnet test` passes.
- New behavior has a test. Use the fakes in `tests/FolderChat.Tests/Fakes`; tests must not need network access or API keys.
- Follow `.editorconfig`.
- Add a line to `CHANGELOG.md` under **Unreleased**.

## Changing the database

1. Edit the entities in Core, or `FolderChatDbContext`.
2. Add a migration:

   ```bash
   dotnet ef migrations add <Name> --project src/FolderChat.Infrastructure --startup-project src/FolderChat.Web --output-dir Persistence/Migrations
   ```

3. Commit the generated files. The app applies migrations on startup.

## Adding a provider kind

Most providers don't need a new kind: if they speak OpenAI's chat-completions format, add a preset instead.

**To add a preset**, add an entry to `ProviderPresets` in `src/FolderChat.Application/Providers/ProviderPresets.cs` and a row to `docs/providers.md`.

**To add a new kind:**

1. Add it to `ProviderKind` in Core.
2. Build its SDK client in `ProviderClients` (Infrastructure/AI), using a named `HttpClient` so tests can script it.
3. Return an `IChatClient` for it in `ChatClientFactory`. If the SDK has no `IChatClient` adapter, write one by deriving from `DelegatingChatClient` or implementing `IChatClient`.
4. List its models in `ModelCatalog`.
5. Map its exceptions in `ProviderErrorDescriber`.
6. Add adapter tests in `tests/FolderChat.Tests/Infrastructure/AdapterTests.cs` with a scripted response, then add it to the Settings type list and the docs.

## Reading a new file type

Attached files are read by `TextExtractor` in `src/FolderChat.Infrastructure/Documents`. Any file that decodes as text already works, so a new type is only needed for binary formats.

1. Recognize the format in `TextExtractor.Identify`, by its first bytes where it has a signature.
2. Write a reader next to `PdfText` and `WordText` that returns an `ExtractedText`. Collect the text with `BoundedText`, so reading stops at the length limit.
3. Add tests to `tests/FolderChat.Tests/Infrastructure/TextExtractorTests.cs`. Build the sample file in memory, as `TestFiles` does, instead of committing a binary file.
4. Update the list of readable files in `TextExtractor`'s messages, the README and `docs/getting-started.md`.

## Reporting security issues

Please don't open a public issue. See [SECURITY.md](SECURITY.md).
