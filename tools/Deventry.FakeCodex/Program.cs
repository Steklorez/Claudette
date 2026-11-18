// fake-codex: stands in for the `codex` CLI in tests (DESIGN.md §18, "Codex, a second agent"), as fake-claude does for
// `claude`. It speaks `codex app-server`'s JSON-RPC, in the shapes recorded against Codex 0.169.0.
//
//   fake-codex --version              prints "codex-cli <version>"
//   fake-codex login status           "Logged in using ChatGPT", or "Not logged in" (exit 1) when signed out
//   fake-codex login [--device-auth]  signs in at once (no browser)
//   fake-codex logout                 signs out
//   fake-codex app-server [-c k=v]…   a scripted app-server (below)
//
// Environment:
//   FAKE_CODEX_VERSION    version to report (default 0.169.0)
//   FAKE_CODEX_LOGGED_IN  "0" to start signed out (default signed in); kept in fake-codex-auth.json in CODEX_HOME
//   FAKE_CODEX_RECORD     path: write {args, cwd, env} there at startup
//   FAKE_CODEX_USAGE      "demo": each turn reports the account's rate limits, its five-hour window starting at 35%
//                         and climbing 3% a turn, its week at 12%; unset: none
//   FAKE_CODEX_SANDBOX    the Windows sandboxes that are ready, by name ("mxc,unelevated"); "windowsSandbox/readiness"
//                         says whether the one "-c windows.sandbox=..." chose is ("none" without one), and choosing mxc
//                         when it isn't ready fails, as Codex's does where MXC is unavailable. Unset: always ready
//
// Turn prompts (the first word):
//   RUN <command>      a commandExecution that "runs" it: its output is "ran: <command>"
//   READ <path>        a commandExecution Codex labels a read of <path>, its output the file's text
//   EDIT_FILE <path>   a fileChange that adds a line to the file, which it writes (or creates the file), with its hunk
//   ASK_PERMISSION     a command that asks for approval first, then says whether it was allowed
//   REASON             a reasoning summary before the reply
//   SLOW               streams its reply over about ten seconds (for interrupts and steering)
//   LIMIT              a turn that fails at the usage limit, the five-hour window at 100%
//   anything else      replies "pong: <prompt>"; an image sent with it is named: "[images: 1]"
// A thread's finished items are kept in deventry-turns/<thread>.jsonl in CODEX_HOME (as the demo writes its Codex tab's), which "thread/turns/list" reads back and
// without which "thread/read" and "thread/resume" fail, as Codex's do for a thread it has no rollout of; its prompts and replies go in
// its rollout too, for History.
// Its models are gpt-fake-1, the default, and the demo's Codex tab's, gpt-5.5-codex; a thread starts or resumes with the one it's
// given. Each turn spends 1,200 input tokens (800 of them cached) and 40 output tokens of the thread's model. A message steered into a
// running turn ("turn/steer") ends it with "steered: <message>".
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var version = Environment.GetEnvironmentVariable("FAKE_CODEX_VERSION") ?? "0.169.0";
var home = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");

if (args is ["--version"])
{
    Console.WriteLine($"codex-cli {version}");
    return 0;
}
if (args is ["login", "status", ..])
{
    if (Auth.IsSignedIn(home))
    {
        Console.WriteLine("Logged in using ChatGPT");
        return 0;
    }
    Console.WriteLine("Not logged in");
    return 1;
}
if (args is ["login", ..])
{
    Auth.Set(home, true);
    Console.WriteLine("Successfully logged in");
    return 0;
}
if (args is ["logout", ..])
{
    Auth.Set(home, false);
    Console.WriteLine("Successfully logged out");
    return 0;
}
if (args is not ["app-server", ..])
{
    Console.Error.WriteLine($"fake-codex: unknown command: {string.Join(' ', args)}");
    return 2;
}
if (Environment.GetEnvironmentVariable("FAKE_CODEX_RECORD") is { Length: > 0 } record)
{
    var env = new JsonObject();
    foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
    {
        env[(string)entry.Key] = (string?)entry.Value;
    }
    File.WriteAllText(record, new JsonObject { ["args"] = new JsonArray([.. args.Select(a => (JsonNode?)a)]), ["cwd"] = Environment.CurrentDirectory, ["env"] = env }.ToJsonString());
}

// The Windows sandbox the launch chose, as -c windows.sandbox="<name>" sets it.
var sandbox = args.Select((a, i) => (a, i)).Where(p => p.a == "-c" && p.i + 1 < args.Length && args[p.i + 1].StartsWith("windows.sandbox=", StringComparison.Ordinal))
    .Select(p => args[p.i + 1]["windows.sandbox=".Length..].Trim('"')).LastOrDefault() ?? "none";
var server = new AppServer(version, home, sandbox);
await server.RunAsync();
return 0;

internal static class Auth
{
    public static bool IsSignedIn(string home)
    {
        var file = Path.Combine(home, "fake-codex-auth.json");
        if (File.Exists(file))
        {
            return JsonNode.Parse(File.ReadAllText(file))?["signedIn"]?.GetValue<bool>() == true;
        }
        return Environment.GetEnvironmentVariable("FAKE_CODEX_LOGGED_IN") != "0";
    }

    public static void Set(string home, bool signedIn)
    {
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Combine(home, "fake-codex-auth.json"), new JsonObject { ["signedIn"] = signedIn }.ToJsonString());
    }
}

internal sealed class AppServer(string version, string home, string sandbox)
{
    private readonly Lock _write = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonObject>> _answers = new();
    private long _nextRequest = 1000;
    private string? _thread;
    private string? _rollout;
    private string _model = "gpt-fake-1";
    private Turn? _turn;
    private long _inputTotal;
    private long _cachedTotal;
    private long _outputTotal;
    private int _turns;

    public async Task RunAsync()
    {
        string? line;
        while ((line = await Console.In.ReadLineAsync()) is not null)
        {
            if (line.Length == 0 || JsonNode.Parse(line) is not JsonObject message)
            {
                continue;
            }
            var method = message["method"]?.GetValue<string>();
            var id = message["id"];
            if (method is null)
            {
                // An answer to one of ours.
                if (id is JsonValue value && value.TryGetValue<long>(out var answered) && _answers.TryRemove(answered, out var waiting))
                {
                    waiting.TrySetResult(message["result"] as JsonObject ?? []);
                }
                continue;
            }
            if (id is null)
            {
                continue; // initialized, and other notifications.
            }
            var parameters = message["params"] as JsonObject ?? [];
            try
            {
                Answer(id, method, parameters);
            }
            catch (Exception ex)
            {
                Send(new JsonObject { ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = -32603, ["message"] = ex.Message } });
            }
        }
        _turn?.Cancel();
    }

    private void Answer(JsonNode id, string method, JsonObject parameters)
    {
        switch (method)
        {
            case "initialize":
                Respond(id, new JsonObject { ["userAgent"] = $"deventry/{version} (fake)", ["platformOs"] = OperatingSystem.IsWindows() ? "windows" : "unix" });
                break;
            case "model/list":
                Respond(id, new JsonObject { ["data"] = new JsonArray(Model("gpt-fake-1", "GPT Fake 1", isDefault: true), Model("gpt-5.5-codex", "GPT-5.5-Codex", isDefault: false)) });
                break;
            case "account/read":
                Respond(id, Auth.IsSignedIn(home)
                    ? new JsonObject { ["account"] = new JsonObject { ["type"] = "chatgpt", ["email"] = "fake@example.com", ["planType"] = "pro" }, ["requiresOpenaiAuth"] = true }
                    : new JsonObject { ["account"] = null, ["requiresOpenaiAuth"] = true });
                break;
            case "windowsSandbox/readiness":
                var ready = Environment.GetEnvironmentVariable("FAKE_CODEX_SANDBOX") is not { } readySandboxes
                    || readySandboxes.Split(',', StringSplitOptions.TrimEntries).Contains(sandbox);
                if (!ready && sandbox == "mxc")
                {
                    Error(id, -32600, "the MXC sandbox is unavailable on this machine");
                    break;
                }
                Respond(id, new JsonObject { ["status"] = ready ? "ready" : sandbox == "none" ? "notConfigured" : "updateRequired" });
                break;
            case "account/rateLimits/read":
                Respond(id, new JsonObject { ["rateLimits"] = RateLimits() });
                break;
            case "thread/start":
                _thread = Guid.CreateVersion7().ToString();
                _model = parameters["model"]?.GetValue<string>() ?? _model;
                Respond(id, new JsonObject
                {
                    ["thread"] = new JsonObject { ["id"] = _thread, ["path"] = _rollout = WriteRollout(_thread, parameters["cwd"]?.GetValue<string>()) },
                    ["model"] = _model,
                    ["reasoningEffort"] = "medium",
                });
                break;
            case "thread/read" or "thread/resume" when !File.Exists(ItemsFile(parameters["threadId"]?.GetValue<string>() ?? "")):
                Error(id, -32600, $"no rollout found for thread id {parameters["threadId"]}");
                break;
            case "thread/read":
                Respond(id, new JsonObject { ["thread"] = new JsonObject { ["id"] = parameters["threadId"]?.DeepClone(), ["status"] = new JsonObject { ["type"] = "notLoaded" } } });
                break;
            case "thread/resume":
                var resumed = parameters["threadId"]!.GetValue<string>();
                _thread = resumed;
                _model = parameters["model"]?.GetValue<string>() ?? _model;
                _rollout = Directory.Exists(Path.Combine(home, "sessions"))
                    ? Directory.EnumerateFiles(Path.Combine(home, "sessions"), $"rollout-*-{resumed}.jsonl", SearchOption.AllDirectories).FirstOrDefault()
                    : null;
                Respond(id, new JsonObject { ["thread"] = new JsonObject { ["id"] = _thread, ["path"] = _rollout }, ["model"] = _model, ["reasoningEffort"] = "medium" });
                if (Environment.GetEnvironmentVariable("FAKE_CODEX_USAGE") == "demo")
                {
                    // The header shows Codex's usage as the tab is selected, before its first turn here.
                    Notify("account/rateLimits/updated", new JsonObject { ["rateLimits"] = RateLimits() });
                }
                break;
            case "thread/turns/list":
                Respond(id, new JsonObject { ["data"] = TurnsOf(parameters["threadId"]?.GetValue<string>() ?? ""), ["nextCursor"] = null });
                break;
            case "thread/settings/update":
                _model = parameters["model"]?.GetValue<string>() ?? _model;
                Respond(id, []);
                break;
            case "thread/name/set" or "thread/compact/start":
                Respond(id, []);
                break;
            case "turn/start":
                var turnId = Guid.CreateVersion7().ToString();
                Respond(id, new JsonObject { ["turn"] = new JsonObject { ["id"] = turnId, ["status"] = "inProgress", ["items"] = new JsonArray() } });
                _turn = new Turn(turnId);
                var input = parameters["input"] as JsonArray ?? [];
                var clientId = parameters["clientUserMessageId"]?.GetValue<string>();
                _ = Task.Run(() => RunTurnAsync(_turn, input, clientId));
                break;
            case "turn/steer":
                if (_turn is not { } running)
                {
                    Error(id, -32600, "no active turn to steer");
                    break;
                }
                running.Steered = Text(parameters["input"] as JsonArray ?? []);
                Respond(id, new JsonObject { ["turnId"] = running.Id });
                break;
            case "turn/interrupt":
                if (_turn is not { } current)
                {
                    Error(id, -32600, "no active turn to interrupt");
                    break;
                }
                current.Cancel();
                Respond(id, []);
                break;
            default:
                Error(id, -32601, $"unknown method: {method}");
                break;
        }
    }

    private async Task RunTurnAsync(Turn turn, JsonArray input, string? clientId)
    {
        var text = Text(input);
        var images = input.OfType<JsonObject>().Count(i => i["type"]?.GetValue<string>() is "image" or "localImage");
        Notify("turn/started", new JsonObject { ["threadId"] = _thread, ["turn"] = new JsonObject { ["id"] = turn.Id, ["status"] = "inProgress" } });
        Item(turn, "item/completed", new JsonObject
        {
            ["type"] = "userMessage",
            ["id"] = Guid.CreateVersion7().ToString(),
            ["clientId"] = clientId,
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        });
        var words = text.Split(' ', 2);
        var rest = words.Length > 1 ? words[1].Trim() : "";
        var status = "completed";
        JsonObject? error = null;
        try
        {
            switch (words[0])
            {
                case "RUN":
                    Command(turn, rest, null, $"ran: {rest}");
                    Reply(turn, "done");
                    break;
                case "READ":
                    var read = File.Exists(rest) ? File.ReadAllText(rest) : "";
                    Command(turn, $"Get-Content {rest}", new JsonObject { ["type"] = "read", ["command"] = $"Get-Content {rest}", ["name"] = Path.GetFileName(rest), ["path"] = rest }, read);
                    Reply(turn, "read it");
                    break;
                case "EDIT_FILE":
                    EditFile(turn, rest);
                    Reply(turn, "edited");
                    break;
                case "ASK_PERMISSION":
                    Reply(turn, await AskPermissionAsync(turn) ? "allowed" : "denied");
                    break;
                case "REASON":
                    var reasoning = Guid.CreateVersion7().ToString();
                    Item(turn, "item/started", new JsonObject { ["type"] = "reasoning", ["id"] = reasoning, ["summary"] = new JsonArray() });
                    Notify("item/reasoning/summaryTextDelta", new JsonObject { ["threadId"] = _thread, ["turnId"] = turn.Id, ["itemId"] = reasoning, ["delta"] = "Thinking it over." });
                    Item(turn, "item/completed", new JsonObject { ["type"] = "reasoning", ["id"] = reasoning, ["summary"] = new JsonArray("Thinking it over.") });
                    Reply(turn, "pong after thinking");
                    break;
                case "SLOW":
                    var message = Guid.CreateVersion7().ToString();
                    var streamed = new StringBuilder();
                    Item(turn, "item/started", new JsonObject { ["type"] = "agentMessage", ["id"] = message, ["text"] = "" });
                    for (var i = 1; i <= 20; i++)
                    {
                        await Task.Delay(500, turn.Token);
                        if (turn.Steered is { } steered)
                        {
                            streamed.Clear();
                            Reply(turn, $"steered: {steered}");
                            break;
                        }
                        var word = $"word{i} ";
                        streamed.Append(word);
                        Notify("item/agentMessage/delta", new JsonObject { ["threadId"] = _thread, ["turnId"] = turn.Id, ["itemId"] = message, ["delta"] = word });
                    }
                    if (streamed.Length > 0)
                    {
                        Item(turn, "item/completed", new JsonObject { ["type"] = "agentMessage", ["id"] = message, ["text"] = streamed.ToString() });
                    }
                    break;
                case "LIMIT":
                    Notify("account/rateLimits/updated", new JsonObject { ["rateLimits"] = RateLimits(full: true) });
                    status = "failed";
                    error = new JsonObject { ["message"] = "You've hit your usage limit.", ["codexErrorInfo"] = "usageLimitExceeded" };
                    break;
                default:
                    Reply(turn, images > 0 ? $"pong: {text} [images: {images}]" : $"pong: {text}");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            status = "interrupted";
        }
        catch (Exception ex)
        {
            // A fault in the fake ends the turn, rather than leaving a test waiting for it.
            status = "failed";
            error = new JsonObject { ["message"] = $"fake-codex: {ex.Message}" };
        }
        if (status != "interrupted")
        {
            _inputTotal += 1200;
            _cachedTotal += 800;
            _outputTotal += 40;
            Notify("thread/tokenUsage/updated", new JsonObject
            {
                ["threadId"] = _thread,
                ["turnId"] = turn.Id,
                ["tokenUsage"] = new JsonObject
                {
                    ["total"] = Usage(_inputTotal, _cachedTotal, _outputTotal),
                    ["last"] = Usage(1200, 800, 40),
                    ["modelContextWindow"] = 258400,
                },
            });
            _turns++;
            if (Environment.GetEnvironmentVariable("FAKE_CODEX_USAGE") == "demo" && status == "completed")
            {
                Notify("account/rateLimits/updated", new JsonObject { ["rateLimits"] = RateLimits() });
            }
        }
        _turn = null;
        Notify("turn/completed", new JsonObject
        {
            ["threadId"] = _thread,
            ["turn"] = new JsonObject { ["id"] = turn.Id, ["status"] = status, ["error"] = error, ["items"] = new JsonArray(), ["durationMs"] = 100 },
        });
    }

    private void Reply(Turn turn, string text)
    {
        var id = Guid.CreateVersion7().ToString();
        Item(turn, "item/started", new JsonObject { ["type"] = "agentMessage", ["id"] = id, ["text"] = "" });
        Notify("item/agentMessage/delta", new JsonObject { ["threadId"] = _thread, ["turnId"] = turn.Id, ["itemId"] = id, ["delta"] = text });
        Item(turn, "item/completed", new JsonObject { ["type"] = "agentMessage", ["id"] = id, ["text"] = text });
    }

    private void Command(Turn turn, string command, JsonObject? action, string output)
    {
        var id = $"exec-{Guid.CreateVersion7()}";
        var actions = new JsonArray(action ?? new JsonObject { ["type"] = "unknown", ["command"] = command });
        var wrapped = OperatingSystem.IsWindows() ? $"\"powershell.exe\" -Command '{command}'" : $"/bin/bash -lc '{command}'";
        Item(turn, "item/started", new JsonObject { ["type"] = "commandExecution", ["id"] = id, ["command"] = wrapped, ["cwd"] = Environment.CurrentDirectory, ["status"] = "inProgress", ["commandActions"] = actions });
        Item(turn, "item/completed", new JsonObject
        {
            ["type"] = "commandExecution",
            ["id"] = id,
            ["command"] = wrapped,
            ["status"] = "completed",
            ["commandActions"] = actions.DeepClone(),
            ["aggregatedOutput"] = output,
            ["exitCode"] = 0,
            ["durationMs"] = 80,
        });
    }

    private void EditFile(Turn turn, string path)
    {
        var id = $"patch-{Guid.CreateVersion7()}";
        JsonObject change;
        if (File.Exists(path))
        {
            var before = File.ReadAllText(path);
            var lines = before.Split('\n').Length - (before.EndsWith('\n') ? 1 : 0);
            var added = "edited by fake-codex";
            File.WriteAllText(path, before + (before.Length == 0 || before.EndsWith('\n') ? "" : "\n") + added + "\n");
            change = new JsonObject { ["path"] = path, ["kind"] = new JsonObject { ["type"] = "update", ["move_path"] = null }, ["diff"] = $"@@ -{lines},0 +{lines + 1} @@\n+{added}\n" };
        }
        else
        {
            File.WriteAllText(path, "written by fake-codex\n");
            change = new JsonObject { ["path"] = path, ["kind"] = new JsonObject { ["type"] = "add" }, ["diff"] = "written by fake-codex\n" };
        }
        Item(turn, "item/started", new JsonObject { ["type"] = "fileChange", ["id"] = id, ["status"] = "inProgress", ["changes"] = new JsonArray(change.DeepClone()) });
        Item(turn, "item/completed", new JsonObject { ["type"] = "fileChange", ["id"] = id, ["status"] = "completed", ["changes"] = new JsonArray(change) });
    }

    private async Task<bool> AskPermissionAsync(Turn turn)
    {
        var id = $"exec-{Guid.CreateVersion7()}";
        var actions = new JsonArray(new JsonObject { ["type"] = "unknown", ["command"] = "git push" });
        Item(turn, "item/started", new JsonObject { ["type"] = "commandExecution", ["id"] = id, ["command"] = "git push", ["status"] = "inProgress", ["commandActions"] = actions });
        var request = Interlocked.Increment(ref _nextRequest);
        var answer = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _answers[request] = answer;
        Send(new JsonObject
        {
            ["method"] = "item/commandExecution/requestApproval",
            ["id"] = request,
            ["params"] = new JsonObject { ["threadId"] = _thread, ["turnId"] = turn.Id, ["itemId"] = id, ["command"] = "git push", ["reason"] = "It needs the network.", ["commandActions"] = actions.DeepClone() },
        });
        JsonObject result;
        try
        {
            result = await answer.Task.WaitAsync(turn.Token);
        }
        finally
        {
            _answers.TryRemove(request, out _);
            Notify("serverRequest/resolved", new JsonObject { ["threadId"] = _thread, ["requestId"] = request });
        }
        var allowed = result["decision"]?.GetValue<string>() is "accept" or "acceptForSession";
        Item(turn, "item/completed", new JsonObject
        {
            ["type"] = "commandExecution",
            ["id"] = id,
            ["command"] = "git push",
            ["status"] = allowed ? "completed" : "declined",
            ["commandActions"] = actions.DeepClone(),
            ["aggregatedOutput"] = allowed ? "pushed" : "",
            ["exitCode"] = allowed ? 0 : null,
        });
        return allowed;
    }

    private JsonObject RateLimits(bool full = false)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return new JsonObject
        {
            ["limitId"] = "codex",
            ["primary"] = new JsonObject { ["usedPercent"] = full ? 100 : Math.Min(100, 35 + 3 * _turns), ["windowDurationMins"] = 300, ["resetsAt"] = now + 3600 },
            ["secondary"] = new JsonObject { ["usedPercent"] = 12, ["windowDurationMins"] = 10080, ["resetsAt"] = now + 86400 * 3 },
            ["planType"] = "pro",
            ["rateLimitReachedType"] = full ? "primary" : null,
        };
    }

    private string WriteRollout(string thread, string? cwd)
    {
        var now = DateTimeOffset.UtcNow;
        var folder = Path.Combine(home, "sessions", now.ToString("yyyy", System.Globalization.CultureInfo.InvariantCulture), now.ToString("MM", System.Globalization.CultureInfo.InvariantCulture), now.ToString("dd", System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(home, "deventry-turns"));
        File.WriteAllText(ItemsFile(thread), "");
        var path = Path.Combine(folder, $"rollout-{now:yyyy-MM-ddTHH-mm-ss}-{thread}.jsonl");
        File.WriteAllText(path, new JsonObject
        {
            ["timestamp"] = now.ToString("O"),
            ["type"] = "session_meta",
            ["payload"] = new JsonObject { ["id"] = thread, ["cwd"] = cwd, ["cli_version"] = version },
        }.ToJsonString() + "\n");
        return path;
    }

    /// <summary>A model for "model/list": its own, and the demo's Codex tab's (`DemoCodex.Model`), which the tab resumes with.</summary>
    private static JsonObject Model(string model, string name, bool isDefault) => new()
    {
        ["id"] = model,
        ["model"] = model,
        ["displayName"] = name,
        ["description"] = $"fake-codex's {name}",
        ["hidden"] = false,
        ["isDefault"] = isDefault,
        ["defaultReasoningEffort"] = "medium",
        ["supportedReasoningEfforts"] = new JsonArray(Effort("low"), Effort("medium"), Effort("high")),
    };

    private static JsonObject Effort(string level) => new() { ["reasoningEffort"] = level, ["description"] = level };

    private static JsonObject Usage(long input, long cached, long output) => new()
    {
        ["totalTokens"] = input + output,
        ["inputTokens"] = input,
        ["cachedInputTokens"] = cached,
        ["cacheWriteInputTokens"] = 0,
        ["outputTokens"] = output,
        ["reasoningOutputTokens"] = 0,
    };

    private static string Text(JsonArray input) => string.Join("\n", input.OfType<JsonObject>().Where(i => i["type"]?.GetValue<string>() == "text").Select(i => i["text"]?.GetValue<string>() ?? ""));

    private void Item(Turn turn, string method, JsonObject item)
    {
        Notify(method, new JsonObject { ["threadId"] = _thread, ["turnId"] = turn.Id, ["item"] = item });
        if (method == "item/completed" && _thread is { } thread)
        {
            Keep(thread, turn.Id, item);
        }
    }

    private string ItemsFile(string thread) => Path.Combine(home, "deventry-turns", $"{thread}.jsonl");

    /// <summary>A finished item, for "thread/turns/list", and a prompt or reply in the rollout, for History.</summary>
    private void Keep(string thread, string turnId, JsonObject item)
    {
        var now = DateTimeOffset.UtcNow;
        var kept = item.DeepClone().AsObject();
        kept["startedAtMs"] = now.ToUnixTimeMilliseconds();
        lock (_write)
        {
            File.AppendAllText(ItemsFile(thread), new JsonObject { ["turnId"] = turnId, ["item"] = kept }.ToJsonString() + "\n");
            var (role, text) = item["type"]?.GetValue<string>() switch
            {
                "userMessage" => ("user", Text(item["content"] as JsonArray ?? [])),
                "agentMessage" => ("assistant", item["text"]?.GetValue<string>() ?? ""),
                _ => (null, ""),
            };
            if (role is not null && _rollout is { } rollout)
            {
                File.AppendAllText(rollout, new JsonObject
                {
                    ["timestamp"] = now.ToString("O"),
                    ["type"] = "response_item",
                    ["payload"] = new JsonObject
                    {
                        ["type"] = "message",
                        ["role"] = role,
                        ["content"] = new JsonArray(new JsonObject { ["type"] = role == "user" ? "input_text" : "output_text", ["text"] = text }),
                    },
                }.ToJsonString() + "\n");
            }
        }
    }

    /// <summary>The thread's turns, oldest first, each with its items.</summary>
    private JsonArray TurnsOf(string thread)
    {
        var turns = new JsonArray();
        if (!File.Exists(ItemsFile(thread)))
        {
            return turns;
        }
        JsonObject? current = null;
        foreach (var line in File.ReadLines(ItemsFile(thread)))
        {
            if (JsonNode.Parse(line) is not JsonObject kept)
            {
                continue;
            }
            var turnId = kept["turnId"]?.GetValue<string>();
            if (current is null || current["id"]?.GetValue<string>() != turnId)
            {
                current = new JsonObject { ["id"] = turnId, ["status"] = "completed", ["items"] = new JsonArray() };
                turns.Add(current);
            }
            ((JsonArray)current["items"]!).Add(kept["item"]!.DeepClone());
        }
        return turns;
    }

    private void Notify(string method, JsonObject parameters) => Send(new JsonObject { ["method"] = method, ["params"] = parameters });

    private void Respond(JsonNode id, JsonObject result) => Send(new JsonObject { ["id"] = id.DeepClone(), ["result"] = result });

    private void Error(JsonNode id, int code, string message) => Send(new JsonObject { ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } });

    private void Send(JsonObject message)
    {
        var line = message.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        lock (_write)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }

    private sealed class Turn(string id)
    {
        private readonly CancellationTokenSource _cancel = new();

        public string Id { get; } = id;

        public string? Steered { get; set; }

        public CancellationToken Token => _cancel.Token;

        public void Cancel() => _cancel.Cancel();
    }
}
