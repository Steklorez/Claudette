using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.Core.Agents;
using Deventry.Core.Codex;
using Deventry.Core.Sessions;

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
        try
        {
            var mode = CodexModes.Normalize(State.Overrides.PermissionMode ?? settings.Codex.DefaultMode);
            ExpectedPermissionMode = mode;
            // The same notes Claude Code's system prompt gets: the project's and the Perforce workspace's.
            var notes = (await Perforce.WithPerforceAsync(await ProjectTools.WithNoteAsync(new ClaudeLaunchOptions { WorkingDirectory = Folder }))).AppendSystemPrompt;
            var options = new CodexLaunchOptions
            {
                Start = new CodexStartOptions
                {
                    WorkingDirectory = Folder,
                    Resume = State.SessionId,
                    Model = State.Overrides.Model ?? settings.Codex.DefaultModel,
                    Effort = State.Overrides.Effort,
                    Mode = mode,
                    DeveloperInstructions = notes,
                    ClientVersion = _services.AppVersion.ToString(),
                },
                ConfigOverrides = [CodexArguments.ClaudeMdFallback],
                ProtocolLogPath = _services.ProtocolLogPath(FolderName),
            };
            _closing.Token.ThrowIfCancellationRequested();
            var session = await sessions.StartAsync(options, _closing.Token);
            if (_closing.IsCancellationRequested)
            {
                await session.DisposeAsync();
                return;
            }
            _session = session;
            _appliedState = SessionState.Starting;
            _sentSinceStart = false;
            State.SessionId = session.SessionId;
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
            Status = TabStatus.Idle;
            _pump = PumpAsync(session);
            _contextRefresh = Context.RefreshUsageAsync(session);
        }
        catch (OperationCanceledException) when (_closing.IsCancellationRequested)
        {
            // Closed: the start's codex, if there was one, is stopped already.
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't start Codex: {ex.Message}";
            Status = TabStatus.Error;
            _conversation.AddNote($"Couldn't start Codex: {ex.Message}", NoteKind.Error);
            NotifyProcessError($"Couldn't start Codex: {ex.Message}");
        }
    }
}
