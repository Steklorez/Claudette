using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Deventry.App.ViewModels;

namespace Deventry.App.Conversation;

/// <summary>
/// The icon on each tool card (DESIGN.md §5): the key of one of the small vector icons in App.axaml, drawn the same
/// on every platform.
/// </summary>
public static class ToolIcons
{
    public const string Default = "IconToolDefault";

    /// <summary>For one of Claude Code's tools, by its name.</summary>
    public static string KeyFor(string toolName) => toolName is "ListMcpResourcesTool" or "ReadMcpResourceTool"
        ? "IconToolMcp"
        : KeyFor(Core.Protocol.ClaudeToolCalls.KindOf(toolName));

    /// <summary>For a call, by what it does, whichever agent made it.</summary>
    public static string KeyFor(Core.Sessions.ToolCall call) => call.Name is "ListMcpResourcesTool" or "ReadMcpResourceTool" ? "IconToolMcp" : KeyFor(call.Kind);

    private static string KeyFor(Core.Sessions.ToolKind kind) => kind switch
    {
        Core.Sessions.ToolKind.Read or Core.Sessions.ToolKind.ViewImage => "IconToolRead",
        Core.Sessions.ToolKind.Edit or Core.Sessions.ToolKind.Delete => "IconToolEdit",
        Core.Sessions.ToolKind.Write => "IconToolWrite",
        Core.Sessions.ToolKind.Command or Core.Sessions.ToolKind.CommandOutput => "IconToolBash",
        Core.Sessions.ToolKind.Search => "IconToolSearch",
        Core.Sessions.ToolKind.Find => "IconToolFolder",
        Core.Sessions.ToolKind.WebFetch => "IconToolWeb",
        Core.Sessions.ToolKind.WebSearch => "IconToolWebSearch",
        Core.Sessions.ToolKind.Agent => "IconToolAgent",
        Core.Sessions.ToolKind.Todo => "IconToolTodo",
        Core.Sessions.ToolKind.Skill => "IconToolSkill",
        Core.Sessions.ToolKind.Monitor => "IconToolMonitor",
        Core.Sessions.ToolKind.Question => "IconToolQuestion",
        Core.Sessions.ToolKind.Plan => "IconToolPlan",
        Core.Sessions.ToolKind.Mcp => "IconToolMcp",
        _ => Default,
    };

    /// <summary>Looks the icon up in the app's resources; the default icon when it's missing.</summary>
    public static readonly IValueConverter Geometry = new FuncValueConverter<string?, Geometry?>(key =>
        Find(key ?? Default) ?? Find(Default));

    /// <summary>
    /// Whether the icon is drawn filled rather than stroked: a maker's logo (<see cref="Logos.IsLogo"/>), for a
    /// <c>filled</c> class on its <c>Path</c>.
    /// </summary>
    public static readonly IValueConverter IsFilled = new FuncValueConverter<string?, bool>(Logos.IsLogo);

    private static Geometry? Find(string key) =>
        Application.Current?.TryFindResource(key, out var resource) == true ? resource as Geometry : null;
}
