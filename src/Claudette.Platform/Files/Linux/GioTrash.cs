using System.ComponentModel;
using Claudette.Core.Files;
using Claudette.Core.Processes;

namespace Claudette.Platform.Files.Linux;

/// <summary>
/// The desktop's trash on Linux (the freedesktop.org trash, which GNOME, KDE and the file managers share), through
/// <c>gio trash</c>, which comes with GLib's tools (<c>libglib2.0-bin</c> on Debian and Ubuntu, <c>glib2</c> elsewhere).
/// It finds the trash on the file's own drive, as the file managers do. Without <c>gio</c>, or on a drive with no trash,
/// nothing moves.
/// </summary>
internal sealed class GioTrash(IProcessLauncher launcher, TimeProvider time) : ITrash
{
    /// <summary>Within a drive it's a rename, so this is only how long a stuck one takes to give up.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public string Name => "the trash";

    public async Task<TrashResult> MoveToTrashAsync(string path, CancellationToken cancellationToken = default)
    {
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(launcher, new ProcessStartSpec("gio", ["trash", "--", path]), Timeout, time, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return new TrashResult(TrashOutcome.NoTrash, "gio isn't installed: it comes with GLib's tools, libglib2.0-bin on Debian and Ubuntu.");
        }
        if (result.ExitCode == 0)
        {
            return TrashResult.Trashed;
        }
        var said = (result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput).Trim();
        // gio's words where a drive has no trash, or it won't make one there.
        return said.Contains("trash directory", StringComparison.OrdinalIgnoreCase) || said.Contains("not supported", StringComparison.OrdinalIgnoreCase)
            ? new TrashResult(TrashOutcome.NoTrash, said)
            : new TrashResult(TrashOutcome.Failed, said.Length > 0 ? said : $"gio trash ended with {result.ExitCode}.");
    }
}
