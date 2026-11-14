using Deventry.Core.Processes;

namespace Deventry.IntegrationTests;

/// <summary>A real process's standard input, through <see cref="ProcessLauncher"/>: what closing a tab's claude does to it.</summary>
public sealed class ProcessLauncherTests
{
    /// <summary>A process that never reads its input, and waits a minute.</summary>
    private static ProcessStartSpec NotReading() => OperatingSystem.IsWindows()
        ? new ProcessStartSpec("cmd.exe", ["/c", "ping -n 60 127.0.0.1 >nul"])
        : new ProcessStartSpec("/bin/sh", ["-c", "sleep 60"]);

    [Fact]
    public async Task Closing_the_input_while_a_line_is_being_written_waits_for_the_line_rather_than_throwing()
    {
        await using var process = new ProcessLauncher().Start(NotReading());
        // Far more than the pipe holds: its write waits for a reader that never comes.
        var writing = process.WriteLineAsync(new string('x', 4 * 1024 * 1024), TestContext.Current.CancellationToken).AsTask();
        Assert.False(writing.IsCompleted);

        // As closing a tab does; closing under the write threw, and the close went no further.
        process.CloseStandardInput();

        Assert.False(writing.IsCompleted);
        // Ending the process ends the write, and the input closes after it.
        process.Kill();
        await Assert.ThrowsAnyAsync<IOException>(() => writing.WaitAsync(Waiting.Timeout, TestContext.Current.CancellationToken));
        await process.Exited.WaitAsync(Waiting.Timeout, TestContext.Current.CancellationToken);
    }
}
