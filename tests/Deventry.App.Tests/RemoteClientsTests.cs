using System.Text.Json.Nodes;
using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.Core.Remote;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>
/// The remote clients' desk over the tabs (DESIGN.md §18, "Remote clients"), with a fake client: what it hears, what
/// it can do to a tab, what each level refuses, when it sends messages, and keeping the computer awake.
/// </summary>
public class RemoteClientsTests
{
    private const string BashRequest = """
        {"type":"control_request","request_id":"p1","request":{"subtype":"can_use_tool","tool_name":"Bash","tool_use_id":"t1","input":{"command":"npm test"}}}
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Remote : IAsyncDisposable
    {
        public required TabTestHarness H { get; init; }

        public required FakeRemoteClient Client { get; init; }

        public required TabViewModel Tab { get; init; }

        public IRemoteDesk Desk => Client.Desk!;

        public RemoteClientsService Service => H.Services.RemoteClients;

        public ValueTask DisposeAsync() => H.DisposeAsync();
    }

    private static async Task<Remote> StartAsync(Action<AppSettings>? configure = null)
    {
        var h = new TabTestHarness(settings =>
        {
            settings.RemoteClients.SetEnabled("telegram", true);
            configure?.Invoke(settings);
        });
        var client = new FakeRemoteClient();
        h.Services.RemoteClients.Credentials = new FakeCredentialStore { Secrets = { [h.Services.RemoteClients.CredentialKeyOf("telegram")] = "123456789:token" } };
        h.Services.RemoteClients.ClientFactory = (_, _, _) => client;
        var tab = await h.OpenTabAsync();
        h.OnUiThread(() => h.Services.RemoteClients.Start(h.Shell));
        await TabTestHarness.Eventually(() => client.Desk is not null && h.Services.RemoteClients.IsActive, "the client to start");
        return new Remote { H = h, Client = client, Tab = tab };
    }

    private static async Task<RemotePromptWaiting> PromptAsync(Remote remote)
    {
        remote.H.Transport.Emit(BashRequest);
        await TabTestHarness.Eventually(() => remote.Client.Of<RemotePromptWaiting>().Count > 0, "the prompt");
        return remote.Client.Of<RemotePromptWaiting>()[^1];
    }

    // ---- Prompts ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_waiting_prompt_reaches_the_client_with_the_cards_answers()
    {
        await using var remote = await StartAsync();

        var waiting = await PromptAsync(remote);

        Assert.Equal(remote.Tab.Id, waiting.Tab.Id);
        Assert.Equal(RemoteTabStatus.NeedsInput, waiting.Tab.Status);
        Assert.Equal("p1", waiting.Prompt.Id);
        Assert.Equal(RemotePromptKind.Command, waiting.Prompt.Kind);
        Assert.Equal("Allow this command?", waiting.Prompt.Title);
        Assert.Equal("npm test", waiting.Prompt.About);
        Assert.Equal(["Allow", "Deny", "Deny with a message…"], waiting.Prompt.Options.Select(o => o.Label));
        Assert.Equal(waiting.Prompt.Id, waiting.Tab.Prompt?.Id);
    }

    [Fact]
    public async Task Answered_from_the_client_the_card_closes_and_says_so()
    {
        await using var remote = await StartAsync();
        await PromptAsync(remote);

        var result = await remote.Desk.AnswerAsync(remote.Tab.Id, "p1", RemotePrompt.OptionIds.Allow, null, "Telegram", Ct);

        Assert.True(result.Done);
        var card = InlineDispatcher.Read(() => remote.Tab.Items.OfType<PermissionItem>().Single());
        Assert.Equal(PermissionState.Allowed, card.State);
        Assert.Equal("Allowed · Answered from Telegram", card.Outcome);
        var closed = Assert.Single(remote.Client.Of<RemotePromptClosed>());
        Assert.Equal((RemotePromptEnd.AnsweredFromClient, "Allowed", "Telegram"), (closed.How, closed.Outcome, closed.Client));
    }

    [Fact]
    public async Task Deny_with_a_message_from_the_client_tells_Claude_the_words()
    {
        await using var remote = await StartAsync();
        await PromptAsync(remote);

        await remote.Desk.AnswerAsync(remote.Tab.Id, "p1", RemotePrompt.OptionIds.DenyWithMessage, "Use the staging database", "Telegram", Ct);

        var card = InlineDispatcher.Read(() => remote.Tab.Items.OfType<PermissionItem>().Single());
        Assert.Equal(PermissionState.Denied, card.State);
        Assert.Equal("Denied: Use the staging database · Answered from Telegram", card.Outcome);
    }

    [Fact]
    public async Task Answered_at_the_desk_the_client_hears_it_closed_there()
    {
        await using var remote = await StartAsync();
        await PromptAsync(remote);

        remote.H.OnUiThread(() => remote.Tab.Items.OfType<PermissionItem>().Single().DenyCommand.Execute(null));

        var closed = Assert.Single(remote.Client.Of<RemotePromptClosed>());
        Assert.Equal((RemotePromptEnd.AnsweredAtDesk, "Denied", null), (closed.How, closed.Outcome, closed.Client));
        var late = await remote.Desk.AnswerAsync(remote.Tab.Id, "p1", RemotePrompt.OptionIds.Allow, null, "Telegram", Ct);
        Assert.Equal(RemoteResult.No("Already answered at the desk."), late);
    }

    [Fact]
    public async Task A_question_in_parts_is_asked_a_part_at_a_time()
    {
        await using var remote = await StartAsync();
        remote.H.Transport.Emit("""
            {"type":"control_request","request_id":"q1","request":{"subtype":"can_use_tool","tool_name":"AskUserQuestion","input":{"questions":[
              {"question":"Which database?","header":"DB","options":[{"label":"Postgres"},{"label":"SQLite"}],"multiSelect":false},
              {"question":"Which port?","header":"Port","options":[{"label":"5432"}],"multiSelect":false}]}}}
            """);
        await TabTestHarness.Eventually(() => remote.Client.Of<RemotePromptWaiting>().Count == 1, "the first part");
        var first = remote.Client.Of<RemotePromptWaiting>()[0].Prompt;
        Assert.Equal(("q1#0", "Which database?", 1, 2), (first.Id, first.About, first.Part, first.Parts));
        Assert.Equal(["Postgres", "SQLite", "Answer in my own words"], first.Options.Select(o => o.Label));

        await remote.Desk.AnswerAsync(remote.Tab.Id, "q1#0", RemotePrompt.OptionIds.Choice(1), null, "Telegram", Ct);
        var second = remote.Client.Of<RemotePromptWaiting>()[^1].Prompt;
        Assert.Equal(("q1#1", "Which port?"), (second.Id, second.About));
        await remote.Desk.AnswerAsync(remote.Tab.Id, "q1#1", RemotePrompt.OptionIds.OwnWords, "6543", "Telegram", Ct);

        var card = InlineDispatcher.Read(() => remote.Tab.Items.OfType<QuestionItem>().Single());
        Assert.Equal(PermissionState.Allowed, card.State);
        Assert.StartsWith("DB: SQLite · Port: 6543", card.Outcome);
    }

    // ---- Messages ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_message_from_the_client_goes_to_the_tab_and_leaves_the_composer_alone()
    {
        await using var remote = await StartAsync();
        remote.H.OnUiThread(() => remote.Tab.ComposerText = "half typed");

        var result = await remote.Desk.SendAsync(remote.Tab.Id, "Fix the rope physics", [], "Telegram", Ct);

        Assert.True(result.Done);
        await TabTestHarness.Eventually(() => remote.H.Transport.SentUserTexts.Contains("Fix the rope physics"), "the message");
        Assert.Equal("half typed", InlineDispatcher.Read(() => remote.Tab.ComposerText));
        var card = InlineDispatcher.Read(() => remote.Tab.Items.OfType<UserMessageItem>().Last());
        Assert.Equal("You, from Telegram", card.CardLabel);
    }

    [Fact]
    public async Task Images_and_files_from_the_client_go_with_the_message_as_the_composers_do()
    {
        await using var remote = await StartAsync();
        RemoteAttachment[] files =
        [
            new("shot.png", TestImages.Png),
            new("notes.txt", "The rope snaps at 40 m."u8.ToArray()),
            new("spec.pdf", "%PDF-1.7 the spec"u8.ToArray()),
            new("tool.exe", [0x4D, 0x5A, 0, 1]),
        ];

        var result = await remote.Desk.SendAsync(remote.Tab.Id, "What's wrong here?", files, "Telegram", Ct);

        // Sent, saying what couldn't go.
        Assert.True(result.Done);
        Assert.Equal("Not sent: tool.exe: Claude can't read it. Images, text files and PDFs can be sent.", result.Reason);
        await TabTestHarness.Eventually(() => remote.H.Transport.Sent.Any(m => (string?)m["type"] == "user" && m["message"]?["content"] is JsonArray), "the message");
        var blocks = remote.H.Transport.Sent.Last(m => (string?)m["type"] == "user")["message"]!["content"]!.AsArray();
        Assert.Equal("image", (string?)blocks[0]!["type"]);
        var text = string.Join("\n", blocks.Skip(1).Select(b => (string?)b!["text"]));
        Assert.StartsWith("What's wrong here?\n\n@.deventry/attachments/", text, StringComparison.Ordinal);
        Assert.Contains("-spec.pdf", text, StringComparison.Ordinal);
        Assert.Contains("Attached file notes.txt:\n\nThe rope snaps at 40 m.", text, StringComparison.Ordinal);
        // The PDF is in the project, which git doesn't see.
        var saved = Path.Combine(remote.H.WorkFolder, ".deventry", "attachments");
        Assert.EndsWith("-spec.pdf", Assert.Single(Directory.GetFiles(saved)), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(remote.H.WorkFolder, ".deventry", ".gitignore")));
        var card = InlineDispatcher.Read(() => remote.Tab.Items.OfType<UserMessageItem>().Last());
        Assert.Single(card.Images);
    }

    [Fact]
    public async Task Only_files_Claude_cant_read_and_no_words_send_nothing()
    {
        await using var remote = await StartAsync();

        var result = await remote.Desk.SendAsync(remote.Tab.Id, "", [new("tool.exe", [0x4D, 0x5A, 0, 1])], "Telegram", Ct);

        Assert.Equal(RemoteResult.No("tool.exe: Claude can't read it. Images, text files and PDFs can be sent."), result);
        Assert.DoesNotContain(remote.H.Transport.Sent, m => (string?)m["type"] == "user");
    }

    [Fact]
    public async Task Stopping_from_the_client_stops_the_tab()
    {
        await using var remote = await StartAsync();
        await remote.Desk.SendAsync(remote.Tab.Id, "Work a while", [], "Telegram", Ct);
        await TabTestHarness.Eventually(() => remote.Tab.IsWorking, "the turn");

        var result = await remote.Desk.StopAsync(remote.Tab.Id, "Telegram", Ct);

        Assert.True(result.Done);
        await TabTestHarness.Eventually(() => remote.H.Transport.SentControlSubtypes.Contains("interrupt"), "the interrupt");
    }

    [Fact]
    public async Task Stopping_a_tab_that_is_not_working_says_so()
    {
        await using var remote = await StartAsync();

        Assert.Equal(RemoteResult.No($"{remote.Tab.DisplayName} isn't working."), await remote.Desk.StopAsync(remote.Tab.Id, "Telegram", Ct));
    }

    // ---- Keeping a tab from them ------------------------------------------------------------------------------------

    private static void ToggleKeep(Remote remote) => remote.H.OnUiThread(() => remote.Tab.ToggleKeepFromRemoteClientsCommand.Execute(null));

    [Fact]
    public async Task A_tab_kept_from_remote_clients_is_gone_from_them_until_it_is_let_go()
    {
        await using var remote = await StartAsync();
        var waiting = await PromptAsync(remote);

        ToggleKeep(remote);

        await TabTestHarness.Eventually(() => remote.Client.Of<RemoteTabClosed>().Count == 1, "the tab to go");
        Assert.True(remote.Client.Of<RemoteTabClosed>()[0].Kept);
        Assert.True(InlineDispatcher.Read(() => remote.Tab.KeepsFromRemoteClients && remote.Tab.State.KeepFromRemoteClients));
        Assert.Empty(await remote.Desk.GetTabsAsync(Ct));
        Assert.Equal(RemoteResult.No(RemoteMessages.TabKept),
            await remote.Desk.AnswerAsync(remote.Tab.Id, waiting.Prompt.Id, RemotePrompt.OptionIds.Allow, null, "Telegram", Ct));
        Assert.Equal(RemoteResult.No(RemoteMessages.TabKept), await remote.Desk.SendAsync(remote.Tab.Id, "hello", [], "Telegram", Ct));
        Assert.Null(await remote.Desk.GetLastReplyAsync(remote.Tab.Id, Ct));

        // Let go, it's back, with the prompt still waiting there.
        ToggleKeep(remote);

        await TabTestHarness.Eventually(() => remote.Client.Of<RemotePromptWaiting>().Count == 2, "the prompt again");
        Assert.Equal(waiting.Prompt.Id, remote.Client.Of<RemotePromptWaiting>()[1].Prompt.Id);
        Assert.Single(await remote.Desk.GetTabsAsync(Ct));
    }

    [Fact]
    public async Task A_kept_tabs_turns_reach_no_client()
    {
        await using var remote = await StartAsync();
        ToggleKeep(remote);

        remote.H.Transport.EmitTurn("A private turn.");
        await TabTestHarness.Eventually(() => remote.Tab.Items.OfType<TurnSummaryItem>().Count() == 1 && remote.Tab.IsSettled, "the kept turn's end");
        ToggleKeep(remote);
        remote.H.Transport.EmitTurn("A turn to share.");

        // The clients hear of the second turn; the first would have come before it.
        await TabTestHarness.Eventually(() => remote.Client.Of<RemoteTurnFinished>().Count == 1, "the second turn's end");
        Assert.Equal("A turn to share.", remote.Client.Of<RemoteTurnFinished>()[0].Reply);
    }

    [Fact]
    public async Task A_closed_tab_is_gone_for_the_client()
    {
        await using var remote = await StartAsync();

        Assert.Equal(RemoteResult.No(RemoteMessages.TabGone), await remote.Desk.SendAsync("no-such-tab", "hello", [], "Telegram", Ct));
    }

    [Theory]
    [InlineData(RemoteAccessLevel.Watch, false, false)]
    [InlineData(RemoteAccessLevel.Answer, true, false)]
    [InlineData(RemoteAccessLevel.Full, true, true)]
    public async Task Each_level_allows_only_its_own(RemoteAccessLevel level, bool canAnswer, bool canSend)
    {
        await using var remote = await StartAsync(s => s.RemoteClients.Access = level);
        await PromptAsync(remote);

        var sent = await remote.Desk.SendAsync(remote.Tab.Id, "hello", [], "Telegram", Ct);
        var answered = await remote.Desk.AnswerAsync(remote.Tab.Id, "p1", RemotePrompt.OptionIds.Allow, null, "Telegram", Ct);
        var opened = await remote.Desk.OpenTabAsync(remote.H.WorkFolder, "Telegram", Ct);

        Assert.Equal(canSend, sent.Done);
        Assert.Equal(canAnswer, answered.Done);
        Assert.False(opened.Done);
        if (!canAnswer)
        {
            Assert.Equal(RemoteAccess.Refusal(RemoteAction.Answer), answered.Reason);
        }
    }

    [Fact]
    public async Task Open_new_tabs_opens_one_in_a_recent_folder_only()
    {
        await using var remote = await StartAsync(s => s.RemoteClients.OpenTabs = true);

        var elsewhere = await remote.Desk.OpenTabAsync(Path.GetTempPath(), "Telegram", Ct);
        var recent = await remote.Desk.OpenTabAsync(remote.H.WorkFolder, "Telegram", Ct);

        Assert.False(elsewhere.Done);
        Assert.True(recent.Done);
        Assert.Equal(2, InlineDispatcher.Read(() => remote.H.Shell.AllTabs.Count()));
        Assert.Contains(recent.TabId, InlineDispatcher.Read(() => remote.H.Shell.AllTabs.Select(t => t.Id).ToList()));
    }

    // ---- When messages go -------------------------------------------------------------------------------------------

    [Fact]
    public async Task At_the_desk_nothing_is_sent_and_going_away_sends_what_waits()
    {
        await using var remote = await StartAsync();
        remote.H.OnUiThread(() => remote.H.Services.Notifications.SetAppActive(true));

        var waiting = await PromptAsync(remote);
        Assert.Equal(RemoteNotice.None, waiting.Notice);

        remote.H.OnUiThread(() => remote.H.Services.Notifications.SetAppActive(false));

        var again = remote.Client.Of<RemotePromptWaiting>()[^1];
        Assert.True(again.Again);
        Assert.Equal(RemoteNotice.Sound, again.Notice);
        Assert.Equal("p1", again.Prompt.Id);
    }

    [Fact]
    public async Task Quiet_ending_tells_the_prompts_still_waiting_again()
    {
        await using var remote = await StartAsync();
        remote.H.OnUiThread(() => remote.Service.SetQuiet(remote.H.Time.GetUtcNow() + TimeSpan.FromHours(1)));
        var waiting = await PromptAsync(remote);

        remote.H.Time.Advance(TimeSpan.FromHours(1));

        await TabTestHarness.Eventually(() => remote.Client.Of<RemotePromptWaiting>().Count == 2, "the prompt told again");
        var again = remote.Client.Of<RemotePromptWaiting>()[^1];
        Assert.True(again.Again);
        Assert.Equal(RemoteNotice.Sound, again.Notice);
        Assert.Equal(waiting.Prompt.Id, again.Prompt.Id);
    }

    [Fact]
    public async Task Quiet_turned_off_tells_the_prompts_still_waiting_again_once()
    {
        await using var remote = await StartAsync();
        remote.H.OnUiThread(() => remote.Service.SetQuiet(remote.H.Time.GetUtcNow() + TimeSpan.FromHours(1)));
        await PromptAsync(remote);

        remote.H.OnUiThread(() => remote.Service.SetQuiet(null));
        Assert.True(remote.Client.Of<RemotePromptWaiting>()[^1].Again);
        // Off already, and the hour's end passing, tell nothing more.
        remote.H.OnUiThread(() => remote.Service.SetQuiet(null));
        remote.H.Time.Advance(TimeSpan.FromHours(1));

        Assert.Equal(2, remote.Client.Of<RemotePromptWaiting>().Count);
    }

    [Fact]
    public async Task Quiet_made_longer_tells_nothing_at_its_first_end()
    {
        await using var remote = await StartAsync();
        remote.H.OnUiThread(() => remote.Service.SetQuiet(remote.H.Time.GetUtcNow() + TimeSpan.FromMinutes(30)));
        await PromptAsync(remote);

        remote.H.OnUiThread(() => remote.Service.SetQuiet(remote.H.Time.GetUtcNow() + TimeSpan.FromHours(1)));
        remote.H.Time.Advance(TimeSpan.FromMinutes(30));
        Assert.Single(remote.Client.Of<RemotePromptWaiting>());

        remote.H.Time.Advance(TimeSpan.FromMinutes(30));
        await TabTestHarness.Eventually(() => remote.Client.Of<RemotePromptWaiting>().Count == 2, "the prompt told again at the new end");
    }

    [Fact]
    public async Task Always_sends_silently_at_the_desk()
    {
        await using var remote = await StartAsync(s => s.RemoteClients.When = RemoteSendWhen.Always);
        remote.H.OnUiThread(() => remote.H.Services.Notifications.SetAppActive(true));

        Assert.Equal(RemoteNotice.Silent, (await PromptAsync(remote)).Notice);
    }

    [Theory]
    [InlineData(true, false, false, 0)]
    [InlineData(false, true, false, 0)]
    [InlineData(false, false, true, 0)]
    [InlineData(false, false, false, 6)]
    public async Task Locked_the_screen_saver_asleep_or_idle_is_away_with_Deventry_in_front(bool locked, bool screenSaver, bool asleep, int idleMinutes)
    {
        await using var remote = await StartAsync();
        remote.H.OnUiThread(() => remote.H.Services.Notifications.SetAppActive(true));
        await remote.Service.ReadPresenceAsync();
        Assert.False(remote.Service.IsAway);

        remote.H.Presence.Reading = new PresenceReading(locked, screenSaver, asleep, TimeSpan.FromMinutes(idleMinutes));
        await remote.Service.ReadPresenceAsync();

        Assert.True(remote.Service.IsAway);
        Assert.Equal(RemoteNotice.Sound, (await PromptAsync(remote)).Notice);
    }

    [Fact]
    public async Task Idle_counts_only_past_the_minutes_set()
    {
        await using var remote = await StartAsync(s => s.RemoteClients.AwayAfterMinutes = 10);
        remote.H.OnUiThread(() => remote.H.Services.Notifications.SetAppActive(true));

        remote.H.Presence.Reading = new PresenceReading(false, false, false, TimeSpan.FromMinutes(9));
        await remote.Service.ReadPresenceAsync();

        Assert.False(remote.Service.IsAway);
    }

    [Fact]
    public async Task An_event_the_user_turned_off_is_not_sent()
    {
        await using var remote = await StartAsync(s => s.RemoteClients.NeedsInput = false);

        Assert.Equal(RemoteNotice.None, (await PromptAsync(remote)).Notice);
    }

    // ---- Turns, errors and closing ----------------------------------------------------------------------------------

    [Fact]
    public async Task A_finished_turn_reaches_the_client_with_its_reply()
    {
        await using var remote = await StartAsync();

        remote.H.Transport.EmitTurn("The rope swings now.\nDetails follow.");

        await TabTestHarness.Eventually(() => remote.Client.Of<RemoteTurnFinished>().Count == 1, "the turn's end");
        var turn = remote.Client.Of<RemoteTurnFinished>()[0];
        Assert.Equal("The rope swings now.\nDetails follow.", turn.Reply);
        Assert.Equal(RemoteNotice.Sound, turn.Notice);
        Assert.Equal("The rope swings now.\nDetails follow.", (await remote.Desk.GetLastReplyAsync(remote.Tab.Id, Ct))?.Markdown);
    }

    /// <summary>
    /// A turn that reads an image that was there before, writes a chart and takes a screenshot, then ends: one
    /// <c>system/init</c> at its start, as Claude Code sends.
    /// </summary>
    private static void EmitTurnWithImages(Remote remote)
    {
        var h = remote.H;
        var before = WriteFile(h, "shots/before.png", TestImages.Png);
        File.SetLastWriteTimeUtc(before, h.Time.GetUtcNow().UtcDateTime.AddDays(-1));
        h.Transport.Emit(Wire.Init());
        var chart = WriteFile(h, "out/chart.png", TestImages.Png);
        File.SetLastWriteTimeUtc(chart, h.Time.GetUtcNow().UtcDateTime.AddMinutes(1));
        h.Transport.Emit(Wire.Tool(null, "t1", "Read", new JsonObject { ["file_path"] = before }));
        h.Transport.Emit(Wire.Result("t1", "Read the image."));
        h.Transport.Emit(Wire.Tool(null, "t2", "Write", new JsonObject { ["file_path"] = chart, ["content"] = "" }));
        h.Transport.Emit(Wire.Result("t2", "File created"));
        h.Transport.Emit(Wire.Tool(null, "t3", "mcp__playwright__browser_take_screenshot", []));
        h.Transport.Emit(MediaTests.ImageResult("t3", parent: null));
        h.Transport.Emit(Wire.Text(null, "The chart is in `out/chart.png`, beside `shots/before.png`."));
        h.Transport.Emit(new JsonObject { ["type"] = "result", ["subtype"] = "success", ["is_error"] = false, ["result"] = "The chart is drawn.", ["session_id"] = "s1" });
    }

    private static string WriteFile(TabTestHarness h, string relative, byte[] data)
    {
        var path = Path.Combine(h.WorkFolder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    [Fact]
    public async Task A_turns_images_reach_a_client_that_sends_them_but_not_one_it_only_read()
    {
        await using var remote = await StartAsync(s => s.RemoteClients.SetSendImages("telegram", true));
        Assert.True(remote.Desk.SendsImages("telegram"));

        EmitTurnWithImages(remote);

        await TabTestHarness.Eventually(() => remote.Client.Of<RemoteTurnFinished>().Count == 1, "the turn's end");
        var turn = remote.Client.Of<RemoteTurnFinished>()[0];
        // The screenshot, and the chart it wrote, which the reply named last; not the image it read, which the reply named too.
        Assert.Equal(["Image.png", "chart.png"], turn.Images.Select(i => i.Name));
        Assert.All(turn.Images, i =>
        {
            Assert.Equal("image/png", i.MediaType);
            Assert.Equal(TestImages.Png, i.Data);
        });
        Assert.Equal(0, turn.ImagesLeftOut);
    }

    [Fact]
    public async Task The_images_Claude_only_read_come_too_marked_when_a_client_sends_those()
    {
        await using var remote = await StartAsync(s => s.RemoteClients.SetSendReadImages("telegram", true));
        Assert.True(remote.Desk.SendsReadImages("telegram"));
        Assert.False(remote.Desk.SendsImages("telegram"));

        EmitTurnWithImages(remote);

        await TabTestHarness.Eventually(() => remote.Client.Of<RemoteTurnFinished>().Count == 1, "the turn's end");
        var turn = remote.Client.Of<RemoteTurnFinished>()[0];
        // All three, as the Media window has them: the image it read, which was there a day before the turn, marked.
        Assert.Equal([("Image.png", false), ("before.png", true), ("chart.png", false)], turn.Images.Select(i => (i.Name, i.WasRead)));
    }

    [Fact]
    public async Task No_images_are_read_while_no_client_sends_them_or_the_messages_say_just_what_happened()
    {
        await using var remote = await StartAsync(s =>
        {
            s.RemoteClients.SetSendImages("telegram", true);
            s.RemoteClients.Detail = RemoteDetail.JustWhatHappened;
        });
        Assert.False(remote.Desk.SendsImages("telegram"));

        EmitTurnWithImages(remote);

        await TabTestHarness.Eventually(() => remote.Client.Of<RemoteTurnFinished>().Count == 1, "the turn's end");
        Assert.Empty(remote.Client.Of<RemoteTurnFinished>()[0].Images);
    }

    [Fact]
    public async Task Claude_Code_stopping_reaches_the_client_with_why()
    {
        await using var remote = await StartAsync();

        remote.H.Transport.Exit(3);

        await TabTestHarness.Eventually(() => remote.Client.Of<RemoteTabFailed>().Count == 1, "the error");
        var failed = remote.Client.Of<RemoteTabFailed>()[0];
        Assert.Equal("Claude Code stopped unexpectedly (exit code 3).", failed.Message);
        Assert.True(failed.Tab.CanRestart);
        Assert.True((await remote.Desk.RestartAsync(remote.Tab.Id, "Telegram", Ct)).Done);
    }

    [Fact]
    public async Task Closing_says_so_and_stops_the_client()
    {
        await using var remote = await StartAsync();

        await InlineDispatcher.RunAsync(() => remote.Service.CloseAsync());

        Assert.Single(remote.Client.Of<RemoteDeskClosing>());
        Assert.True(remote.Client.Stopped);
        Assert.True(remote.H.Services.State.RemoteClients.SaidClosing);
    }

    [Fact]
    public async Task The_tabs_are_listed_with_their_project()
    {
        await using var remote = await StartAsync();

        var tab = Assert.Single(await remote.Desk.GetTabsAsync(Ct));

        Assert.Equal(remote.Tab.Id, tab.Id);
        Assert.Equal("work", tab.Project);
        Assert.Equal(RemoteTabStatus.Idle, tab.Status);
    }

    // ---- The bot and the computer -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_client_on_keeps_the_computer_awake_unless_General_says_not()
    {
        await using var remote = await StartAsync();
        await TabTestHarness.Eventually(() => remote.H.SleepBlocker.IsBlocking, "sleep held off");

        remote.H.OnUiThread(() =>
        {
            remote.H.Services.Settings.RemoteClients.KeepAwake = false;
            remote.H.Services.SaveSettings();
        });

        await TabTestHarness.Eventually(() => !remote.H.SleepBlocker.IsBlocking, "sleep allowed");
    }

    [Fact]
    public async Task Turning_the_bot_off_stops_the_client()
    {
        await using var remote = await StartAsync();

        remote.H.OnUiThread(() =>
        {
            remote.H.Services.Settings.RemoteClients.SetEnabled("telegram", false);
            remote.H.Services.SaveSettings();
        });

        await TabTestHarness.Eventually(() => remote.Client.Stopped && !remote.Service.IsActive, "the client to stop");
    }

    [Fact]
    public async Task Without_a_token_the_bot_does_not_start_and_setting_one_starts_it()
    {
        await using var h = new TabTestHarness(s => s.RemoteClients.SetEnabled("telegram", true));
        var keys = new FakeCredentialStore();
        var client = new FakeRemoteClient();
        h.Services.RemoteClients.Credentials = keys;
        h.Services.RemoteClients.ClientFactory = (_, _, _) => client;
        await h.OpenTabAsync();
        h.OnUiThread(() => h.Services.RemoteClients.Start(h.Shell));
        await h.Services.RemoteClients.UpdateAsync();
        Assert.False(h.Services.RemoteClients.IsActive);
        Assert.Equal(RemoteClientStatus.Failed, InlineDispatcher.Read(() => h.Services.RemoteClients.StateOf("telegram").Status));

        await Assert.ThrowsAsync<ArgumentException>(() => h.Services.RemoteClients.SetSecretAsync("telegram", "not a token"));
        await h.Services.RemoteClients.SetSecretAsync("telegram", "123456789:AAFakeTokenForTestsOnly_abcdefghijk");

        Assert.Equal("123456789:AAFakeTokenForTestsOnly_abcdefghijk", keys.Secrets[h.Services.RemoteClients.CredentialKeyOf("telegram")]);
        Assert.True(h.Services.RemoteClients.IsActive);
        // Only in the credential store: never in the settings or the state.
        Assert.DoesNotContain("AAFakeToken", InlineDispatcher.Read(() => JsonFileStore<AppSettings>.Serialize(h.Services.Settings)), StringComparison.Ordinal);
        Assert.DoesNotContain("AAFakeToken", InlineDispatcher.Read(() => JsonFileStore<AppState>.Serialize(h.Services.State)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pairing_an_account_keeps_it_in_the_state()
    {
        await using var remote = await StartAsync();
        var pairing = remote.Service.PairingOf("telegram");
        var code = pairing.Start();

        Assert.Equal(PairingOutcome.Paired, pairing.TryPair(code.Code, 11, "Matt", "reapazor"));

        await TabTestHarness.Eventually(() => remote.H.Services.State.RemoteClients.Accounts.Count == 1, "the account kept");
        Assert.Equal(11, remote.H.Services.State.RemoteClients.Accounts[0].UserId);
    }

    // ---- Settings → Remote clients ----------------------------------------------------------------------------------

    [Theory]
    [InlineData("telegram")]
    [InlineData("bot")]
    [InlineData("phone")]
    [InlineData("remote")]
    [InlineData("pair")]
    public async Task Search_finds_the_page(string words)
    {
        await using var h = new TabTestHarness();
        using var settings = new SettingsViewModel(h.Services, null);

        settings.SearchText = words;

        Assert.Contains(settings.SearchResults, r => r.Category is SettingsCategory.AllClients or "Telegram");
    }

    [Fact]
    public async Task The_page_keeps_the_token_in_the_credential_store_and_pairs_with_a_QR_code()
    {
        await using var remote = await StartAsync();
        remote.H.Services.State.RemoteClients.Identities["telegram"] = "deventry_bot";
        using var settings = new SettingsViewModel(remote.H.Services, null);
        var page = settings.Client("telegram");
        await TabTestHarness.Eventually(() => page.HasSecret, "the token read");

        page.StartPairingCommand.Execute(null);

        Assert.True(page.IsPairing);
        Assert.Matches("^[2-9A-Z]{4}-[2-9A-Z]{4}$", page.PairingCode);
        Assert.Equal($"/start {page.PairingCode.Replace("-", "", StringComparison.Ordinal)}", page.PairingCommand);
        Assert.NotNull(page.PairingQrPng);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], page.PairingQrPng![..4]);
        Assert.Equal("Works once, for 5:00 more.", page.PairingTimeLeft);

        remote.H.Time.Advance(TimeSpan.FromSeconds(61));
        await TabTestHarness.Eventually(() => page.PairingTimeLeft == "Works once, for 3:59 more.", "the time left");

        Assert.Equal(PairingOutcome.Paired, remote.Service.PairingOf("telegram").TryPair(page.PairingCommand[7..], 11, "Matt", "reapazor"));
        await TabTestHarness.Eventually(() => !page.IsPairing && page.Accounts.Count == 1, "the pairing to end");
        Assert.Equal("Matt (@reapazor)", page.Accounts[0].Name);

        remote.H.OnUiThread(() =>
        {
            page.Accounts[0].Unpairing.AskCommand.Execute(null);
            page.Accounts[0].Unpairing.ConfirmCommand.Execute(null);
        });
        await TabTestHarness.Eventually(() => page.Accounts.Count == 0 && remote.H.Services.State.RemoteClients.Accounts.Count == 0, "the account unpaired");
    }

    [Fact]
    public async Task A_token_that_is_not_one_is_refused_on_the_page()
    {
        await using var h = new TabTestHarness();
        h.Services.RemoteClients.Credentials = new FakeCredentialStore();
        using var settings = new SettingsViewModel(h.Services, null);
        var page = settings.Client("telegram");

        page.StartSettingSecretCommand.Execute(null);
        page.NewSecret = "hello";
        await InlineDispatcher.RunAsync(() => page.SaveSecretCommand.ExecuteAsync(null));

        Assert.True(page.IsSettingSecret);
        Assert.StartsWith("That isn't a bot's token.", page.SecretError);
    }

    [Fact]
    public async Task The_page_says_what_counts_as_away_here()
    {
        await using var h = new TabTestHarness();
        using var settings = new SettingsViewModel(h.Services, null);

        settings.AllClients.AwayAfterMinutes = 12;

        Assert.EndsWith("or nobody has used its keyboard or mouse for 12 minutes.", settings.AllClients.AwayText);
        Assert.Equal(12, h.Services.Settings.RemoteClients.AwayAfterMinutes);
    }

    [Fact]
    public async Task Telegram_is_Off_in_the_sidebar_until_the_bot_is_used_and_needs_you_without_its_token()
    {
        await using var h = new TabTestHarness();
        using var settings = new SettingsViewModel(h.Services, null);
        var entry = settings.NavEntries.Single(e => e.Title == "Telegram");
        Assert.Equal("Off", entry.Tag);
        Assert.False(entry.IsTagCaution);

        h.OnUiThread(() => settings.Client("telegram").IsOn = true);

        // On with no token, it can't start.
        await TabTestHarness.Eventually(() => entry.Tag == "Needs you", "the tag");
        Assert.True(entry.IsTagCaution);
    }

    [Fact]
    public async Task A_connected_clients_tag_says_it_is_not_paired_until_an_account_is_then_connected()
    {
        await using var remote = await StartAsync();
        using var settings = new SettingsViewModel(remote.H.Services, null);
        var entry = settings.NavEntries.Single(e => e.Title == "Telegram");

        await TabTestHarness.Eventually(() => entry.Tag == "Not paired", "not paired");
        Assert.True(entry.IsTagCaution);

        var code = remote.Service.PairingOf("telegram").Start();
        Assert.Equal(PairingOutcome.Paired, remote.Service.PairingOf("telegram").TryPair(code.Code, 11, "Matt", "reapazor"));

        await TabTestHarness.Eventually(() => entry.Tag == "Connected", "connected");
        Assert.False(entry.IsTagCaution);
        remote.Client.Become(new RemoteClientState(RemoteClientStatus.Failed, "Telegram didn't accept the bot's token."));
        await TabTestHarness.Eventually(() => entry.Tag == "Needs you", "needs you");
    }

    [Fact]
    public async Task What_the_client_says_its_account_is_called_is_kept()
    {
        await using var remote = await StartAsync();

        remote.Client.Identify("deventry_bot");

        await TabTestHarness.Eventually(() => remote.Service.IdentityOf("telegram") == "deventry_bot", "the bot's name kept");
        Assert.Equal("deventry_bot", remote.H.Services.State.RemoteClients.Identities["telegram"]);
        Assert.Contains("Telegram, @deventry_bot, Connected; no account paired", InlineDispatcher.Read(remote.Service.Describe));
    }

    [Fact]
    public async Task A_token_another_Deventry_on_this_computer_keeps_is_not_this_ones()
    {
        await using var h = new TabTestHarness(s => s.RemoteClients.SetEnabled("telegram", true));
        // The demo, or a copy with a data folder of its own: the same credential store, another data folder's id.
        h.Services.RemoteClients.Credentials = new FakeCredentialStore
        {
            Secrets = { ["telegram-bot-token"] = "123456789:no-ones", ["telegram-bot-token-0123456789abcdef0123456789abcdef"] = "123456789:another-deventry" },
        };
        var started = 0;
        h.Services.RemoteClients.ClientFactory = (_, _, _) =>
        {
            Interlocked.Increment(ref started);
            return new FakeRemoteClient();
        };
        await h.OpenTabAsync();
        h.OnUiThread(() => h.Services.RemoteClients.Start(h.Shell));
        await h.Services.RemoteClients.UpdateAsync();

        Assert.Equal(0, Volatile.Read(ref started));
        Assert.Equal(false, h.Services.RemoteClients.HasSecret("telegram"));
        Assert.Equal($"telegram-bot-token-{h.Services.MachineId}", h.Services.RemoteClients.CredentialKeyOf("telegram"));
    }
}
