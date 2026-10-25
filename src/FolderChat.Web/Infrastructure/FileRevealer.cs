using System.Diagnostics;

namespace FolderChat.Web.Infrastructure;

internal static class FileRevealer
{
    /// <summary>
    /// Shows the file in the file manager: selected in Explorer or Finder, or its folder on Linux. It never opens the
    /// file, which for a script would run it. Returns false when there is no desktop to show it on.
    /// </summary>
    public static bool Reveal(string path)
    {
        try
        {
            using var process = OperatingSystem.IsWindows()
                ? Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\""))
                : OperatingSystem.IsMacOS()
                    ? Process.Start("open", ["-R", path])
                    : Process.Start("xdg-open", [Path.GetDirectoryName(path) ?? path]);
            return process is not null;
        }
        catch (Exception)
        {
            // No desktop session (a server or container), or no file manager installed.
            return false;
        }
    }
}
