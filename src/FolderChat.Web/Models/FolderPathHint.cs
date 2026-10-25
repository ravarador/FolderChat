namespace FolderChat.Web.Models;

/// <summary>
/// How to get a folder's full path, for the forms that add a folder. The server runs on the user's own computer, so
/// its operating system is theirs.
/// </summary>
public static class FolderPathHint
{
    public static string Example =>
        OperatingSystem.IsWindows() ? @"C:\Users\you\Documents\Contracts"
        : OperatingSystem.IsMacOS() ? "/Users/you/Documents/Contracts"
        : "/home/you/Documents/Contracts";

    public static string Tip =>
        OperatingSystem.IsWindows() ? "In File Explorer, right-click the folder and choose Copy as path, then paste it here."
        : OperatingSystem.IsMacOS() ? "In Finder, select the folder and press Option+Command+C to copy its path, then paste it here."
        : "Paste the folder's full path.";
}
