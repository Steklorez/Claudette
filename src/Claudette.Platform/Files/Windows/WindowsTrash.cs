using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;
using Claudette.Core.Files;
using Claudette.Platform.Notifications.Windows;

namespace Claudette.Platform.Files.Windows;

/// <summary>
/// Windows' Recycle Bin, through the shell's <c>IFileOperation</c>, as Explorer's Delete does. Where an item can't be
/// recycled (a network share, a drive with no Recycle Bin, one too large for it), the shell would delete it for good
/// instead: the progress sink stops it first, so it stays, and the result says there's no trash. Should the shell still
/// come to that, it asks itself (<c>FOF_WANTNUKEWARNING</c>). It runs on a thread of its own, since the shell's file
/// operations want a single-threaded apartment.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsTrash : ITrash
{
    private static readonly Guid FileOperationClass = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    private static readonly Guid ShellItemInterface = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    private const uint FofSilent = 0x4;
    private const uint FofNoConfirmation = 0x10;
    private const uint FofAllowUndo = 0x40;
    private const uint FofNoErrorUi = 0x400;
    private const uint FofWantNukeWarning = 0x4000;
    private const uint FofxRecycleOnDelete = 0x80000;
    private const uint FofxEarlyFailure = 0x100000;

    public string Name => "the Recycle Bin";

    public Task<TrashResult> MoveToTrashAsync(string path, CancellationToken cancellationToken = default)
    {
        var done = new TaskCompletionSource<TrashResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.TrySetResult(Recycle(path));
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException or UnauthorizedAccessException or IOException)
            {
                done.TrySetResult(new TrashResult(TrashOutcome.Failed, ex.Message));
            }
        })
        {
            IsBackground = true,
            Name = "Claudette's Recycle Bin",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task.WaitAsync(cancellationToken);
    }

    private static TrashResult Recycle(string path)
    {
        Marshal.ThrowExceptionForHR(FileOperationInterop.SHCreateItemFromParsingName(path, 0, ShellItemInterface, out var item));
        try
        {
            var operation = WinRt.CreateComInstance<IFileOperation>(FileOperationClass);
            operation.SetOperationFlags(FofAllowUndo | FofxRecycleOnDelete | FofNoConfirmation | FofNoErrorUi | FofSilent | FofWantNukeWarning | FofxEarlyFailure);
            var sink = new RecycleOnlySink();
            operation.Advise(sink, out var cookie);
            try
            {
                operation.DeleteItem(item, 0);
                var performed = operation.PerformOperations();
                operation.GetAnyOperationsAborted(out var aborted);
                if (sink.WouldDeleteForGood || (aborted != 0 && sink.ItemError is null))
                {
                    return new TrashResult(TrashOutcome.NoTrash, "Windows can't put it in the Recycle Bin.");
                }
                if (sink.ItemError is { } failed || performed < 0)
                {
                    return new TrashResult(TrashOutcome.Failed, Marshal.GetExceptionForHR(sink.ItemError ?? performed)?.Message);
                }
                return TrashResult.Trashed;
            }
            finally
            {
                operation.Unadvise(cookie);
            }
        }
        finally
        {
            Marshal.Release(item);
        }
    }
}

/// <summary>
/// Stops a delete the shell would do for good rather than by recycling: before each item, the shell says whether it's
/// recycling it (<c>TSF_DELETE_RECYCLE_IF_POSSIBLE</c>), and a failure from here cancels it.
/// </summary>
[GeneratedComClass]
internal sealed partial class RecycleOnlySink : IFileOperationProgressSink
{
    private const uint RecycleIfPossible = 0x80;

    /// <summary><c>COPYENGINE_E_USER_CANCELLED</c>.</summary>
    private const int Cancelled = unchecked((int)0x80270000);

    /// <summary>The shell was going to delete it for good, and was stopped.</summary>
    public bool WouldDeleteForGood { get; private set; }

    /// <summary>Why the item couldn't be deleted, or null.</summary>
    public int? ItemError { get; private set; }

    public int PreDeleteItem(uint flags, nint item)
    {
        if ((flags & RecycleIfPossible) == 0)
        {
            WouldDeleteForGood = true;
            return Cancelled;
        }
        return 0;
    }

    public int PostDeleteItem(uint flags, nint item, int result, nint newlyCreated)
    {
        if (result < 0 && result != Cancelled)
        {
            ItemError = result;
        }
        return 0;
    }

    public int StartOperations() => 0;

    public int FinishOperations(int result) => 0;

    public int PreRenameItem(uint flags, nint item, nint newName) => 0;

    public int PostRenameItem(uint flags, nint item, nint newName, int result, nint newlyCreated) => 0;

    public int PreMoveItem(uint flags, nint item, nint destination, nint newName) => 0;

    public int PostMoveItem(uint flags, nint item, nint destination, nint newName, int result, nint newlyCreated) => 0;

    public int PreCopyItem(uint flags, nint item, nint destination, nint newName) => 0;

    public int PostCopyItem(uint flags, nint item, nint destination, nint newName, int result, nint newlyCreated) => 0;

    public int PreNewItem(uint flags, nint destination, nint newName) => 0;

    public int PostNewItem(uint flags, nint destination, nint newName, nint templateName, uint attributes, int result, nint newItem) => 0;

    public int UpdateProgress(uint workTotal, uint workSoFar) => 0;

    public int ResetTimer() => 0;

    public int PauseTimer() => 0;

    public int ResumeTimer() => 0;
}

internal static partial class FileOperationInterop
{
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemFromParsingName(string path, nint bindContext, in Guid riid, out nint item);
}

// IFileOperation and its progress sink, declared by hand in vtable order from shobjidl_core.h, as WinRtInterop's are.
// Shell items and strings Claudette doesn't read pass as pointers.

[GeneratedComInterface]
[Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
internal partial interface IFileOperation
{
    void Advise(IFileOperationProgressSink sink, out uint cookie);

    void Unadvise(uint cookie);

    void SetOperationFlags(uint flags);

    void SetProgressMessage(nint message);

    void SetProgressDialog(nint dialog);

    void SetProperties(nint properties);

    void SetOwnerWindow(nint owner);

    void ApplyPropertiesToItem(nint item);

    void ApplyPropertiesToItems(nint items);

    void RenameItem(nint item, nint newName, nint sink);

    void RenameItems(nint items, nint newName);

    void MoveItem(nint item, nint destination, nint newName, nint sink);

    void MoveItems(nint items, nint destination);

    void CopyItem(nint item, nint destination, nint copyName, nint sink);

    void CopyItems(nint items, nint destination);

    void DeleteItem(nint item, nint sink);

    void DeleteItems(nint items);

    void NewItem(nint destination, uint attributes, nint name, nint templateName, nint sink);

    /// <summary>Its HRESULT: a cancelled item makes it fail, which isn't an error here.</summary>
    [PreserveSig]
    int PerformOperations();

    void GetAnyOperationsAborted(out int aborted);
}

[GeneratedComInterface]
[Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2")]
internal partial interface IFileOperationProgressSink
{
    [PreserveSig]
    int StartOperations();

    [PreserveSig]
    int FinishOperations(int result);

    [PreserveSig]
    int PreRenameItem(uint flags, nint item, nint newName);

    [PreserveSig]
    int PostRenameItem(uint flags, nint item, nint newName, int result, nint newlyCreated);

    [PreserveSig]
    int PreMoveItem(uint flags, nint item, nint destination, nint newName);

    [PreserveSig]
    int PostMoveItem(uint flags, nint item, nint destination, nint newName, int result, nint newlyCreated);

    [PreserveSig]
    int PreCopyItem(uint flags, nint item, nint destination, nint newName);

    [PreserveSig]
    int PostCopyItem(uint flags, nint item, nint destination, nint newName, int result, nint newlyCreated);

    [PreserveSig]
    int PreDeleteItem(uint flags, nint item);

    [PreserveSig]
    int PostDeleteItem(uint flags, nint item, int result, nint newlyCreated);

    [PreserveSig]
    int PreNewItem(uint flags, nint destination, nint newName);

    [PreserveSig]
    int PostNewItem(uint flags, nint destination, nint newName, nint templateName, uint attributes, int result, nint newItem);

    [PreserveSig]
    int UpdateProgress(uint workTotal, uint workSoFar);

    [PreserveSig]
    int ResetTimer();

    [PreserveSig]
    int PauseTimer();

    [PreserveSig]
    int ResumeTimer();
}
