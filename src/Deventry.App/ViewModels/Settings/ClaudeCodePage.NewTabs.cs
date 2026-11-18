using Deventry.Core;

namespace Deventry.App.ViewModels.Settings;

/// <summary>
/// Settings → Agents → Claude Code → New Claude Code tabs (DESIGN.md §14): the model, effort and permission mode a new
/// Claude Code tab starts with, which its Tab settings… can change. The lists are what Claude Code offered in its last
/// <c>initialize</c> reply on this machine, with a built-in list only until a session has started.
/// </summary>
public sealed partial class ClaudeCodePage
{
    /// <summary>Used until a session has told Deventry which models the installed Claude Code offers.</summary>
    private static readonly IReadOnlyList<Choice> BuiltInModels =
        [new("opus", "Opus"), new("sonnet", "Sonnet"), new("haiku", "Haiku"), new("fable", "Fable")];

    private static readonly IReadOnlyList<string> BuiltInEffortLevels = ["low", "medium", "high", "xhigh", "max"];

    public IReadOnlyList<Choice> ModelChoices => field ??= BuildModelChoices();

    public IReadOnlyList<Choice> EffortChoices => field ??= BuildEffortChoices();

    /// <summary>
    /// "Claude Code's default" names the mode it comes to from the user's and managed settings, usually Auto (DESIGN.md
    /// §7, "Starting mode"). A project's settings can still change it for its tabs.
    /// </summary>
    public IReadOnlyList<Choice> ModeChoices => field ??=
    [
        new(null, $"Claude Code's default ({PermissionModeInfo.Label(Services.ReadStartingPermissionMode(null).Expected)})"),
        .. PermissionModeInfo.Choices.Select(m => new Choice(m.Value, m.Label)),
    ];

    public Choice DefaultModel
    {
        get => ModelChoices.FirstOrDefault(c => c.Value == Settings.NewTabs.DefaultModel) ?? ModelChoices[0];
        set => Set(value?.Value, v => Settings.NewTabs.DefaultModel = v);
    }

    public Choice DefaultEffort
    {
        get => EffortChoices.FirstOrDefault(c => c.Value == Settings.NewTabs.DefaultEffort) ?? EffortChoices[0];
        set => Set(value?.Value, v => Settings.NewTabs.DefaultEffort = v);
    }

    public Choice DefaultMode
    {
        get => ModeChoices.FirstOrDefault(c => c.Value == Settings.NewTabs.DefaultPermissionMode) ?? ModeChoices[0];
        set => Set(value?.Value, v => Settings.NewTabs.DefaultPermissionMode = v);
    }

    /// <summary>What Claude Code offered in its last <c>initialize</c> reply, plus the current default if it's not among them.</summary>
    private IReadOnlyList<Choice> BuildModelChoices()
    {
        var known = Services.State.KnownModels;
        IReadOnlyList<Choice> models = known.Count > 0 ? [.. known.Select(m => new Choice(m.Value, m.DisplayName))] : BuiltInModels;
        var choices = new List<Choice> { new(null, "Claude Code's default") };
        choices.AddRange(models);
        if (Settings.NewTabs.DefaultModel is { } current && choices.All(c => c.Value != current))
        {
            choices.Add(new Choice(current, current));
        }
        return choices;
    }

    /// <summary>Every effort level a model offers, in Claude Code's order.</summary>
    private IReadOnlyList<Choice> BuildEffortChoices()
    {
        var levels = Services.State.KnownModels.SelectMany(m => m.SupportedEffortLevels).Distinct(StringComparer.Ordinal).ToList();
        if (levels.Count == 0)
        {
            levels = [.. BuiltInEffortLevels];
        }
        var choices = new List<Choice> { new(null, "The model's default") };
        choices.AddRange(levels.Select(l => new Choice(l, Formats.Capitalize(l))));
        if (Settings.NewTabs.DefaultEffort is { } current && choices.All(c => c.Value != current))
        {
            choices.Add(new Choice(current, Formats.Capitalize(current)));
        }
        return choices;
    }
}
