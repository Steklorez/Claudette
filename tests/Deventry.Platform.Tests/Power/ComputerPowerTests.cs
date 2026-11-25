using Deventry.Core.Power;
using Deventry.Core.Processes;
using Deventry.Platform.Power;
using Deventry.Platform.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Platform.Tests.Power;

/// <summary>
/// Shutting the computer down and putting it to sleep once Claude is done (DESIGN.md §9, "When Claude is done"). What
/// Deventry runs goes through a fake launcher, and Windows' sleep is a stand-in: nothing here ever shuts a computer down
/// or puts it to sleep.
/// </summary>
public class ComputerPowerTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-22T12:00:00Z"));
    private readonly AnsweringLauncher _launcher = new();
    private readonly HashSet<string> _files = ["/usr/bin/systemctl", "/usr/bin/gdbus"];
    private int _slept;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CommandComputerPower Power(PowerSystem system) =>
        new(system, _launcher, _time, _files.Contains, @"C:\Windows\System32", () =>
        {
            _slept++;
            return null;
        });

    [Theory]
    [InlineData("Windows", WhenDone.ShutDown, "shutdown.exe", "/s /t 0")]
    [InlineData("Mac", WhenDone.ShutDown, "/usr/bin/osascript", "-e " + CommandComputerPower.MacShutDown)]
    [InlineData("Mac", WhenDone.Sleep, "/usr/bin/pmset", "sleepnow")]
    [InlineData("Linux", WhenDone.ShutDown, "/usr/bin/systemctl", "poweroff")]
    [InlineData("Linux", WhenDone.Sleep, "/usr/bin/systemctl", "suspend")]
    public async Task Each_system_runs_its_own_command_and_forces_nothing(string system, WhenDone action, string file, string arguments)
    {
        Assert.Null(await Power(Enum.Parse<PowerSystem>(system)).RunAsync(action, Ct));

        var spec = Assert.Single(_launcher.Specs);
        Assert.EndsWith(file, spec.FileName, StringComparison.Ordinal);
        Assert.Equal(arguments, string.Join(' ', spec.Arguments));
        Assert.DoesNotContain("/f", spec.Arguments);
        Assert.DoesNotContain("--force", spec.Arguments);
    }

    [Fact]
    public async Task Windows_sleeps_through_its_own_call_rather_than_a_command()
    {
        Assert.Null(await Power(PowerSystem.Windows).RunAsync(WhenDone.Sleep, Ct));

        Assert.Equal(1, _slept);
        Assert.Empty(_launcher.Specs);
    }

    [Fact]
    public async Task A_command_that_fails_says_what_it_said()
    {
        _launcher.ExitCode = 1;
        _launcher.Error = "Failed to power off system via logind: Access denied";

        var error = await Power(PowerSystem.Linux).RunAsync(WhenDone.ShutDown, Ct);

        Assert.Equal("systemctl didn't (exit code 1): Failed to power off system via logind: Access denied", error);
    }

    [Fact]
    public async Task Linux_without_systemctl_can_do_neither()
    {
        _files.Remove("/usr/bin/systemctl");
        var linux = Power(PowerSystem.Linux);

        Assert.Contains("systemctl wasn't found", linux.UnavailableReason(WhenDone.ShutDown), StringComparison.Ordinal);
        Assert.NotNull(linux.UnavailableReason(WhenDone.Sleep));
        Assert.NotNull(await linux.RunAsync(WhenDone.Sleep, Ct));
        Assert.Empty(_launcher.Specs);
    }

    [Theory]
    [InlineData("('yes',)", null)]
    [InlineData("('challenge',)", "Linux asks for a password before this computer can shut down, which nobody may be there to type.")]
    [InlineData("('no',)", "Linux doesn't let this session shut down.")]
    public async Task Linux_asks_logind_when_it_is_turned_on_whether_it_would_need_a_password(string reply, string? reason)
    {
        _launcher.Output = reply;

        Assert.Equal(reason, await Power(PowerSystem.Linux).PrepareAsync(WhenDone.ShutDown, Ct));
        Assert.Contains("org.freedesktop.login1.Manager.CanPowerOff", Assert.Single(_launcher.Specs).Arguments);
    }

    [Fact]
    public async Task On_macOS_turning_shut_down_on_asks_for_System_Events_while_the_user_is_there()
    {
        var mac = Power(PowerSystem.Mac);
        Assert.Null(await mac.PrepareAsync(WhenDone.ShutDown, Ct));
        Assert.Equal(["-e", CommandComputerPower.MacAskPermission], Assert.Single(_launcher.Specs).Arguments);

        _launcher.ExitCode = 1;
        _launcher.Error = "execution error: Not authorized to send Apple events to System Events. (-1743)";
        Assert.Contains("Privacy & Security → Automation", await mac.PrepareAsync(WhenDone.ShutDown, Ct), StringComparison.Ordinal);

        // Sleep needs nothing asked.
        Assert.Null(await mac.PrepareAsync(WhenDone.Sleep, Ct));
        Assert.Equal(2, _launcher.Specs.Count);
    }

    /// <summary>Answers each command at once, with the output and exit code set.</summary>
    private sealed class AnsweringLauncher : IProcessLauncher
    {
        private readonly Lock _lock = new();
        private readonly List<ProcessStartSpec> _specs = [];
        private int _nextId = 8000;

        public int ExitCode { get; set; }

        public string? Output { get; set; }

        public string? Error { get; set; }

        public IReadOnlyList<ProcessStartSpec> Specs
        {
            get
            {
                lock (_lock)
                {
                    return [.. _specs];
                }
            }
        }

        public IRunningProcess Start(ProcessStartSpec spec)
        {
            lock (_lock)
            {
                _specs.Add(spec);
            }
            var process = new FakeRunningProcess(Interlocked.Increment(ref _nextId));
            if (Output is { } output)
            {
                process.WriteOutput(output);
            }
            if (Error is { } error)
            {
                process.WriteError(error);
            }
            process.Exit(ExitCode);
            return process;
        }
    }
}
