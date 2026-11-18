using System.Globalization;
using System.Text.Json.Nodes;
using Deventry.Core.Agents;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;

namespace Deventry.Core.Demo;

/// <summary>
/// The demo's Codex tab (DESIGN.md §18, "Codex, a second agent"; §11, "Demo mode"): its conversation as Codex keeps it,
/// which the stand-in (<see cref="StandInCodex"/>, or <c>fake-codex</c> for the screenshots) reads back when the tab
/// starts; its session file and name, for History; and Codex's usage, for the header while it's selected.
/// </summary>
public static class DemoCodex
{
    /// <summary>
    /// In <c>CODEX_HOME</c>: each thread's finished items, a <c>{"turnId", "item"}</c> line each, which a stand-in Codex
    /// reads back for <c>thread/turns/list</c> and keeps adding to.
    /// </summary>
    public const string TurnsFolder = "deventry-turns";

    /// <summary>The demo tab's thread.</summary>
    public const string Thread = "019a5d2e-6f1b-7c3a-9d4e-2b8f1c0a7e55";

    public const string TabId = "tab-codex";

    public const string Name = "Load older inventory saves";

    /// <summary>The model the demo's Codex says it runs.</summary>
    public const string Model = "gpt-5.5-codex";

    public const string ModelName = "GPT-5.5-Codex";

    /// <summary>
    /// What the demo's Codex offers (<c>model/list</c>), which the demo's state remembers as a Codex that ran before would
    /// have left it (<see cref="AppState.KnownCodexModels"/>), so the tab's row names its model before the tab starts.
    /// </summary>
    public static ModelInfo Offered { get; } = new(Model, Model, ModelName, "The demo's stand-in for Codex", true, ["low", "medium", "high"])
    {
        IsDefault = true,
        DefaultEffort = "medium",
    };

    /// <summary>The file a thread's items are kept in.</summary>
    public static string TurnsFile(string codexHome, string thread) => Path.Combine(codexHome, TurnsFolder, $"{thread}.jsonl");

    /// <summary>
    /// Writes the tab's conversation, session file and name into <paramref name="codexHome"/>, its turns an hour or two
    /// before <paramref name="now"/>, about the project in <paramref name="folder"/>; returns the tab.
    /// </summary>
    public static TabState Write(string codexHome, string folder, DateTimeOffset now)
    {
        var started = now.AddMinutes(-140);
        var save = Path.Combine(folder, "Assets", "Scripts", "Inventory", "InventorySave.cs");
        var load = Path.Combine(folder, "Assets", "Scripts", "Inventory", "InventoryLoad.cs");
        const string Review = "Review the inventory save code for anything that would break a player's older save files.";
        const string Change = "Add loading, and read saves from before the version field without losing their items.";
        var turns = new (string Turn, DateTimeOffset At, JsonObject Item)[]
        {
            ("turn-review", started, UserMessage("u-review", Review)),
            ("turn-review", started.AddSeconds(4), Reasoning("r-review", "Checking how InventorySave writes its data, and whether anything reads it back with a version to go by.")),
            ("turn-review", started.AddSeconds(9), Command("c-read", $"cat {Quote(save)}", "read", save, InventorySaveExcerpt)),
            ("turn-review", started.AddSeconds(14), Command("c-search", "rg -n \"SaveVersion|FromJson\" Assets", "search", null, "")),
            ("turn-review", started.AddSeconds(22), AgentMessage("a-review", """
                The save code is short, and two things would break older saves:

                - **Nothing reads the file back yet.** `InventorySave` only writes `inventory.json`; there's no `Load`, so there's nothing to keep working.
                - **The file has no version.** `JsonUtility.ToJson` writes the fields as they are now. Rename or add a field, and an older file reads with the new field empty, silently.

                I'd add a `SaveVersion` field, written with every save, and a loader that treats a file without one as version 1.
                """)),
            ("turn-change", started.AddMinutes(6), UserMessage("u-change", Change)),
            ("turn-change", started.AddMinutes(6).AddSeconds(5), Reasoning("r-change", "A new InventoryLoad keeps reading apart from writing. A missing SaveVersion reads as 0 from JsonUtility, which I'll take as the first format.")),
            ("turn-change", started.AddMinutes(6).AddSeconds(18), FileChange("p-load", load, InventoryLoad)),
            ("turn-change", started.AddMinutes(6).AddSeconds(26), AgentMessage("a-change", """
                Added `InventoryLoad.cs`:

                - `Load()` returns an empty inventory when there's no save yet.
                - A file without `SaveVersion` reads as **version 1**, the format saves have now, so older saves keep their items.
                - Anything newer than this build knows is left as it is, with a warning in the log, rather than overwritten.

                `InventorySave` should write `SaveVersion = InventoryLoad.CurrentVersion` too; say if you'd like that next.
                """)),
        };

        var items = TurnsFile(codexHome, Thread);
        Directory.CreateDirectory(Path.GetDirectoryName(items)!);
        File.WriteAllLines(items, turns.Select(t =>
        {
            t.Item["startedAtMs"] = t.At.ToUnixTimeMilliseconds();
            return new JsonObject { ["turnId"] = t.Turn, ["item"] = t.Item }.ToJsonString();
        }));
        WriteSessionFile(codexHome, folder, started, [(started, "user", Review), (started.AddMinutes(1), "assistant", "The save code is short, and two things would break older saves."),
            (started.AddMinutes(6), "user", Change), (started.AddMinutes(7), "assistant", "Added InventoryLoad.cs.")]);
        File.AppendAllText(Path.Combine(codexHome, "session_index.jsonl"), new JsonObject { ["id"] = Thread, ["thread_name"] = Name }.ToJsonString() + "\n");

        return new TabState
        {
            Id = TabId,
            Folder = folder,
            Agent = AgentKinds.Codex,
            SessionId = Thread,
            UserName = Name,
            Overrides = new TabOverrides { Model = Model },
            SessionStartedAt = started,
        };
    }

    /// <summary>
    /// Codex's usage as the stand-in reports it after <paramref name="turns"/> turns of its own: the five-hour window part
    /// used and climbing with each turn, the week less so (<c>account/rateLimits/updated</c>'s <c>rateLimits</c>).
    /// </summary>
    public static JsonObject RateLimits(DateTimeOffset now, int turns = 0) => new()
    {
        ["limitId"] = "codex",
        ["primary"] = new JsonObject
        {
            ["usedPercent"] = Math.Min(100, 41 + 3 * turns),
            ["windowDurationMins"] = 300,
            ["resetsAt"] = now.AddMinutes(137).ToUnixTimeSeconds(),
        },
        ["secondary"] = new JsonObject
        {
            ["usedPercent"] = Math.Min(100, 18 + turns),
            ["windowDurationMins"] = 10080,
            ["resetsAt"] = now.AddDays(4).AddHours(6).ToUnixTimeSeconds(),
        },
        ["planType"] = "pro",
        ["rateLimitReachedType"] = null,
    };

    private static void WriteSessionFile(string codexHome, string folder, DateTimeOffset started, (DateTimeOffset At, string Role, string Text)[] messages)
    {
        var local = started.ToLocalTime();
        var day = Path.Combine(codexHome, "sessions", local.ToString("yyyy", CultureInfo.InvariantCulture), local.ToString("MM", CultureInfo.InvariantCulture), local.ToString("dd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(day);
        var lines = new List<string>
        {
            new JsonObject
            {
                ["timestamp"] = started.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                ["type"] = "session_meta",
                ["payload"] = new JsonObject { ["id"] = Thread, ["cwd"] = folder, ["cli_version"] = StandInCodex.Version },
            }.ToJsonString(),
        };
        lines.AddRange(messages.Select(m => new JsonObject
        {
            ["timestamp"] = m.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["type"] = "response_item",
            ["payload"] = new JsonObject
            {
                ["type"] = "message",
                ["role"] = m.Role,
                ["content"] = new JsonArray(new JsonObject { ["type"] = m.Role == "user" ? "input_text" : "output_text", ["text"] = m.Text }),
            },
        }.ToJsonString()));
        File.WriteAllLines(Path.Combine(day, $"rollout-{local.ToString("yyyy-MM-dd'T'HH-mm-ss", CultureInfo.InvariantCulture)}-{Thread}.jsonl"), lines);
    }

    private static string Quote(string path) => path.Contains(' ', StringComparison.Ordinal) ? $"\"{path}\"" : path;

    private static JsonObject UserMessage(string id, string text) => new()
    {
        ["type"] = "userMessage",
        ["id"] = id,
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
    };

    private static JsonObject Reasoning(string id, string summary) => new() { ["type"] = "reasoning", ["id"] = id, ["summary"] = new JsonArray(summary) };

    private static JsonObject AgentMessage(string id, string text) => new() { ["type"] = "agentMessage", ["id"] = id, ["text"] = text.Trim() };

    private static JsonObject Command(string id, string command, string kind, string? path, string output) => new()
    {
        ["type"] = "commandExecution",
        ["id"] = id,
        ["command"] = command,
        ["commandActions"] = new JsonArray(path is null
            ? new JsonObject { ["type"] = kind, ["command"] = command, ["query"] = "SaveVersion|FromJson", ["path"] = "Assets" }
            : new JsonObject { ["type"] = kind, ["command"] = command, ["name"] = Path.GetFileName(path), ["path"] = path }),
        ["status"] = "completed",
        ["aggregatedOutput"] = output,
        ["exitCode"] = 0,
    };

    private static JsonObject FileChange(string id, string path, string content) => new()
    {
        ["type"] = "fileChange",
        ["id"] = id,
        ["status"] = "completed",
        ["changes"] = new JsonArray(new JsonObject { ["path"] = path, ["kind"] = new JsonObject { ["type"] = "add" }, ["diff"] = content }),
    };

    private const string InventorySaveExcerpt = """
        using System.IO;
        using UnityEngine;

        namespace Tidepool.Inventory
        {
            public static class InventorySave
            {
                private static string SavePath => Path.Combine(Application.persistentDataPath, "inventory.json");

                public static void Save(Inventory inventory)
                {
                    var json = JsonUtility.ToJson(inventory.ToData());
                    File.WriteAllText(SavePath, json);
                }
            }
        }

        """;

    private const string InventoryLoad = """
        using System.IO;
        using UnityEngine;

        namespace Tidepool.Inventory
        {
            public static class InventoryLoad
            {
                /// <summary>The save format this build writes. A file without a version is the first.</summary>
                public const int CurrentVersion = 2;

                private static string SavePath => Path.Combine(Application.persistentDataPath, "inventory.json");

                public static Inventory Load()
                {
                    if (!File.Exists(SavePath))
                    {
                        return new Inventory();
                    }
                    var data = JsonUtility.FromJson<InventoryData>(File.ReadAllText(SavePath));
                    // JsonUtility leaves a missing field at its default: a save from before the version field reads as 0.
                    var version = data.SaveVersion == 0 ? 1 : data.SaveVersion;
                    if (version > CurrentVersion)
                    {
                        Debug.LogWarning($"inventory.json is from a newer build (version {version}); leaving it as it is.");
                        return new Inventory();
                    }
                    return Inventory.FromData(data);
                }
            }
        }

        """;
}
