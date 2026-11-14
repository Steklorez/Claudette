using System.Text;
using Deventry.Core.Processes;
using Deventry.Core.TimedMessages;
using Deventry.Platform.Power;
using Deventry.Platform.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Platform.Tests.Power;

/// <summary>
/// The wake helper on macOS and Linux (DESIGN.md §18, "Timed messages", "Waking the computer"). What Deventry runs goes
/// through a fake launcher; never sudo, pkexec, pmset or rtcwake. The helper script's checks run for real on macOS and
/// Linux, with words it turns away before it would touch the computer's wake.
/// </summary>
public class WakeHelperTests
{
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-14T12:00:00Z"));
    private readonly ExitingLauncher _launcher = new();
    private readonly HashSet<string> _files = ["/usr/bin/pkexec", "/usr/sbin/rtcwake"];

    private HelperWakeTimer Mac(string user = "kim") => new(WakeHelperSystem.Mac, _launcher, _time, _files.Contains, user);

    private HelperWakeTimer Linux() => new(WakeHelperSystem.Linux, _launcher, _time, _files.Contains);

    [Fact]
    public void It_needs_the_helper_until_the_helper_and_its_rule_are_both_there()
    {
        var mac = Mac();
        Assert.Equal(WakeSetup.NeedsHelper, mac.Setup);
        _files.Add(mac.HelperPath);
        Assert.Equal(WakeSetup.NeedsHelper, mac.Setup);
        _files.Add(mac.RulePath);
        Assert.Equal(WakeSetup.HelperInstalled, mac.Setup);
    }

    [Fact]
    public void Linux_without_pkexec_or_rtcwake_cant_be_woken()
    {
        _files.Remove("/usr/bin/pkexec");
        Assert.Equal(WakeSetup.Unavailable, Linux().Setup);
        Assert.Contains("pkexec", Linux().UnavailableReason, StringComparison.Ordinal);
        _files.Add("/usr/bin/pkexec");
        _files.Remove("/usr/sbin/rtcwake");
        Assert.Contains("rtcwake", Linux().UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_macOS_a_wake_is_set_through_sudo_and_cleared_for_a_time_gone_by()
    {
        var mac = Mac();
        _files.UnionWith([mac.HelperPath, mac.RulePath]);
        var at = _time.GetUtcNow().AddHours(19);

        await mac.WakeAtAsync(at, TestContext.Current.CancellationToken);

        var spec = Assert.Single(_launcher.Specs);
        Assert.Equal("/usr/bin/sudo", spec.FileName);
        Assert.Equal(["-n", mac.HelperPath, "set", at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)], spec.Arguments);
        Assert.Equal(at, mac.WakesAt);

        await mac.WakeAtAsync(_time.GetUtcNow().AddMinutes(-1), TestContext.Current.CancellationToken);
        Assert.Equal(["-n", mac.HelperPath, "clear"], _launcher.Specs[^1].Arguments);
        Assert.Null(mac.WakesAt);
    }

    [Fact]
    public async Task On_Linux_a_wake_is_set_through_pkexec()
    {
        var linux = Linux();
        _files.UnionWith([linux.HelperPath, linux.RulePath]);
        var at = _time.GetUtcNow().AddHours(1);

        await linux.WakeAtAsync(at, TestContext.Current.CancellationToken);

        var spec = Assert.Single(_launcher.Specs);
        Assert.Equal("/usr/bin/pkexec", spec.FileName);
        Assert.Equal([linux.HelperPath, "set", at.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)], spec.Arguments);
    }

    [Fact]
    public async Task A_helper_that_fails_says_so_and_sets_no_wake()
    {
        var mac = Mac();
        _files.UnionWith([mac.HelperPath, mac.RulePath]);
        _launcher.ExitCode = 1;
        _launcher.Error = "sudo: a password is required";

        await mac.WakeAtAsync(_time.GetUtcNow().AddHours(1), TestContext.Current.CancellationToken);

        Assert.Null(mac.WakesAt);
        Assert.Contains("sudo: a password is required", mac.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_the_helper_nothing_runs()
    {
        await Mac().WakeAtAsync(_time.GetUtcNow().AddHours(1), TestContext.Current.CancellationToken);

        Assert.Empty(_launcher.Specs);
    }

    [Fact]
    public async Task Installing_asks_for_the_password_once_and_carries_both_files_in_the_command()
    {
        var mac = Mac();

        await mac.InstallAsync(TestContext.Current.CancellationToken);

        // The fake root step installs nothing, so sudo isn't asked after it.
        var spec = Assert.Single(_launcher.Specs);
        Assert.Equal("/usr/bin/osascript", spec.FileName);
        var command = spec.Arguments[1];
        Assert.EndsWith("with administrator privileges", command, StringComparison.Ordinal);
        var script = mac.InstallScript();
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(mac.HelperScript())), script, StringComparison.Ordinal);
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes(mac.RuleText())), script, StringComparison.Ordinal);
        // The sudoers rule is checked before it's put in place.
        Assert.Contains("/usr/sbin/visudo -cf /etc/sudoers.d/deventry-wake.new >/dev/null && mv -f /etc/sudoers.d/deventry-wake.new /etc/sudoers.d/deventry-wake", script, StringComparison.Ordinal);
        // Nothing passes through a file the user could change first.
        Assert.DoesNotContain("/tmp", script, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('/', '\\'), script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_macOS_installing_asks_sudo_whether_the_rule_took()
    {
        var mac = Mac();
        _launcher.OnStart = spec =>
        {
            if (spec.FileName == "/usr/bin/osascript")
            {
                _files.UnionWith([mac.HelperPath, mac.RulePath]);
            }
            else if (spec.Arguments.Contains("-l"))
            {
                _launcher.ExitCode = 1;
            }
        };

        Assert.False(await mac.InstallAsync(TestContext.Current.CancellationToken));

        Assert.Equal(["-n", "-l", mac.HelperPath], _launcher.Specs[^1].Arguments);
        Assert.Contains("sudo won't run it without a password", mac.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_Linux_installing_runs_its_script_through_pkexec()
    {
        var linux = Linux();

        await linux.InstallAsync(TestContext.Current.CancellationToken);

        var spec = Assert.Single(_launcher.Specs);
        Assert.Equal("/usr/bin/pkexec", spec.FileName);
        Assert.Equal(["/bin/sh", "-c", linux.InstallScript()], spec.Arguments);
        Assert.Contains("<annotate key=\"org.freedesktop.policykit.exec.path\">/usr/local/libexec/deventry-wake</annotate>", linux.RuleText(), StringComparison.Ordinal);
        Assert.Contains("<allow_active>yes</allow_active>", linux.RuleText(), StringComparison.Ordinal);
        Assert.Contains("<allow_any>no</allow_any>", linux.RuleText(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_sudoers_rule_names_only_the_user_and_the_helper()
    {
        Assert.Equal("kim ALL=(root) NOPASSWD: /Library/PrivilegedHelperTools/app.deventry.wake", Mac().RuleText().Split('\n')[1]);
    }

    [Theory]
    [InlineData("kim", true)]
    [InlineData("kim.lee", true)]
    [InlineData("_svc-1", true)]
    [InlineData("kim ALL=(ALL) ALL", false)]
    [InlineData("kim\nroot", false)]
    [InlineData("", false)]
    [InlineData("-kim", false)]
    public void Only_a_plain_user_name_goes_in_a_sudoers_rule(string user, bool safe)
    {
        Assert.Equal(safe, HelperWakeTimer.IsSafeUser(user));
        Assert.Equal(safe ? WakeSetup.NeedsHelper : WakeSetup.Unavailable, Mac(user).Setup);
    }

    [Theory]
    [InlineData(WakeHelperSystem.Mac)]
    [InlineData(WakeHelperSystem.Linux)]
    public async Task The_helper_turns_away_anything_but_set_with_a_time_to_come_or_clear(WakeHelperSystem system)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The helper is a shell script for macOS and Linux.");
        var script = Path.Combine(Path.GetTempPath(), $"deventry-wake-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(script, new HelperWakeTimer(system, _launcher, _time, _files.Contains, "kim").HelperScript(), TestContext.Current.CancellationToken);
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            async Task<int> Run(params string[] words) =>
                (await ProcessRunner.RunAsync(new ProcessLauncher(), new ProcessStartSpec("/bin/sh", [script, .. words]), TimeSpan.FromMinutes(1), TimeProvider.System,
                    TestContext.Current.CancellationToken)).ExitCode;

            Assert.Equal(64, await Run());
            Assert.Equal(64, await Run("wake"));
            Assert.Equal(64, await Run("set"));
            Assert.Equal(64, await Run("set", "soon"));
            Assert.Equal(64, await Run("set", "-5"));
            Assert.Equal(64, await Run("set", "1", "2"));
            Assert.Equal(64, await Run("clear", "now"));
            Assert.Equal(65, await Run("set", "1"));
            Assert.Equal(65, await Run("set", (now + 40L * 24 * 3600).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.Equal(65, await Run("set", "999999999999999999999"));
        }
        finally
        {
            File.Delete(script);
        }
    }

    /// <summary>Processes that end at once, with the code and error output a test gives them.</summary>
    private sealed class ExitingLauncher : IProcessLauncher
    {
        private readonly Lock _lock = new();
        private readonly List<ProcessStartSpec> _specs = [];
        private int _nextId = 7000;

        public int ExitCode { get; set; }

        public string? Error { get; set; }

        /// <summary>Runs as each process starts, before it ends: what it did on the computer.</summary>
        public Action<ProcessStartSpec>? OnStart { get; set; }

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
            OnStart?.Invoke(spec);
            var process = new FakeRunningProcess(Interlocked.Increment(ref _nextId));
            if (Error is { } error)
            {
                process.WriteError(error);
            }
            process.Exit(ExitCode);
            return process;
        }
    }
}
