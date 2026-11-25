using Deventry.Core.Accounts;
using Deventry.Core.LocalModels;
using Deventry.Core.Processes;
using Microsoft.Extensions.Logging;

namespace Deventry.App.Services;

/// <summary>Whether an account's server answers, and if not, why, in words for the tab.</summary>
/// <param name="Started">Deventry started it just now.</param>
public sealed record ServerReadiness(bool IsAnswering, string? Problem = null, bool Started = false)
{
    public static readonly ServerReadiness Answering = new(true);
}

/// <summary>
/// The servers Ollama and llama.cpp accounts' models run on (DESIGN.md §18, "Ollama and llama.cpp accounts"): asking
/// one, finding its program, and starting it when an account says to and it isn't running. A server Deventry starts
/// listens only on this computer, writes its output to the log folder, and is stopped when Deventry closes; one that was
/// already running, the user's own, is never stopped.
/// </summary>
public sealed class LocalServersService(AppServices services) : IAsyncDisposable
{
    /// <summary>How often a server Deventry started is asked whether it answers yet.</summary>
    internal static readonly TimeSpan StartPoll = TimeSpan.FromMilliseconds(500);

    private readonly ILogger _logger = services.Loggers.CreateLogger("LocalServers");
    private readonly Lock _lock = new();

    // The servers Deventry started, by kind and address, and a start under way, so two tabs don't start one each.
    private readonly Dictionary<string, Started> _started = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<ServerReadiness>> _starting = new(StringComparer.OrdinalIgnoreCase);

    private sealed record Started(IRunningProcess Process, string LogPath, Task Output);

    /// <summary>The client for a server at <paramref name="address"/>.</summary>
    public ILocalModelServer ServerAt(LocalServerKind kind, string address, string? key = null) =>
        LocalModelServers.For(kind, address, services.Http, services.Time, key);

    /// <summary>The client for <paramref name="account"/>'s server, with its key from the credential store.</summary>
    /// <exception cref="LocalServerException">The key couldn't be read.</exception>
    public async Task<ILocalModelServer> ServerForAsync(ClaudeAccount account)
    {
        var local = account.Local ?? throw new InvalidOperationException($"{account.Name} isn't an Ollama or llama.cpp account.");
        return ServerAt(account.Server!, local.Address, local.HasKey ? await services.Accounts.ReadKeyAsync(account) : null);
    }

    /// <summary>Whether a file exists, for finding a server's program. Tests replace it, so what's installed on the machine doesn't matter.</summary>
    public Func<string, bool> FileExists { get; internal set; } = File.Exists;

    /// <summary>The server's program: the account's, else on the user's <c>PATH</c>, else where its installer puts it.</summary>
    public async Task<string?> FindProgramAsync(LocalServerKind kind, string? configured)
    {
        var environment = await services.UserEnvironment.GetAsync();
        var searchPath = environment?.GetValueOrDefault("PATH") ?? services.UserEnvironment.PathVariable;
        var exists = FileExists;
        return await Task.Run(() => LocalServerLaunch.Find(kind, configured, searchPath, exists));
    }

    /// <summary>Where a server Deventry starts writes its output: <c>ollama.log</c>, <c>llama-server.log</c>.</summary>
    public string LogPathFor(LocalServerKind kind) => Path.Combine(services.Paths.LogDirectory, kind.Executable + ".log");

    /// <summary>
    /// Makes sure <paramref name="account"/>'s server answers, starting it when the account says to and the address is
    /// on this computer. Never throws: what's wrong comes back in words.
    /// </summary>
    public async Task<ServerReadiness> EnsureRunningAsync(ClaudeAccount account, CancellationToken cancellationToken = default)
    {
        if (account is not { Server: { } kind, Local: { } local })
        {
            return ServerReadiness.Answering;
        }
        ILocalModelServer server;
        try
        {
            server = await ServerForAsync(account);
        }
        catch (Exception ex) when (ex is LocalServerException or InvalidOperationException)
        {
            return new ServerReadiness(false, ex.Message);
        }
        if (await server.IsAnsweringAsync(cancellationToken))
        {
            return ServerReadiness.Answering;
        }
        var where = LocalServers.Where(local.Address);
        if (!local.StartServer || !LocalServers.IsOnThisComputer(local.Address))
        {
            return new ServerReadiness(false, $"{kind.ServerName} isn't answering at {Display(local.Address)}. Start {kind.ServerName}, then try again, or move the tab to another account.");
        }
        var key = Key(kind, local.Address);
        var settings = local.Copy();
        Task<ServerReadiness> start;
        lock (_lock)
        {
            // One start at a time an address: a second tab waits for the first's. One that's finished starts again.
            if (!_starting.TryGetValue(key, out start!) || start.IsCompleted)
            {
                start = Task.Run(() => StartAsync(kind, settings, server, key), CancellationToken.None);
                _starting[key] = start;
            }
        }
        _logger.LogDebug("{Server} isn't answering {Where}: starting it, or waiting for its start.", kind.ServerName, where);
        return await start.WaitAsync(cancellationToken);
    }

    private async Task<ServerReadiness> StartAsync(LocalServerKind kind, LocalServerSettings local, ILocalModelServer server, string key)
    {
        try
        {
            var program = await FindProgramAsync(kind, local.ServerPath);
            if (program is null)
            {
                return new ServerReadiness(false, local.ServerPath is { Length: > 0 } path
                    ? $"Deventry can't start {kind.ServerName}: {path} isn't there. Choose it again in Settings → Agents → Claude Code → Accounts."
                    : $"{kind.ServerName} isn't answering at {Display(local.Address)}, and Deventry can't find {kind.Executable} to start it. Install {kind.Name} ({kind.ProjectUrl}), or start it yourself.");
            }
            var environment = await services.UserEnvironment.GetAsync() ?? UserEnvironment.Snapshot();
            var spec = LocalServerLaunch.Command(kind, program, local, environment);
            var logPath = LogPathFor(kind);
            IRunningProcess process;
            lock (_lock)
            {
                // Started already, by another tab, and still running: it's loading.
                if (_started.TryGetValue(key, out var running) && !running.Process.Exited.IsCompleted)
                {
                    process = running.Process;
                    logPath = running.LogPath;
                }
                else
                {
                    process = services.Launcher.Start(spec);
                    _started[key] = new Started(process, logPath, Task.Run(() => WriteOutputAsync(process, logPath)));
                }
            }
            _logger.LogInformation("Started {Server} ({Program}) for {Address}.", kind.ServerName, program, local.Address);
            var deadline = services.Time.GetUtcNow() + LocalServerLaunch.StartTimeout(kind);
            while (services.Time.GetUtcNow() < deadline)
            {
                if (process.Exited.IsCompleted)
                {
                    return new ServerReadiness(false, $"{kind.ServerName} stopped as it started{LastLines(logPath)}");
                }
                if (await server.IsAnsweringAsync())
                {
                    return new ServerReadiness(true, Started: true);
                }
                await Task.Delay(StartPoll, services.Time);
            }
            return new ServerReadiness(false, $"Deventry started {kind.ServerName}, but it didn't answer at {Display(local.Address)} within {(int)LocalServerLaunch.StartTimeout(kind).TotalSeconds} seconds{LastLines(logPath)}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Couldn't start {Server}.", kind.ServerName);
            return new ServerReadiness(false, $"Deventry couldn't start {kind.ServerName}: {ex.Message}");
        }
    }

    /// <summary>A server's output, to its log, a line at a time, until it ends.</summary>
    private static async Task WriteOutputAsync(IRunningProcess process, string logPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        await using var log = new StreamWriter(new FileStream(logPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        var gate = new SemaphoreSlim(1, 1);
        async Task PumpAsync(System.Threading.Channels.ChannelReader<string> lines)
        {
            await foreach (var line in lines.ReadAllAsync())
            {
                await gate.WaitAsync();
                try
                {
                    await log.WriteLineAsync(line);
                }
                finally
                {
                    gate.Release();
                }
            }
        }
        await Task.WhenAll(PumpAsync(process.StandardOutput), PumpAsync(process.StandardError));
    }

    /// <summary>The last lines a server wrote, for saying why it didn't start: ": …".</summary>
    private static string LastLines(string logPath)
    {
        try
        {
            using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return lines.Length == 0 ? "." : $". It said: {string.Join(" ", lines.TakeLast(3))}";
        }
        catch (IOException)
        {
            return ".";
        }
    }

    private static string Key(LocalServerKind kind, string address) => $"{kind.Kind} {LocalServers.NormalizeAddress(address)}";

    /// <summary>An address as the tab says it: without its scheme.</summary>
    public static string Display(string address) =>
        LocalServers.NormalizeAddress(address) is { } normalized ? normalized[(normalized.IndexOf("://", StringComparison.Ordinal) + 3)..] : address;

    /// <summary>The servers Deventry started, still running.</summary>
    public int StartedCount
    {
        get
        {
            lock (_lock)
            {
                return _started.Values.Count(s => !s.Process.Exited.IsCompleted);
            }
        }
    }

    /// <summary>Stops the servers Deventry started, and waits for them to go: nothing else was using them.</summary>
    public async ValueTask DisposeAsync()
    {
        Started[] started;
        lock (_lock)
        {
            started = [.. _started.Values];
            _started.Clear();
        }
        foreach (var server in started)
        {
            try
            {
                server.Process.Kill();
                await server.Process.Exited.WaitAsync(TimeSpan.FromSeconds(10), services.Time);
                await server.Output.WaitAsync(TimeSpan.FromSeconds(2), services.Time);
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException)
            {
                _logger.LogWarning(ex, "A server Deventry started didn't stop in time.");
            }
            await server.Process.DisposeAsync();
        }
    }
}
