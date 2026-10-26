using System.ComponentModel;
using Claudette.Core.Files;
using Claudette.Core.Processes;
using Claudette.Platform.Files;
using Claudette.Platform.Files.Linux;
using Claudette.Platform.Tests.Support;

namespace Claudette.Platform.Tests.Files;

/// <summary>
/// The OS's trash, for deleting from the Project Files tab's explorer (DESIGN.md §18, "Project files", "Renaming and
/// deleting"). <c>gio</c> is faked; the Recycle Bin and the macOS Trash are used for real only with
/// <c>CLAUDETTE_TEST_TRASH=1</c>, since what they're given lands in the user's own trash.
/// </summary>
public class TrashTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly FakeLauncher _launcher = new();

    [Fact]
    public void Every_OS_has_a_trash_named_as_a_sentence_says_it()
    {
        var trash = Trash.CreateForCurrentOS(_launcher, TimeProvider.System);

        Assert.Equal(OperatingSystem.IsWindows() ? "the Recycle Bin" : OperatingSystem.IsMacOS() ? "the Trash" : "the trash", trash.Name);
    }

    [Fact]
    public async Task Gio_trash_moves_it_and_says_so()
    {
        var trashing = new GioTrash(_launcher, TimeProvider.System).MoveToTrashAsync("/home/matt/NightOwl/Brain.cpp", Token);
        var process = await Started(0);
        process.Exit(0);

        Assert.Equal(TrashResult.Trashed, await trashing);
        Assert.Equal("gio", _launcher.Specs[0].FileName);
        // The path after --, so a name starting with a dash isn't taken for an option.
        Assert.Equal(["trash", "--", "/home/matt/NightOwl/Brain.cpp"], _launcher.Specs[0].Arguments);
    }

    [Fact]
    public async Task A_drive_gio_has_no_trash_on_is_no_trash_and_another_failure_says_why()
    {
        var gio = new GioTrash(_launcher, TimeProvider.System);

        var noTrash = gio.MoveToTrashAsync("/mnt/share/Brain.cpp", Token);
        var first = await Started(0);
        first.WriteError("gio: file:///mnt/share/Brain.cpp: Unable to find or create trash directory for file:///mnt/share/Brain.cpp");
        first.Exit(2);

        Assert.Equal(TrashOutcome.NoTrash, (await noTrash).Outcome);

        var failed = gio.MoveToTrashAsync("/home/matt/locked", Token);
        var second = await Started(1);
        second.WriteError("gio: file:///home/matt/locked: Error trashing file: Permission denied");
        second.Exit(2);

        Assert.Equal(new TrashResult(TrashOutcome.Failed, "gio: file:///home/matt/locked: Error trashing file: Permission denied"), await failed);
    }

    [Fact]
    public async Task Without_gio_there_is_no_trash()
    {
        var result = await new GioTrash(new MissingLauncher(), TimeProvider.System).MoveToTrashAsync("/home/matt/Brain.cpp", Token);

        Assert.Equal(TrashOutcome.NoTrash, result.Outcome);
        Assert.Contains("gio isn't installed", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_OSs_own_trash_takes_a_file_and_a_folder()
    {
        Assert.SkipUnless((OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) && Environment.GetEnvironmentVariable("CLAUDETTE_TEST_TRASH") == "1",
            "Windows or macOS with CLAUDETTE_TEST_TRASH=1 only: it puts two small things in your trash.");
        var folder = Directory.CreateTempSubdirectory("claudette-trash-").FullName;
        try
        {
            var file = Path.Combine(folder, "Claudette trash test.txt");
            File.WriteAllText(file, "Claudette's trash test: delete me.");
            var inside = Path.Combine(folder, "Claudette trash test folder");
            Directory.CreateDirectory(inside);
            File.WriteAllText(Path.Combine(inside, "a.txt"), "a");
            var trash = Trash.CreateForCurrentOS(_launcher, TimeProvider.System);

            Assert.Equal(TrashResult.Trashed, await trash.MoveToTrashAsync(file, Token));
            Assert.Equal(TrashResult.Trashed, await trash.MoveToTrashAsync(inside, Token));

            Assert.False(File.Exists(file));
            Assert.False(Directory.Exists(inside));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private async Task<FakeRunningProcess> Started(int index)
    {
        await Waiting.UntilAsync(() => _launcher.Started.Count > index && _launcher.Started[index].InputClosed, $"process {index} to have its input");
        return _launcher.Started[index];
    }

    private sealed class MissingLauncher : IProcessLauncher
    {
        public IRunningProcess Start(ProcessStartSpec spec) => throw new Win32Exception(2, "No such file or directory");
    }
}
