using Deventry.Core.Claude;
using Deventry.Core.Processes;
using Deventry.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Deventry.Core.Codex;

/// <summary>How a Codex tab's <c>codex app-server</c> starts (DESIGN.md §18, "Codex, a second agent").</summary>
public sealed record CodexLaunchOptions
{
    public required CodexStartOptions Start { get; init; }

    /// <summary>Configuration for this process alone, each <c>key=value</c> as <c>-c</c> takes it (TOML values).</summary>
    public IReadOnlyList<string> ConfigOverrides { get; init; } = [];

    /// <summary>Variables set (or, with null, removed) on top of the user environment.</summary>
    public IReadOnlyDictionary<string, string?> EnvironmentOverrides { get; init; } = new Dictionary<string, string?>();

    /// <summary>Where to log the raw protocol traffic (DESIGN.md §13, "Logging"); null for none.</summary>
    public string? ProtocolLogPath { get; init; }
}

public interface ICodexSessionFactory
{
    /// <summary>Starts a <c>codex app-server</c> and its thread.</summary>
    Task<CodexSession> StartAsync(CodexLaunchOptions options, CancellationToken cancellationToken = default);
}

/// <summary>The arguments <c>codex</c> runs with.</summary>
public static class CodexArguments
{
    /// <summary>Configuration every tab's Codex gets: a project's <c>CLAUDE.md</c> read when it has no <c>AGENTS.md</c>.</summary>
    public const string ClaudeMdFallback = "project_doc_fallback_filenames=[\"CLAUDE.md\"]";

    public static IReadOnlyList<string> ForAppServer(CodexLaunchOptions options) =>
        ["app-server", .. options.ConfigOverrides.SelectMany(c => new[] { "-c", c })];
}

/// <param name="environment">The user environment each <c>codex</c> starts from (DESIGN.md §13). Null: Deventry's own.</param>
public sealed class CodexSessionFactory(
    string codexPath,
    IProcessLauncher launcher,
    TimeProvider timeProvider,
    ILoggerFactory? loggerFactory = null,
    ProtocolDiagnostics? diagnostics = null,
    UserEnvironment? environment = null) : ICodexSessionFactory
{
    private readonly ILoggerFactory _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

    public string CodexPath => codexPath;

    /// <summary>
    /// On Windows, the sandboxes to try, in order (DESIGN.md §18, "Codex's sandbox"): whatever the user's own configuration
    /// chose, then MXC, then the unelevated one. The elevated one needs an administrator to set up, so it's left to the
    /// user's configuration.
    /// </summary>
    public static IReadOnlyList<string?> WindowsSandboxes { get; } = [null, "mxc", "unelevated"];

    public async Task<CodexSession> StartAsync(CodexLaunchOptions options, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows() || options.ConfigOverrides.Any(o => o.StartsWith("windows.sandbox", StringComparison.Ordinal)))
        {
            return await StartOnceAsync(options, cancellationToken).ConfigureAwait(false);
        }
        foreach (var sandbox in WindowsSandboxes)
        {
            var attempt = options with
            {
                Start = options.Start with { RequireWindowsSandbox = true },
                ConfigOverrides = sandbox is null ? options.ConfigOverrides : [.. options.ConfigOverrides, $"windows.sandbox=\"{sandbox}\""],
            };
            try
            {
                return await StartOnceAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch (CodexSandboxNotReadyException)
            {
                // The next one, if it's ready.
            }
            catch (CodexRequestException) when (sandbox is not null)
            {
                // That sandbox can't be chosen here (MXC unavailable, or not allowed): the next one.
            }
        }
        // None is ready: Codex asks before each command and edit, which the tab says (SandboxReady).
        var session = await StartOnceAsync(options, cancellationToken).ConfigureAwait(false);
        session.SandboxReady = false;
        return session;
    }

    private async Task<CodexSession> StartOnceAsync(CodexLaunchOptions options, CancellationToken cancellationToken)
    {
        var userEnvironment = environment is null ? null : await environment.GetAsync(cancellationToken).ConfigureAwait(false);
        var spec = new ProcessStartSpec(codexPath, CodexArguments.ForAppServer(options))
        {
            WorkingDirectory = options.Start.WorkingDirectory,
            // Both agents' session variables go, as for claude (DESIGN.md §18, "The environment").
            Environment = ClaudeEnvironment.From(userEnvironment, options.EnvironmentOverrides),
            TrackProcessTree = true,
        };
        var log = options.ProtocolLogPath is { } logPath ? new ProtocolLog(logPath, timeProvider) : null;
        IClaudeTransport transport;
        try
        {
            transport = new ProcessClaudeTransport(launcher.Start(spec), _loggerFactory.CreateLogger<ProcessClaudeTransport>());
        }
        catch
        {
            log?.Dispose();
            throw;
        }
        if (log is not null)
        {
            transport = new LoggingTransport(transport, log);
        }
        var session = new CodexSession(transport, timeProvider, _loggerFactory.CreateLogger<CodexSession>(), diagnostics);
        try
        {
            await session.StartAsync(options.Start, cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
