using System.Diagnostics;
using System.Net;
using FolderChat.Application.Common;

namespace FolderChat.Web.Infrastructure;

/// <summary>
/// Opens the computer's own window for choosing a folder, so the user can browse to one instead of pasting its path.
/// The browser can't do this itself: web pages are never told where a folder is.
/// </summary>
public interface IFolderPicker
{
    /// <summary>False on a computer without a desktop to show the window on, such as a server.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Opens the window and waits for the user. Null when they cancel, or when <paramref name="ct"/> is cancelled,
    /// which closes the window.
    /// </summary>
    Task<string?> ChooseAsync(CancellationToken ct);
}

public static class FolderPickerExtensions
{
    /// <summary>
    /// Whether the window can open for this request. It opens on the computer running FolderChat, so only a browser on
    /// that computer is offered it. A request without an address comes from inside the process, as in tests.
    /// </summary>
    public static bool CanOpenFor(this IFolderPicker picker, HttpContext context) =>
        picker.IsAvailable && (context.Connection.RemoteIpAddress is not { } address || IPAddress.IsLoopback(address));
}

/// <summary>File Explorer's folder window on Windows, Finder's on a Mac, and zenity's or kdialog's on Linux.</summary>
internal sealed class SystemFolderPicker(ILogger<SystemFolderPicker> logger) : IFolderPicker
{
    private const string Title = "Choose a folder for FolderChat";

    private readonly SemaphoreSlim oneAtATime = new(1, 1);
    private readonly string? linuxChooser = OperatingSystem.IsLinux() ? FindLinuxChooser() : null;

    public bool IsAvailable =>
        OperatingSystem.IsWindows() ? Environment.UserInteractive : OperatingSystem.IsMacOS() || linuxChooser is not null;

    public async Task<string?> ChooseAsync(CancellationToken ct)
    {
        if (!await oneAtATime.WaitAsync(0, ct))
        {
            throw new ValidationFailedException("A window for choosing a folder is already open. Choose the folder there, or close it.");
        }

        try
        {
            if (OperatingSystem.IsWindows())
            {
                return await WindowsFolderDialog.ShowAsync(Title, ct);
            }

            return OperatingSystem.IsMacOS()
                ? await RunAsync("osascript", ["-e", "activate", "-e", $"POSIX path of (choose folder with prompt \"{Title}\")"], ct)
                : Path.GetFileName(linuxChooser) == "kdialog"
                    ? await RunAsync(linuxChooser!, ["--getexistingdirectory", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "--title", Title], ct)
                    : await RunAsync(linuxChooser!, ["--file-selection", "--directory", $"--title={Title}"], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No desktop after all, a missing program, or Windows refusing the dialog: the path can still be pasted.
            logger.LogWarning(ex, "The window for choosing a folder couldn't open");
            throw new ValidationFailedException("FolderChat couldn't open a window for choosing a folder. Paste the folder's path instead.");
        }
        finally
        {
            oneAtATime.Release();
        }
    }

    /// <summary>Runs a program that shows the window and prints the chosen folder's path.</summary>
    private static async Task<string?> RunAsync(string program, IEnumerable<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} didn't start.");
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true); // closes the window
            return null;
        }

        var path = (await output).Trim();
        if (process.ExitCode == 0 && path.Length > 0)
        {
            return path;
        }

        // Cancelling makes osascript report error -128, and zenity and kdialog exit with 1.
        var error = await errors;
        if (OperatingSystem.IsMacOS() ? error.Contains("(-128)", StringComparison.Ordinal) : process.ExitCode == 1)
        {
            return null;
        }

        throw new InvalidOperationException($"{Path.GetFileName(program)} exited with {process.ExitCode}: {error.Trim()}");
    }

    /// <summary>zenity (GNOME and most desktops) or kdialog (KDE), when there's a display to show it on.</summary>
    private static string? FindLinuxChooser()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return null;
        }

        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return new[] { "zenity", "kdialog" }
            .SelectMany(name => folders.Select(folder => Path.Join(folder, name)))
            .FirstOrDefault(File.Exists);
    }
}
