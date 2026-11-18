using System.Text.Json.Nodes;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Agents;
using Deventry.Core.History;
using Deventry.Core.Library;
using Deventry.Core.Sessions;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>
/// A Codex session in the session library (DESIGN.md §18, "The session library"): its session file is what's copied,
/// and another machine brings it into its own Codex folder, where Codex resumes it by its id.
/// </summary>
public sealed class CodexLibraryTests
{
    private const string Thread = "01a1eb89-43e5-71e3-8bc3-b1e0d1c53852";

    private static string Meta(string folder) => new JsonObject
    {
        ["timestamp"] = "2026-11-17T09:30:00.000Z",
        ["type"] = "session_meta",
        ["payload"] = new JsonObject { ["id"] = Thread, ["cwd"] = folder, ["cli_version"] = "0.169.0" },
    }.ToJsonString();

    private static string Prompt(string text) => new JsonObject
    {
        ["timestamp"] = "2026-11-17T09:31:00.000Z",
        ["type"] = "response_item",
        ["payload"] = new JsonObject { ["type"] = "message", ["role"] = "user", ["content"] = new JsonArray(new JsonObject { ["type"] = "input_text", ["text"] = text }) },
    }.ToJsonString();

    private static async Task SaveToLibraryAsync(TabTestHarness h, string machine, params string[] lines)
    {
        var source = Path.Combine(h.Root, "library-source.jsonl");
        await File.WriteAllLinesAsync(source, lines, TestContext.Current.CancellationToken);
        await h.Services.Library.Library.SaveAsync(new SessionRecord
        {
            SessionId = Thread,
            Name = "Jump fix",
            Machine = machine,
            LastUsed = h.Time.GetUtcNow().AddHours(-1),
            Folder = h.WorkFolder,
            Tokens = new TokenTotals(),
            Agent = AgentKinds.Codex,
        }, source, subagentsDirectory: null);
    }

    [Fact]
    public async Task A_codex_session_from_another_machine_is_brought_into_codexs_folder_and_resumed_by_its_id()
    {
        await using var h = new TabTestHarness();
        var codexHome = h.Services.CodexHome = Path.Combine(h.Root, "codex-home");
        string[] lines = [Meta(h.WorkFolder), Prompt("Fix the jump")];
        await SaveToLibraryAsync(h, "DESKTOP-01", lines);
        h.OpenHistory();
        var history = h.Shell.History!;
        await TabTestHarness.Eventually(() => !history.IsLoading, "History to load");
        var entry = history.Groups.SelectMany(g => g.Entries).Single();
        Assert.Equal((AgentKinds.Codex, "DESKTOP-01", "Fix the jump"), (entry.Agent, entry.Machine, entry.FirstPrompt));

        await history.OpenCommand.ExecuteAsync(entry);

        var tab = h.Shell.SelectedTab!;
        await TabTestHarness.Eventually(() => h.CodexFactory.Launches.Count == 1 && tab.Status == TabStatus.Idle, "the resume");
        var start = h.CodexFactory.Launches[0].Start;
        Assert.Equal((Thread, false), (start.Resume, start.Fork));
        Assert.True(tab.IsCodex);
        var placed = CodexHistory.Find(codexHome, Thread);
        Assert.NotNull(placed);
        Assert.Equal(lines, await File.ReadAllLinesAsync(placed, TestContext.Current.CancellationToken));
        Assert.Equal(placed, tab.State.TranscriptPath);
        Assert.True(tab.SyncToLibrary);
        Assert.Empty(h.Factory.Launches);
    }

    [Fact]
    public async Task A_codex_tab_that_syncs_copies_its_session_file_and_brings_another_machines_turns_here()
    {
        await using var h = new TabTestHarness();
        var codexHome = h.Services.CodexHome = Path.Combine(h.Root, "codex-home");
        var file = CodexHistory.PlaceFor(codexHome, Thread, h.Time.GetUtcNow());
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllLinesAsync(file, [Meta(h.WorkFolder), Prompt("Fix the jump")], TestContext.Current.CancellationToken);
        // The library's copy has a turn more, from the other machine.
        string[] later = [Meta(h.WorkFolder), Prompt("Fix the jump"), Prompt("And the double jump")];
        await SaveToLibraryAsync(h, "DESKTOP-01", later);
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, SessionId = Thread, Agent = AgentKinds.Codex, SyncToLibrary = true }];
        h.Shell.Restore(null);
        var tab = h.Shell.AllTabs.Single();
        h.Transport.RpcAnswers["thread/turns/list"] = _ => new JsonObject
        {
            ["data"] = new JsonArray(new JsonObject
            {
                ["id"] = "turn-1",
                ["items"] = new JsonArray(new JsonObject { ["type"] = "userMessage", ["id"] = "u1", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "Fix the jump" }) }),
            }),
        };

        tab.ReadEarlierConversation();

        await TabTestHarness.Eventually(() => tab.Status == TabStatus.Idle && tab.Items.OfType<Conversation.NoteItem>().Any(n => n.Text.StartsWith("Picked up where DESKTOP-01 left off", StringComparison.Ordinal)),
            "the other machine's turns");
        Assert.Equal(later, await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken));

        // A turn here copies the session file back, as this machine's.
        await File.AppendAllLinesAsync(file, [Prompt("Now the wall jump")], TestContext.Current.CancellationToken);
        tab.ComposerText = "Now the wall jump";
        await tab.SendCommand.ExecuteAsync(null);
        // After the library is looked at once more, as before any first message.
        await TabTestHarness.Eventually(() => h.Transport.SentRpcMethods.Contains("turn/start"), "the message");
        h.Transport.EmitCodexTurn("Done.", Thread);
        await TabTestHarness.Eventually(() => tab.Items.OfType<Conversation.TurnSummaryItem>().Any(), "the turn to end");
        await TabTestHarness.Eventually(() => h.Services.Library.Library.GetRecord(Thread) is { Machine: var machine } && machine == h.Services.Library.MachineName,
            "the copy", poll: () => h.Time.Advance(TimeSpan.FromSeconds(5)));
        var record = h.Services.Library.Library.GetRecord(Thread)!;
        Assert.Equal(AgentKinds.Codex, record.Agent);
        Assert.Equal(await File.ReadAllLinesAsync(file, TestContext.Current.CancellationToken),
            await File.ReadAllLinesAsync(h.Services.Library.Library.GetTranscriptPath(Thread)!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_codex_session_opened_as_a_copy_forks_its_thread()
    {
        await using var h = new TabTestHarness();
        h.Transport.RpcAnswers["thread/fork"] = _ => new JsonObject { ["thread"] = new JsonObject { ["id"] = "codex-fork-1" }, ["model"] = "gpt-fake-1" };
        h.Services.State.Tabs = [new TabState { Folder = h.WorkFolder, SessionId = Thread, Agent = AgentKinds.Codex, ForkOnNextStart = true }];
        h.Shell.Restore(null);
        var tab = h.Shell.AllTabs.Single();

        await tab.EnsureStartedAsync();

        var start = Assert.Single(h.CodexFactory.Launches).Start;
        Assert.Equal((Thread, true), (start.Resume, start.Fork));
        Assert.Contains("thread/fork", h.Transport.SentRpcMethods);
        Assert.DoesNotContain("thread/resume", h.Transport.SentRpcMethods);
        Assert.Equal("codex-fork-1", tab.State.SessionId);
        Assert.False(tab.State.ForkOnNextStart);
    }
}
