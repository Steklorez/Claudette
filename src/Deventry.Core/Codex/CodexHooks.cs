using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Protocol;

namespace Deventry.Core.Codex;

/// <summary>What a Codex patch does to a file.</summary>
public enum CodexPatchChange
{
    Add,
    Update,
    Delete,
}

/// <summary>A file a Codex patch (<c>apply_patch</c>) changes, relative to the folder Codex works in, as its header names it.</summary>
/// <param name="MovedTo">Where an update moves the file (<c>*** Move to:</c>); null when it stays.</param>
public sealed record CodexPatchFile(string Path, CodexPatchChange Change, string? MovedTo);

/// <summary>Deventry's hook as Codex lists it for a folder (<c>hooks/list</c>): the key and hash it's trusted by, and whether it is.</summary>
public sealed record CodexHookState(string Key, string Hash, string TrustStatus)
{
    public bool IsTrusted => TrustStatus is "trusted" or "managed";
}

/// <summary>
/// Deventry's own hook for a Codex tab's patches (DESIGN.md §18, "Perforce"): Codex runs it before each
/// <c>apply_patch</c>, as Claude Code calls Deventry's PreToolUse hook before Edit and Write, so Deventry checks the
/// files out of Perforce before Codex writes them. Codex runs a command, Deventry's own executable in hook mode, which
/// passes what Codex gave it to the tab whose <c>codex</c> ran it, over a pipe that tab's environment names, and gives
/// back the tab's answer. Its command is the same for every tab, so the user trusts it once.
/// </summary>
public static class CodexHooks
{
    /// <summary>The argument that starts Deventry in hook mode.</summary>
    public const string Option = "--codex-hook";

    /// <summary>The variable in a Codex tab's environment that names the pipe to the tab.</summary>
    public const string PipeVariable = "DEVENTRY_CODEX_HOOK_PIPE";

    /// <summary>The tool Codex's patches go through, which the hook matches.</summary>
    public const string PatchTool = "apply_patch";

    /// <summary>How long Codex lets the hook run; the tab answers before it's up.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Before <see cref="Timeout"/>, the helper gives up on the tab and lets the patch go on.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromSeconds(3);

    /// <summary>Lets the tool run, as a hook that has nothing to say does.</summary>
    public const string Continue = "{}";

    /// <summary>Deventry at <paramref name="executable"/> in hook mode.</summary>
    /// <exception cref="ArgumentException">The path can't go in a command as it is.</exception>
    public static string Command(string executable) => Accounts.ApiKeyHelper.IsSafePath(executable)
        ? $"\"{executable}\" {Option}"
        : throw new ArgumentException($"Deventry's path can't go in a command as it is: {executable}", nameof(executable));

    /// <summary>Deventry was started as the hook.</summary>
    public static bool IsHookLaunch(IReadOnlyList<string> args) => args is [Option];

    /// <summary>The <c>-c</c> configuration that adds the hook to one <c>codex app-server</c>.</summary>
    public static string Config(string command) =>
        $"hooks.PreToolUse=[{{matcher=\"{PatchTool}\",hooks=[{{type=\"command\",command={TomlString(command)},timeout={(int)Timeout.TotalSeconds}}}]}}]";

    private static string TomlString(string text) => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    /// <summary>The files a patch's text changes, as its headers name them (<c>*** Update File: …</c> and so on).</summary>
    public static IReadOnlyList<CodexPatchFile> Files(string patch)
    {
        var files = new List<CodexPatchFile>();
        foreach (var raw in patch.ReplaceLineEndings("\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (Header(line, "*** Add File: ") is { } added)
            {
                files.Add(new CodexPatchFile(added, CodexPatchChange.Add, null));
            }
            else if (Header(line, "*** Update File: ") is { } updated)
            {
                files.Add(new CodexPatchFile(updated, CodexPatchChange.Update, null));
            }
            else if (Header(line, "*** Delete File: ") is { } deleted)
            {
                files.Add(new CodexPatchFile(deleted, CodexPatchChange.Delete, null));
            }
            else if (Header(line, "*** Move to: ") is { } movedTo && files.Count > 0 && files[^1].Change == CodexPatchChange.Update)
            {
                files[^1] = files[^1] with { MovedTo = movedTo };
            }
        }
        return files;
    }

    private static string? Header(string line, string prefix) =>
        line.StartsWith(prefix, StringComparison.Ordinal) && line[prefix.Length..].Trim() is { Length: > 0 } path ? path : null;

    /// <summary>
    /// The files a hook call's patch changes, as full paths: <c>tool_input.command</c> is the patch, and its paths are
    /// relative to <c>cwd</c>. None for another tool.
    /// </summary>
    public static IReadOnlyList<CodexPatchFile> FilesOf(JsonObject hookInput)
    {
        if (hookInput.GetString("tool_name") != PatchTool || hookInput.GetObject("tool_input")?.GetString("command") is not { } patch)
        {
            return [];
        }
        var cwd = hookInput.GetString("cwd");
        string Full(string path) => cwd is null || Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(Path.Combine(cwd, path));
        return [.. Files(patch).Select(f => f with { Path = Full(f.Path), MovedTo = f.MovedTo is { } to ? Full(to) : null })];
    }

    /// <summary>
    /// The helper's side: passes the hook's input to the tab whose <c>codex</c> ran it, over the pipe
    /// <paramref name="pipeName"/>, and returns the tab's answer. With no tab to ask, or none answering in time, it lets
    /// the patch go on (<see cref="Continue"/>): a hook that fails would stop it.
    /// </summary>
    public static async Task<string> RelayAsync(string input, string? pipeName, TimeProvider time, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(pipeName))
        {
            return Continue;
        }
        try
        {
            // One line each way: the input as Codex wrote it may run over several.
            var line = (JsonNode.Parse(input) ?? new JsonObject()).ToJsonString();
            using var deadline = new CancellationTokenSource(Timeout - Margin, time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(linked.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\n" };
            await writer.WriteLineAsync(line.AsMemory(), linked.Token).ConfigureAwait(false);
            await writer.FlushAsync(linked.Token).ConfigureAwait(false);
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
            return await reader.ReadLineAsync(linked.Token).ConfigureAwait(false) is { Length: > 0 } answer ? answer : Continue;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or UnauthorizedAccessException or TimeoutException)
        {
            return Continue;
        }
    }
}

/// <summary>
/// The tab's end of the pipe its Codex's hook helper reaches it through (<see cref="CodexHooks"/>): each connection is
/// one hook call, a line of input, answered with a line. Dispose it as the tab's Codex stops.
/// </summary>
public sealed class CodexHookPipe : IAsyncDisposable
{
    private readonly Func<JsonObject, CancellationToken, Task<JsonObject>> _answer;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listening;

    /// <param name="answer">The tab's answer to a hook call; called off the UI thread.</param>
    public CodexHookPipe(Func<JsonObject, CancellationToken, Task<JsonObject>> answer)
    {
        _answer = answer;
        _listening = Task.Run(ListenAsync);
    }

    /// <summary>
    /// The pipe's name, for <see cref="CodexHooks.PipeVariable"/>. On macOS and Linux the pipe is a socket at
    /// <c>$TMPDIR/CoreFxPipe_&lt;name&gt;</c>, whose path macOS keeps to 104 characters, and its <c>TMPDIR</c> takes 49.
    /// </summary>
    public string Name { get; } = $"deventry-codex-{Guid.NewGuid().ToString("N")[..16]}";

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(Name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                // The next call can connect while this one is answered.
                _ = AnswerAsync(server);
                server = null;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                // A helper that went away: listen again.
            }
            finally
            {
                if (server is not null)
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
    }

    private async Task AnswerAsync(NamedPipeServerStream server)
    {
        await using (server.ConfigureAwait(false))
        {
            try
            {
                using var reader = new StreamReader(server, new UTF8Encoding(false), leaveOpen: true);
                var line = await reader.ReadLineAsync(_stop.Token).ConfigureAwait(false);
                var answer = line is { Length: > 0 } && JsonNode.Parse(line) is JsonObject input
                    ? await _answer(input, _stop.Token).ConfigureAwait(false)
                    : [];
                await using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\n" };
                await writer.WriteLineAsync(answer.ToJsonString().AsMemory(), _stop.Token).ConfigureAwait(false);
                await writer.FlushAsync(_stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or ObjectDisposedException)
            {
                // The helper lets the patch go on without an answer.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await _listening.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
        _stop.Dispose();
    }
}
