using System.Diagnostics;

namespace FolderChat.Web.Infrastructure;

internal static class BrowserLauncher
{
    /// <summary>Opens <paramref name="url"/> in the default browser. Failure is harmless: the URL is also in the log.</summary>
    public static void Open(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No desktop session (a server or container). The user opens the URL themselves.
        }
    }
}
