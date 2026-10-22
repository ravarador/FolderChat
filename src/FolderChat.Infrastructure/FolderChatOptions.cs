namespace FolderChat.Infrastructure;

/// <summary>Settings under the <c>FolderChat</c> section of appsettings.json or <c>FolderChat__*</c> environment variables.</summary>
public sealed class FolderChatOptions
{
    public const string SectionName = "FolderChat";

    /// <summary>
    /// Where the database, encryption keys and logs are kept. Defaults to a <c>FolderChat</c> folder in the
    /// user's local app-data directory.
    /// </summary>
    public string? DataDirectory { get; set; }

    /// <summary>Opens the chat in the default browser once the server is ready.</summary>
    public bool OpenBrowserOnStart { get; set; }
}

/// <summary>Resolved locations of everything FolderChat writes to disk.</summary>
public sealed class AppPaths
{
    public AppPaths(string dataDirectory)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }

    public string DatabasePath => Path.Combine(DataDirectory, "folderchat.db");

    public string KeysDirectory => Path.Combine(DataDirectory, "keys");

    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public static AppPaths FromOptions(FolderChatOptions options) =>
        new(string.IsNullOrWhiteSpace(options.DataDirectory) ? DefaultDataDirectory() : options.DataDirectory);

    /// <summary>%LOCALAPPDATA%\FolderChat on Windows, ~/.local/share/FolderChat on Linux, ~/Library/Application Support/FolderChat on macOS.</summary>
    public static string DefaultDataDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "FolderChat");
}
