using System.ComponentModel;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// What each tool window's tab says beside its title (DESIGN.md §3, "Tool windows"), as the side panel's page tabs did:
/// a busy dot while a subagent or a project's job runs, Tasks' count, a warning dot when an MCP server needs looking at,
/// and a tip, all of the session the windows show, kept up as it changes.
/// </summary>
public sealed partial class ToolDockViewModel
{
    private readonly List<INotifyPropertyChanged> _watched = [];

    /// <summary>Watches what the tabs say of <paramref name="session"/>, in place of the one before.</summary>
    private void WatchAdornments(TabViewModel? session)
    {
        foreach (var source in _watched)
        {
            source.PropertyChanged -= OnAdornmentSourceChanged;
        }
        _watched.Clear();
        if (session is not null)
        {
            _watched.AddRange([session, session.TodoList, session.ProjectTools.Runs, session.McpServers]);
            foreach (var source in _watched)
            {
                source.PropertyChanged += OnAdornmentSourceChanged;
            }
        }
        UpdateAdornments(session);
    }

    private void OnAdornmentSourceChanged(object? sender, PropertyChangedEventArgs e) => UpdateAdornments(Session);

    private void UpdateAdornments(TabViewModel? session)
    {
        foreach (var window in _windows)
        {
            var (badge, busy, warning, tip) = window.Kind.Id switch
            {
                "Agents" when session is not null => (null, session.HasActiveAgents, false, session.AgentsHeader),
                "Project" when session is not null => (null, session.ProjectTools.Runs.IsJobRunning, false, session.ProjectTools.HeaderTitle),
                "Tasks" when session is not null => (session.TodoList.HasItems ? session.TodoList.Badge : null, false, false, session.TodoList.PageTip),
                "Mcp" when session is not null => (null, false, session.McpServers.Attention is not null, session.McpServers.Attention ?? "MCP servers"),
                "ScratchPad" => (null, false, false, "Notes shared by the tabs in this project"),
                "Games" => (null, false, false, "Playing against Claudette"),
                _ => ((string?)null, false, false, (string?)null),
            };
            window.Badge = badge;
            window.IsBusy = busy;
            window.HasWarning = warning;
            window.Tip = tip;
        }
    }
}
