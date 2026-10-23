using System.Runtime.InteropServices;
using Claudette.App.Services;
using Claudette.App.ViewModels;
using Claudette.Core;
using Claudette.Core.Development;
using Claudette.Core.Git;
using Claudette.Core.Processes;

namespace Claudette.App.Tests.Support;

/// <summary>The window's side of a restart, over the harness's shell.</summary>
internal sealed class ShellRestartHost(ShellViewModel shell) : IRestartHost
{
    public bool Exited { get; private set; }

    public bool AnyTabWorking => shell.AnyTabWorking;

    public event Action? TabsChanged
    {
        add => shell.TabStatusChanged += value;
        remove => shell.TabStatusChanged -= value;
    }

    public RestartSnapshot Capture() => shell.CaptureForRestart();

    public Task CloseTabsAsync(string message) => shell.CloseTabsForRestartAsync();

    public void Recover(RestartSnapshot snapshot) => shell.Restore(null, snapshot);

    public void Exit() => Exited = true;
}

/// <summary>A source build's new commits upstream, set up for a test: the service, its badge, the restarts and the checkout.</summary>
internal sealed record SourceUpdateSetup(SourceUpdateService Updates, SourceUpdateViewModel Badge, RestartService Restarts, ShellRestartHost Host, FakeCheckout Checkout)
    : IDisposable
{
    public void Dispose()
    {
        Badge.Dispose();
        Updates.Dispose();
        Restarts.Dispose();
    }
}

/// <summary>
/// A source build's checkout as git and <c>dotnet</c> tell it (DESIGN.md §9, "New commits upstream"), played through
/// <see cref="FakeLauncher"/>: what each command answers, and what the test sees run. A new build that's started says
/// it's up.
/// </summary>
internal sealed class FakeCheckout
{
    public static readonly DateTime Running = new(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Rebuilt = Running.AddMinutes(5);

    public int Behind { get; set; } = 3;

    public int Ahead { get; set; }

    public bool HasChanges { get; set; }

    public string UpstreamHead { get; set; } = "c3c3c3c3c3c3c3c3";

    public bool BuildSucceeds { get; set; } = true;

    public required string Output { get; init; }

    public required AppPaths Paths { get; init; }

    /// <summary>The git and dotnet command lines run, without git's own options.</summary>
    public List<string> Commands { get; } = [];

    /// <summary>
    /// A source build in a checkout under the harness's folder, running from a copy (or in place), with the checkout's
    /// answers, checked once.
    /// </summary>
    public static async Task<SourceUpdateSetup> StartAsync(TabTestHarness h, FakeLauncher launcher, bool inPlace = false, Action<FakeCheckout>? configure = null)
    {
        var output = MakeBuild(h.Root);
        var checkout = new FakeCheckout { Output = output, Paths = h.Services.Paths };
        configure?.Invoke(checkout);
        launcher.OnStart = checkout.Answer;
        var host = new ShellRestartHost(h.Shell);
        var restarts = new RestartService(h.Services, new DevelopmentBuild(output, inPlace ? output : Path.Combine(h.Root, "running"), Running), host);
        restarts.Start();
        var updates = new SourceUpdateService(h.Services, restarts, new SourceCheckout(new GitWorkingTree(launcher, h.Time), launcher, h.Time));
        var badge = new SourceUpdateViewModel(updates, h.Time);
        await updates.CheckNowAsync();
        return new SourceUpdateSetup(updates, badge, restarts, host, checkout);
    }

    public void Answer(ProcessStartSpec spec, FakeProcess process)
    {
        var arguments = spec.Arguments.SkipWhile(a => a is "-c" or "core.quotepath=false").ToArray();
        if (arguments.Contains(LaunchArguments.RestoreOption))
        {
            // The new build, saying it's up.
            RestartHandshake.SignalReady(Paths.RestartReadyFile, spec.Arguments[^1]);
            return;
        }
        lock (Commands)
        {
            Commands.Add(string.Join(' ', arguments));
        }
        var (code, output) = arguments switch
        {
            ["symbolic-ref", ..] => (0, "main"),
            ["rev-parse", "--abbrev-ref", ..] => (0, "origin/main"),
            ["fetch", ..] => (0, ""),
            ["rev-parse", "@{upstream}"] => (0, UpstreamHead),
            ["rev-list", ..] => (0, $"{Ahead}\t{Behind}"),
            ["--no-optional-locks", "status", ..] => (0, HasChanges ? " M README.md\0" : ""),
            ["log", ..] => (0, Log()),
            ["merge", ..] => Merge(),
            ["build", ..] => Build(),
            _ => (1, ""),
        };
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            process.WriteOutput(line);
        }
        process.Exit(code);
    }

    private string Log() => string.Join('\n', Enumerable.Range(1, Math.Min(Behind, SourceCheckout.MaxCommits)).Reverse()
        .Select(i => $"{i}{i}{i}{i}{i}{i}{i}{i}\u001fCommit {i}\u001fAda\u001f2026-10-22T09:0{i % 10}:00Z"));

    private (int, string) Merge()
    {
        Behind = 0;
        return (0, "");
    }

    private (int, string) Build()
    {
        if (!BuildSucceeds)
        {
            return (1, "Restore complete\nerror CS1002: ; expected");
        }
        Touch(Output, Rebuilt);
        return (0, "Restore complete\nClaudette -> " + Output);
    }

    /// <summary>A build output in a checkout, with this platform's native libraries.</summary>
    private static string MakeBuild(string root)
    {
        var repo = Path.Combine(root, "repo");
        var output = Path.Combine(repo, "src", "Claudette.App", "bin", "Debug", "net10.0");
        Directory.CreateDirectory(Path.Combine(output, "runtimes", RuntimeInformation.RuntimeIdentifier, "native"));
        File.WriteAllText(Path.Combine(repo, SourceBuild.SolutionFileName), "<Solution />");
        File.WriteAllText(Path.Combine(output, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", "native.lib"), "here");
        foreach (var name in new[] { "Claudette", "Claudette.exe", "Claudette.dll", "Claudette.deps.json", "Claudette.runtimeconfig.json" })
        {
            File.WriteAllText(Path.Combine(output, name), name);
        }
        Touch(output, Running);
        return output;
    }

    private static void Touch(string output, DateTime stamp)
    {
        foreach (var file in Directory.EnumerateFiles(output))
        {
            File.SetLastWriteTimeUtc(file, stamp);
        }
    }
}
