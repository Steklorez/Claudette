using System.Text.Json;
using System.Text.Json.Nodes;
using Deventry.Core.Protocol;

namespace Deventry.Core.Demo;

/// <summary>
/// Deventry standing in for its demo's <c>codex</c> (DESIGN.md §11, "Demo mode"; §18, "Codex, a second agent"), as
/// <see cref="StandInClaude"/> stands in for Claude Code: it speaks <c>codex app-server</c>'s JSON-RPC, reads a thread's
/// earlier turns back from <c>CODEX_HOME</c> (<see cref="DemoCodex.TurnsFolder"/>), answers each message with the next
/// of a few example turns, and reports Codex's usage for the header. It reads no project and runs nothing.
/// </summary>
public sealed class StandInCodex
{
    /// <summary>In the environment of the demo's <c>codex</c>: Deventry, started so, stands in for it.</summary>
    public const string Variable = "DEVENTRY_STAND_IN_CODEX";

    /// <summary>The Codex it says it is: one Deventry supports.</summary>
    public const string Version = "0.169.0";

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TimeProvider _time;
    private readonly string _home;
    private readonly string _folder;
    private readonly TimeSpan _delay;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lock _turnLock = new();
    private string _thread = Guid.CreateVersion7().ToString();
    private CancellationTokenSource? _turn;
    private int _turns;

    /// <param name="codexHome">Its <c>CODEX_HOME</c>, the demo's, where the threads' turns are kept.</param>
    /// <param name="folder">The folder it runs in, the tab's.</param>
    /// <param name="delay">How long it pauses between the pieces of a reply as it streams; none in tests.</param>
    public StandInCodex(TextReader input, TextWriter output, TimeProvider time, string codexHome, string folder, TimeSpan? delay = null)
    {
        _input = input;
        _output = output;
        _time = time;
        _home = codexHome;
        _folder = folder;
        _delay = delay ?? TimeSpan.FromMilliseconds(60);
    }

    /// <summary>Does what <c>codex</c> with <paramref name="args"/> would; returns its exit code.</summary>
    public async Task<int> RunAsync(string[] args)
    {
        switch (args)
        {
            case ["--version"]:
                await _output.WriteLineAsync($"codex-cli {Version}");
                break;
            case ["login", "status", ..]:
                await _output.WriteLineAsync("Logged in using ChatGPT");
                break;
            case ["login", ..] or ["logout", ..]:
                await _output.WriteLineAsync("This is Deventry's demo: its Codex is always signed in.");
                break;
            case ["app-server", ..]:
                await ServeAsync();
                break;
            default:
                await _output.WriteLineAsync($"Deventry's demo stands in for codex app-server, not codex {string.Join(' ', args)}.");
                await _output.FlushAsync();
                return 2;
        }
        await _output.FlushAsync();
        return 0;
    }

    private async Task ServeAsync()
    {
        string? line;
        while ((line = await _input.ReadLineAsync()) is not null)
        {
            JsonObject? message;
            try
            {
                message = JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }
            if (message?.GetString("method") is { } method && message["id"] is { } id)
            {
                await AnswerAsync(id, method, message.GetObject("params") ?? []);
            }
        }
        lock (_turnLock)
        {
            _turn?.Cancel();
        }
    }

    private async Task AnswerAsync(JsonNode id, string method, JsonObject parameters)
    {
        switch (method)
        {
            case "initialize":
                await RespondAsync(id, new JsonObject { ["userAgent"] = $"deventry-demo/{Version}", ["platformOs"] = OperatingSystem.IsWindows() ? "windows" : "unix" });
                return;
            case "model/list":
                await RespondAsync(id, new JsonObject
                {
                    ["data"] = new JsonArray(new JsonObject
                    {
                        ["id"] = DemoCodex.Offered.Value,
                        ["model"] = DemoCodex.Offered.ResolvedModel,
                        ["displayName"] = DemoCodex.Offered.DisplayName,
                        ["description"] = DemoCodex.Offered.Description,
                        ["hidden"] = false,
                        ["isDefault"] = DemoCodex.Offered.IsDefault,
                        ["defaultReasoningEffort"] = DemoCodex.Offered.DefaultEffort,
                        ["supportedReasoningEfforts"] = new JsonArray([.. DemoCodex.Offered.SupportedEffortLevels.Select(e => (JsonNode)Effort(e))]),
                    }),
                });
                return;
            case "account/read":
                await RespondAsync(id, new JsonObject { ["account"] = new JsonObject { ["type"] = "chatgpt", ["email"] = StandInClaude.Email, ["planType"] = "pro" }, ["requiresOpenaiAuth"] = true });
                return;
            case "thread/start":
                _thread = Guid.CreateVersion7().ToString();
                await RespondAsync(id, Started());
                await NotifyAsync("account/rateLimits/updated", new JsonObject { ["rateLimits"] = DemoCodex.RateLimits(_time.GetUtcNow(), _turns) });
                return;
            case "thread/read" when !File.Exists(DemoCodex.TurnsFile(_home, parameters.GetString("threadId") ?? "")):
            case "thread/resume" when !File.Exists(DemoCodex.TurnsFile(_home, parameters.GetString("threadId") ?? "")):
                await ErrorAsync(id, -32600, $"no rollout found for thread id {parameters.GetString("threadId")}");
                return;
            case "thread/read":
                await RespondAsync(id, new JsonObject { ["thread"] = new JsonObject { ["id"] = parameters.GetString("threadId") } });
                return;
            case "thread/resume":
                _thread = parameters.GetString("threadId")!;
                await RespondAsync(id, Started());
                await NotifyAsync("account/rateLimits/updated", new JsonObject { ["rateLimits"] = DemoCodex.RateLimits(_time.GetUtcNow(), _turns) });
                return;
            case "thread/fork":
                var source = DemoCodex.TurnsFile(_home, parameters.GetString("threadId") ?? "");
                _thread = Guid.CreateVersion7().ToString();
                Directory.CreateDirectory(Path.GetDirectoryName(source)!);
                File.Copy(source, DemoCodex.TurnsFile(_home, _thread), overwrite: true);
                await RespondAsync(id, Started());
                return;
            case "thread/turns/list":
                await RespondAsync(id, new JsonObject { ["data"] = TurnsOf(parameters.GetString("threadId") ?? _thread), ["nextCursor"] = null });
                return;
            case "turn/start":
                var turnId = Guid.CreateVersion7().ToString();
                await RespondAsync(id, new JsonObject { ["turn"] = new JsonObject { ["id"] = turnId, ["status"] = "inProgress" } });
                var text = string.Join("\n", (parameters.GetArray("input") ?? []).OfType<JsonObject>().Select(i => i.GetString("text")).OfType<string>());
                CancellationTokenSource turn;
                lock (_turnLock)
                {
                    turn = _turn = new CancellationTokenSource();
                }
                _ = Task.Run(() => RunTurnAsync(turnId, text, parameters.GetString("clientUserMessageId"), turn.Token));
                return;
            case "turn/interrupt":
                lock (_turnLock)
                {
                    _turn?.Cancel();
                }
                await RespondAsync(id, []);
                return;
            case "hooks/list":
                await RespondAsync(id, new JsonObject { ["data"] = new JsonArray() });
                return;
            case "thread/settings/update" or "thread/name/set" or "config/batchWrite":
                await RespondAsync(id, []);
                return;
            default:
                await ErrorAsync(id, -32601, $"The demo's Codex doesn't do {method}.");
                return;
        }
    }

    private JsonObject Started() => new()
    {
        ["thread"] = new JsonObject { ["id"] = _thread },
        ["model"] = DemoCodex.Model,
        ["reasoningEffort"] = DemoCodex.Offered.DefaultEffort,
    };

    private static JsonObject Effort(string effort) => new() { ["reasoningEffort"] = effort, ["description"] = effort };

    /// <summary>The thread's turns as Codex lists them: its kept items, grouped by turn, oldest first.</summary>
    private JsonArray TurnsOf(string thread)
    {
        var turns = new JsonArray();
        var file = DemoCodex.TurnsFile(_home, thread);
        if (!File.Exists(file))
        {
            return turns;
        }
        JsonObject? current = null;
        foreach (var line in File.ReadLines(file))
        {
            if (JsonNode.Parse(line) is not JsonObject kept || kept.GetObject("item") is not { } item)
            {
                continue;
            }
            var turnId = kept.GetString("turnId");
            if (current is null || current.GetString("id") != turnId)
            {
                current = new JsonObject { ["id"] = turnId, ["status"] = "completed", ["items"] = new JsonArray() };
                turns.Add(current);
            }
            current.GetArray("items")!.Add(item.DeepClone());
        }
        return turns;
    }

    /// <summary>A message's turn: the next of the example turns, kept with the thread as Codex would keep it.</summary>
    private async Task RunTurnAsync(string turnId, string text, string? clientId, CancellationToken cancellationToken)
    {
        var status = "completed";
        await NotifyAsync("turn/started", new JsonObject { ["threadId"] = _thread, ["turn"] = new JsonObject { ["id"] = turnId, ["status"] = "inProgress" } });
        try
        {
            await ItemAsync(turnId, new JsonObject
            {
                ["type"] = "userMessage",
                ["id"] = Guid.CreateVersion7().ToString(),
                ["clientId"] = clientId,
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            });
            if (_turns++ % 2 == 1)
            {
                await ItemAsync(turnId, new JsonObject
                {
                    ["type"] = "reasoning",
                    ["id"] = Guid.CreateVersion7().ToString(),
                    ["summary"] = new JsonArray("The demo's next turn shows a command. Deventry shows Codex's commands as it shows Claude's tool calls, by what they do: this one reads a file."),
                });
                var path = Path.Combine(_folder, "README.md");
                var command = new JsonObject
                {
                    ["type"] = "commandExecution",
                    ["id"] = Guid.CreateVersion7().ToString(),
                    ["command"] = "cat README.md",
                    ["commandActions"] = new JsonArray(new JsonObject { ["type"] = "read", ["command"] = "cat README.md", ["name"] = "README.md", ["path"] = path }),
                    ["status"] = "inProgress",
                };
                await NotifyAsync("item/started", new JsonObject { ["threadId"] = _thread, ["turnId"] = turnId, ["item"] = command });
                await PauseAsync(cancellationToken);
                command["status"] = "completed";
                command["aggregatedOutput"] = "(The demo's files are made up, so there's nothing here to read.)\n";
                command["exitCode"] = 0;
                await ItemAsync(turnId, command);
                await ReplyAsync(turnId, "That was a command, shown by what it did: Codex read a file. A real Codex would go on from what it found; the demo's stops here.", cancellationToken);
            }
            else
            {
                await ReplyAsync(turnId, """
                    This is **Deventry's demo**, and I'm a stand-in for Codex: I can't read your projects or run anything, and nothing you send leaves this computer.

                    Each message gets the next of two example turns: a reply like this one, and one that runs a command. The tab above shows a whole conversation with Codex, read back as Codex keeps it.
                    """, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            status = "interrupted";
        }
        await NotifyAsync("thread/tokenUsage/updated", new JsonObject
        {
            ["threadId"] = _thread,
            ["turnId"] = turnId,
            ["tokenUsage"] = new JsonObject
            {
                ["total"] = new JsonObject { ["inputTokens"] = 18_400 * _turns, ["cachedInputTokens"] = 14_200 * _turns, ["outputTokens"] = 320 * _turns },
                ["last"] = new JsonObject { ["inputTokens"] = 18_400, ["cachedInputTokens"] = 14_200, ["outputTokens"] = 320 },
                ["modelContextWindow"] = 272_000,
            },
        });
        await NotifyAsync("account/rateLimits/updated", new JsonObject { ["rateLimits"] = DemoCodex.RateLimits(_time.GetUtcNow(), _turns) });
        await NotifyAsync("turn/completed", new JsonObject { ["threadId"] = _thread, ["turn"] = new JsonObject { ["id"] = turnId, ["status"] = status, ["durationMs"] = 900 } });
    }

    /// <summary>Streams <paramref name="reply"/> in a few pieces, then sends it whole.</summary>
    private async Task ReplyAsync(string turnId, string reply, CancellationToken cancellationToken)
    {
        reply = reply.ReplaceLineEndings("\n").Trim();
        var id = Guid.CreateVersion7().ToString();
        const int pieces = 6;
        var size = Math.Max(1, (reply.Length + pieces - 1) / pieces);
        for (var at = 0; at < reply.Length; at += size)
        {
            await NotifyAsync("item/agentMessage/delta", new JsonObject { ["threadId"] = _thread, ["turnId"] = turnId, ["itemId"] = id, ["delta"] = reply.Substring(at, Math.Min(size, reply.Length - at)) });
            await PauseAsync(cancellationToken);
        }
        await ItemAsync(turnId, new JsonObject { ["type"] = "agentMessage", ["id"] = id, ["text"] = reply });
    }

    /// <summary>A finished item: told, and kept with the thread.</summary>
    private async Task ItemAsync(string turnId, JsonObject item)
    {
        await NotifyAsync("item/completed", new JsonObject { ["threadId"] = _thread, ["turnId"] = turnId, ["item"] = item });
        var file = DemoCodex.TurnsFile(_home, _thread);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var kept = item.DeepClone().AsObject();
        kept["startedAtMs"] = _time.GetUtcNow().ToUnixTimeMilliseconds();
        await File.AppendAllTextAsync(file, new JsonObject { ["turnId"] = turnId, ["item"] = kept }.ToJsonString() + "\n");
    }

    private async Task PauseAsync(CancellationToken cancellationToken)
    {
        if (_delay > TimeSpan.Zero)
        {
            await Task.Delay(_delay, _time, cancellationToken);
        }
    }

    private Task RespondAsync(JsonNode id, JsonObject result) => WriteAsync(new JsonObject { ["id"] = id.DeepClone(), ["result"] = result });

    private Task ErrorAsync(JsonNode id, int code, string message) => WriteAsync(new JsonObject { ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } });

    private Task NotifyAsync(string method, JsonObject parameters) => WriteAsync(new JsonObject { ["method"] = method, ["params"] = parameters });

    private async Task WriteAsync(JsonObject message)
    {
        await _writeLock.WaitAsync();
        try
        {
            await _output.WriteLineAsync(message.ToJsonString());
            await _output.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }
}
