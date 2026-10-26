using System.Runtime.Versioning;
using Claudette.Core.Files;
using Claudette.Platform.Notifications.Mac;

namespace Claudette.Platform.Files.Mac;

/// <summary>
/// The macOS Trash, through <c>-[NSFileManager trashItemAtURL:resultingItemURL:error:]</c>, which Finder's Move to Trash
/// uses too: a file on a volume with no trash (a network share, say) stays, with <c>NSFeatureUnsupportedError</c>.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacTrash : ITrash
{
    /// <summary><c>NSFeatureUnsupportedError</c>: the volume has no trash.</summary>
    private const nint FeatureUnsupported = 3328;

    public string Name => "the Trash";

    public Task<TrashResult> MoveToTrashAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => MoveToTrash(path), cancellationToken);

    private static TrashResult MoveToTrash(string path)
    {
        var pool = ObjC.AutoreleasePoolPush();
        var name = ObjC.NSString(path);
        try
        {
            var manager = ObjC.Send(ObjC.GetClass("NSFileManager"), "defaultManager");
            var url = ObjC.Send(ObjC.GetClass("NSURL"), "fileURLWithPath:", name);
            nint error = 0;
            // A BOOL comes back in the low byte.
            if ((ObjC.Send(manager, "trashItemAtURL:resultingItemURL:error:", url, 0, (nint)(&error)) & 0xFF) != 0)
            {
                return TrashResult.Trashed;
            }
            var reason = error == 0 ? null : ObjC.ToManagedString(ObjC.Send(error, "localizedDescription"));
            return error != 0 && ObjC.Send(error, "code") == FeatureUnsupported
                ? new TrashResult(TrashOutcome.NoTrash, reason)
                : new TrashResult(TrashOutcome.Failed, reason);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return new TrashResult(TrashOutcome.NoTrash, ex.Message);
        }
        finally
        {
            ObjC.Release(name);
            ObjC.AutoreleasePoolPop(pool);
        }
    }
}
