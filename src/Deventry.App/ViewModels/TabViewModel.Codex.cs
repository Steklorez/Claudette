using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.Core.Agents;
using Deventry.Core.Codex;
using Deventry.Core.History;
using Deventry.Core.Library;
using Deventry.Core.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Deventry.App.ViewModels;

/// <summary>
/// The tab's agent (DESIGN.md §18, "Codex, a second agent"): Claude Code unless the tab was opened on another, and a Codex
/// tab's start, through <c>codex app-server</c>.
/// </summary>
public partial class TabViewModel
{
    /// <summary>The agent the tab runs. A tab keeps its agent.</summary>
    public IAgentType Agent => AgentsService.Find(State.Agent);

    public bool IsCodex => Agent.Kind == AgentKinds.Codex;

    /// <summary>What the tab's words call its agent: "Claude", "Codex".</summary>
    public string AgentShortName => Agent.ShortName;

    /// <summary>The agent's icon, by its key in App.axaml (DESIGN.md §18, "Its icon").</summary>
    public string AgentIconKey => IsCodex ? "IconAgentCodex" : "IconAgentClaudeCode";

    /// <summary>The row shows the agent's icon: while tabs of more than one agent are open, which the shell says.</summary>
    [ObservableProperty]
    public partial bool ShowsAgentIcon { get; set; }

    /// <summary>The Claude app reaches the tab's session: Claude Code's alone (DESIGN.md §18, "Remote Control").</summary>
    public bool OffersRemoteControl => Offers(AgentFeatures.RemoteControl);

    /// <summary>Ultracode, in the effort menu: Claude Code's alone.</summary>
    public bool OffersUltracode => Offers(AgentFeatures.Ultracode);

    /// <summary>What only some agents have, which this tab offers.</summary>
    public bool Offers(AgentFeatures feature) => Agent.Features.HasFlag(feature);

    /// <summary>
    /// Starts the tab's Codex (DESIGN.md §18, "Codex, a second agent"): its thread, resumed by its id when it has one,
    /// with Deventry's notes for the folder as Codex's developer instructions and the folder's <c>CLAUDE.md</c> read when
    /// it has no <c>AGENTS.md</c>.
    /// </summary>
    private async Task StartCodexAsync()
    {
        if (_services.CodexSessions is not { } sessions)
        {
            ErrorMessage = "Codex isn't set up";
            Status = TabStatus.Error;
            _conversation.AddNote("Deventry hasn't found Codex. Install it, or point Deventry at it, in Settings → Agents → Codex; then restart the tab.", NoteKind.Error);
            return;
        }
        _restartForExtraFolders = false;
        _restartForAccount = false;
        if (!Directory.Exists(Folder))
        {
            MarkFolderMissing();
            return;
        }
        ErrorMessage = null;
        Status = TabStatus.Starting;
        var settings = _services.Settings;
        var resume = State.SessionId;
        var fork = State.ForkOnNextStart && resume is not null;
        LibraryComparison? pickedUp = null;
        if (resume is not null && !fork && State.SyncToLibrary)
        {
            (var start, pickedUp) = await SyncFromLibraryAsync(resume);
            // Only a tab that syncs takes part in leases (DESIGN.md §9, "One machine at a time").
            if (!start || _closing.IsCancellationRequested || !await ClaimLeaseAsync())
            {
                return;
            }
        }
        try
        {
            var mode = CodexModes.Normalize(State.Overrides.PermissionMode ?? settings.Codex.DefaultMode);
            ExpectedPermissionMode = mode;
            // Deventry's hook, which checks files out of Perforce before Codex's patches (DESIGN.md §18, "Perforce").
            var hook = await PerforceHookCommandAsync();
            // The same notes Claude Code's system prompt gets: the project's, the Perforce workspace's and the subagents'.
            var notes = WithSubagentNote(await Perforce.WithPerforceAsync(await ProjectTools.WithNoteAsync(new ClaudeLaunchOptions { WorkingDirectory = Folder }))).AppendSystemPrompt;
            var options = new CodexLaunchOptions
            {
                Start = new CodexStartOptions
                {
                    WorkingDirectory = Folder,
                    Resume = resume,
                    Fork = fork,
                    // Up to the point a rewind or branch goes back to (DESIGN.md §18, "Rewind and branch").
                    ForkAfter = fork ? State.ResumeAt : null,
                    Model = State.Overrides.Model ?? settings.Codex.DefaultModel,
                    Effort = State.Overrides.Effort,
                    Mode = mode,
                    DeveloperInstructions = notes,
                    // Deventry's own tools, which a thread's messages to its sub-threads go through (DESIGN.md §18,
                    // "Threads"); a resumed thread keeps the ones it started with.
                    DynamicTools = resume is null ? Core.Codex.CodexTools.All() : null,
                    ClientVersion = _services.AppVersion.ToString(),
                },
                // Subagents turned off take Codex's tools for them away (DESIGN.md §18, "Subagents").
                ConfigOverrides =
                [
                    CodexArguments.ClaudeMdFallback,
                    .. SubagentUse == Core.Settings.SubagentUse.DontAllow ? [CodexArguments.NoSubagents] : Array.Empty<string>(),
                    .. hook is null ? Array.Empty<string>() : [CodexHooks.Config(hook)],
                ],
                EnvironmentOverrides = hook is null
                    ? _services.CodexVariables
                    : new Dictionary<string, string?>(_services.CodexVariables) { [CodexHooks.PipeVariable] = HookPipe().Name },
                ProtocolLogPath = _services.ProtocolLogPath(FolderName),
            };
            _closing.Token.ThrowIfCancellationRequested();
            var session = await sessions.StartAsync(options, _closing.Token);
            if (_closing.IsCancellationRequested)
            {
                await session.DisposeAsync();
                return;
            }
            session.ToolHandler = OnCodexToolCallAsync;
            _session = session;
            _appliedState = SessionState.Starting;
            _sentSinceStart = false;
            State.SessionId = session.SessionId;
            // Its session file is what the library copies (DESIGN.md §18, "The session library").
            State.TranscriptPath = session.ThreadPath ?? State.TranscriptPath;
            if (fork)
            {
                // A copy has its own thread from the start, and the original is left as it was.
                RecordSessionOrigin(resume, session.SessionId!, isCopy: true);
                State.ForkOnNextStart = false;
                State.ResumeAt = null;
                State.ResumeDropsTurn = null;
                OnPropertyChanged(nameof(InfoRows));
                OnSyncSizeChanged();
            }
            State.SessionStartedAt ??= _services.Time.GetUtcNow();
            Tasks.Clear();
            ProcessMonitor.AttachTree(session);
            Effort = session.Effort ?? State.Overrides.Effort;
            PermissionMode = session.PermissionMode;
            _modelId = session.Model;
            ModelName = ModelDisplayName(_modelId) ?? CurrentModelInfo?.DisplayName;
            OnPropertyChanged(nameof(Models));
            OnPropertyChanged(nameof(EffortLevels));
            OnPropertyChanged(nameof(PermissionModeChoices));
            if (!session.SandboxReady)
            {
                // DESIGN.md §18, "Codex's sandbox": none of Codex's Windows sandboxes could be used.
                _conversation.AddNote("Codex's Windows sandbox isn't ready, so Codex asks before each command and edit. Choose one in Codex's own configuration (windows.sandbox) to let it work on its own.", NoteKind.Warning);
            }
            if (resume is not null && !_restoredTranscript)
            {
                // Before the session's own events, which wait for the pump.
                _restoredTranscript = true;
                await ReplayCodexTurnsAsync(session, fork ? _forkNote ?? "Opened as a copy. The original session is left as it was."
                    : pickedUp is not null ? PickedUpNote(pickedUp) : "Resumed.");
                _forkNote = null;
            }
            if (hook is not null)
            {
                _ = AskToTrustHookAsync(session, hook);
            }
            Status = TabStatus.Idle;
            _pump = PumpAsync(session);
            _contextRefresh = Context.RefreshUsageAsync(session);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Closed: the start's codex, if there was one, is stopped already.
        }
        catch (CodexThreadMissingException)
        {
            // The tab says so and waits: starting a new session is the user's choice, as for Claude Code (DESIGN.md §9,
            // "Old sessions").
            _restoredTranscript = true;
            IsSessionMissing = true;
            ErrorMessage = "Its earlier conversation is gone";
            Status = TabStatus.Error;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't start Codex: {ex.Message}";
            Status = TabStatus.Error;
            _conversation.AddNote($"Couldn't start Codex: {ex.Message}", NoteKind.Error);
            NotifyProcessError($"Couldn't start Codex: {ex.Message}");
        }
    }

    /// <summary>The pipe the tab's hook helper reaches it through, made with the first start that has the hook.</summary>
    private CodexHookPipe? _hookPipe;

    /// <summary>The user said not to trust Deventry's hook, so the tab doesn't ask again until it's opened again.</summary>
    private bool _hookTrustDeclined;

    private CodexHookPipe HookPipe() => _hookPipe ??= new CodexHookPipe(Perforce.CheckOutBeforeCodexPatchAsync);

    /// <summary>
    /// Deventry's hook command, when the tab's Codex should have it: Settings → Perforce checks files out before changes,
    /// the folder is in a workspace, and Deventry's own path can go in a command. Null otherwise.
    /// </summary>
    private async Task<string?> PerforceHookCommandAsync()
    {
        if (!_services.Settings.Perforce.CheckOutBeforeChanges || _services.Accounts.HelperExecutable is not { } executable
            || !Core.Accounts.ApiKeyHelper.IsSafePath(executable))
        {
            return null;
        }
        try
        {
            return await _services.Perforce.Files.WorkspaceForAsync(Folder, _closing.Token) is null ? null : CodexHooks.Command(executable);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Codex runs a hook only once the user has trusted it, as its own <c>/hooks</c> asks: while Deventry's isn't, the
    /// tab asks, saying what it runs, and writes the trust once they say yes (DESIGN.md §18, "Trusting Deventry's hook").
    /// </summary>
    private async Task AskToTrustHookAsync(CodexSession session, string command)
    {
        CodexHookState? hook;
        try
        {
            hook = await session.FindHookAsync(Folder, command, _closing.Token);
        }
        catch (Exception ex) when (ex is CodexRequestException or TimeoutException or OperationCanceledException or IOException)
        {
            return;
        }
        if (hook is null || hook.IsTrusted || _hookTrustDeclined || _closing.IsCancellationRequested)
        {
            return;
        }
        _services.Dispatcher.Post(() => _shell.Confirm(
            "Let Codex check files out of Perforce?",
            $"This folder is in a Perforce workspace, where files are read-only until they're checked out. Deventry can check them out before each of Codex's edits, through a hook that runs {command}. "
                + "Codex runs a hook only once you've trusted it: trusting it writes its hash to Codex's configuration (config.toml), as Codex's own /hooks does. A new version of Deventry asks again.",
            "Trust Deventry's hook",
            () => TrustHookAsync(session, hook),
            "Not now",
            () =>
            {
                _hookTrustDeclined = true;
                _conversation.AddNote("Codex's edits won't check files out of Perforce first: Deventry's hook isn't trusted.", NoteKind.Warning);
                return Task.CompletedTask;
            }));
    }

    private async Task TrustHookAsync(CodexSession session, CodexHookState hook)
    {
        try
        {
            await session.TrustHookAsync(hook, _closing.Token);
            _conversation.AddNote("Codex checks files out of Perforce before it changes them now.");
        }
        catch (Exception ex) when (ex is CodexRequestException or TimeoutException or IOException)
        {
            _conversation.AddNote($"Couldn't trust Deventry's hook in Codex's configuration: {ex.Message}", NoteKind.Warning);
        }
    }

    /// <summary>
    /// A resumed Codex tab's earlier conversation, which Codex keeps and reads back (DESIGN.md §18, "History"), shown as a
    /// Claude Code tab's transcript is (<see cref="RestoreTranscriptAsync"/>).
    /// </summary>
    /// <param name="note">What the tab says under them: resumed, picked up from another machine, or opened as a copy.</param>
    private async Task ReplayCodexTurnsAsync(CodexSession session, string note)
    {
        IReadOnlyList<(SessionEvent Event, DateTimeOffset? Time)> turns;
        try
        {
            turns = await session.ReadTurnsAsync(_closing.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _conversation.AddNote($"Couldn't read the earlier conversation: {ex.Message}", NoteKind.Warning);
            return;
        }
        Agents.IsReplaying = true;
        var todoListClosed = State.TodoListClosed;
        var recallPrompts = _recall.Count == 0;
        using (Items.DeferNotifications())
        {
            foreach (var (sessionEvent, time) in turns)
            {
                if (sessionEvent is PromptReplayed { Message: var prompt })
                {
                    var text = prompt.PromptText ?? "";
                    _conversation.ReplayUserMessage(text, [], time, prompt.Uuid);
                    if (recallPrompts)
                    {
                        _recall.Add(text);
                    }
                    continue;
                }
                _conversation.Replay(sessionEvent, time);
                ChangedFiles.Record(sessionEvent, live: false, time);
            }
        }
        RestoreChosenPlan();
        RestoreTodoListClosed(todoListClosed);
        ChangedFiles.ChangesRead();
        if (turns.Count > 0)
        {
            _titleRequested = true;
            _conversation.AddNote(note);
        }
    }

    /// <summary>
    /// Before a Codex tab that syncs resumes its thread (DESIGN.md §18, "The session library"): another machine's later
    /// turns are brought from the library into the session's file in Codex's folder, as is the whole session when this
    /// machine has none of it, and a session that went on in both places is kept apart (DESIGN.md §9, "One machine at a
    /// time"). Returns whether the tab starts, and the comparison that brought turns here.
    /// </summary>
    private async Task<(bool Start, LibraryComparison? PickedUp)> SyncFromLibraryAsync(string sessionId)
    {
        var codexHome = _services.CodexHome;
        var here = State.TranscriptPath is { } path && File.Exists(path) ? path
            : codexHome is null ? null
            : await Task.Run(() => CodexHistory.Find(codexHome, sessionId));
        State.TranscriptPath = here ?? State.TranscriptPath;
        var comparison = here is null ? null : await CompareWithLibraryAsync();
        if (comparison is { Standing: LibraryStanding.Diverged })
        {
            await KeepApartAsync(comparison.Machine);
            return (false, null);
        }
        var bring = comparison is { Standing: LibraryStanding.Ahead } || here is null && _services.Library.Library.GetTranscriptPath(sessionId) is not null;
        if (!bring || (here ?? (codexHome is null ? null : CodexHistory.PlaceFor(codexHome, sessionId, _services.Time.GetUtcNow()))) is not { } target)
        {
            return (true, null);
        }
        try
        {
            State.TranscriptPath = await _services.Library.PickUpAsync(sessionId, target);
            _services.SaveState();
            return (true, comparison ?? new LibraryComparison(LibraryStanding.Ahead, null, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // It goes on from this machine's; the copy after its next turn won't write over the other machine's.
            _conversation.AddNote($"Couldn't bring this session's later turns from the session library: {ex.Message}", NoteKind.Warning);
            return (true, null);
        }
    }
}
