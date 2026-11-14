using System.ComponentModel;
using Deventry.Core.Git;
using Deventry.Core.Processes;
using Deventry.Core.Tests.Support;
using Microsoft.Extensions.Logging;

namespace Deventry.Core.Tests.Git;

/// <summary>Why a git command failed, in the log: its callers see only null or empty, and the log is where it's said.</summary>
public sealed class GitWorkingTreeLogTests
{
    private readonly RecordingLogger _log = new();
    private readonly FakeProcessLauncher _launcher = new();
    private readonly string _folder = Path.GetTempPath();

    private GitWorkingTree Git() => new(_launcher, TimeProvider.System, logger: _log);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task What_git_said_when_it_refused_is_kept_at_debug_without_an_addresss_password()
    {
        _launcher.Respond = _ => new ProcessResult(128, "", "fatal: unable to access 'https://kim:hunter2@example.com/repo.git/': Could not resolve host");

        var result = await Git().RunAsync(_folder, ["fetch", "https://kim:hunter2@example.com/repo.git"], Token);

        Assert.Equal(128, result!.ExitCode);
        var (level, message) = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Debug, level);
        Assert.Contains("git fetch https://***@example.com/repo.git", message, StringComparison.Ordinal);
        Assert.Contains("exited with 128: fatal: unable to access 'https://***@example.com/repo.git/'", message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Git_that_couldnt_run_is_a_warning_with_why()
    {
        _launcher.StartFailure = new Win32Exception("The directory name is invalid.");

        Assert.Null(await Git().RunAsync(_folder, ["status"], Token));

        var (level, message) = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains("git status in ", message, StringComparison.Ordinal);
        Assert.EndsWith("didn't run: The directory name is invalid.", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_command_that_worked_says_nothing()
    {
        _launcher.Respond = _ => new ProcessResult(0, "main\n", "");

        await Git().RunAsync(_folder, ["branch", "--show-current"], Token);

        Assert.Empty(_log.Entries);
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly Lock _lock = new();
        private readonly List<(LogLevel, string)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message)> Entries
        {
            get
            {
                lock (_lock)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lock)
            {
                _entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
