using System.Text;

namespace FolderChat.Tests.Fakes;

/// <summary>A real folder on disk for a test to fill with files. Deleted when the test ends.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder() => Directory.CreateDirectory(Path);

    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "folderchat-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Writes a file, creating its folders. Paths use forward slashes.</summary>
    public string Write(string relativePath, string text) => Write(relativePath, Encoding.UTF8.GetBytes(text));

    public string Write(string relativePath, byte[] content)
    {
        var fullPath = FullPath(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, content);
        return fullPath;
    }

    public void Delete(string relativePath) => File.Delete(FullPath(relativePath));

    public string FullPath(string relativePath) =>
        System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file may still be open; the OS cleans up its temp folder eventually.
        }
    }
}
