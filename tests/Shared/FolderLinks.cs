using System.Diagnostics;

namespace Claudette.Tests;

/// <summary>
/// Links to folders, for paths that reach a folder another way, as macOS's <c>/var</c> reaches <c>/private/var</c>: a
/// symbolic link, or on Windows a junction, which needs no Developer Mode, so the tests run on every OS.
/// </summary>
internal static class FolderLinks
{
    /// <summary>Makes <paramref name="link"/> a link to the folder <paramref name="target"/>, spelled as given. Disposing removes it.</summary>
    public static IDisposable Create(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return new Link(link);
        }
        using var process = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
            { CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException($"mklink /J {link} {target} failed: {output}");
        }
        return new Link(link);
    }

    /// <summary>
    /// Removes the link, not what it links to. Deleting the folder that holds a junction would also unmount it as a
    /// volume first, which Windows refuses without administrator rights.
    /// </summary>
    private sealed class Link(string path) : IDisposable
    {
        public void Dispose()
        {
            if (OperatingSystem.IsWindows())
            {
                Directory.Delete(path);
            }
            else
            {
                File.Delete(path);
            }
        }
    }
}
