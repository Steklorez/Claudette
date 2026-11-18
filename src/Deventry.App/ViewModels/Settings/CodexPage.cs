using Deventry.Core.Codex;

namespace Deventry.App.ViewModels.Settings;

/// <summary>
/// Settings → Agents → Codex (DESIGN.md §18, "Codex, a second agent"): the Codex Deventry found, or installing it; its own
/// sign-in; and how a new Codex tab starts. What applies to both agents is All agents' (<see cref="AllAgentsPage"/>).
/// Dispose it with the window.
/// </summary>
public sealed partial class CodexPage : SettingsPage, IDisposable
{
    public CodexPage(SettingsContext context) : base(context, SettingsCategory.Codex)
    {
        Section("installed", "Codex",
        [
            Info("codex.found", "Found", keywords: ["installed", "version", "look again", "install codex"]),
            Setting("codex.codexPath", "Path to codex", s => s.Codex.CodexPath, (s, v) => s.Codex.CodexPath = v,
                "Empty finds Codex by itself: on your PATH, or where its installer puts it.", keywords: ["executable", "binary", "location", "openai"]),
        ], description: "OpenAI's coding agent. Deventry runs it through codex app-server, a tab at a time.");
        Section("account", "Account",
        [
            Info("codex.account", "Signed in", "Codex signs itself in, as codex login does in a terminal: with your ChatGPT account in the browser.",
                keywords: ["sign in", "sign out", "login", "logout", "chatgpt", "openai"]),
        ]);
        Section("newTabs", "New Codex tabs",
        [
            Setting("codex.defaultModel", "Model", s => s.Codex.DefaultModel, (s, v) => s.Codex.DefaultModel = v,
                "Empty uses Codex's own default.", keywords: ["gpt", "model"]),
            Setting("codex.defaultMode", "Mode", s => s.Codex.DefaultMode, (s, v) => s.Codex.DefaultMode = v,
                "What Codex may do without asking. Each tab can change it.", keywords: ["sandbox", "approval", "read only", "full access", "plan"]),
        ], description: "These apply to tabs started after a change.");
        Setup = new CodexSetupViewModel(Services);
    }

    public override string IconKey => "IconAgentCodex";

    public override string Description => "Codex, the second agent: finding or installing it, its sign-in, and how its tabs start.";

    /// <summary>Finding or installing Codex, and its sign-in, as the setup and sign-in screens offer them.</summary>
    public CodexSetupViewModel Setup { get; }

    public void Dispose() => Setup.Dispose();

    public string? CodexPath
    {
        get => Settings.Codex.CodexPath;
        set => Set(string.IsNullOrWhiteSpace(value) ? null : value.Trim(), v => Settings.Codex.CodexPath = v);
    }

    // ---- New Codex tabs ------------------------------------------------------------------------------------------------

    public string? DefaultModel
    {
        get => Settings.Codex.DefaultModel;
        set => Set(string.IsNullOrWhiteSpace(value) ? null : value.Trim(), v => Settings.Codex.DefaultModel = v);
    }

    public IReadOnlyList<PermissionModeChoice> ModeChoices => PermissionModeInfo.CodexChoices;

    public PermissionModeChoice DefaultMode
    {
        get => ModeChoices.First(c => c.Value == CodexModes.Normalize(Settings.Codex.DefaultMode));
        set
        {
            if (value is not null)
            {
                Set(value.Value == CodexModes.Auto ? null : value.Value, v => Settings.Codex.DefaultMode = v);
            }
        }
    }
}
