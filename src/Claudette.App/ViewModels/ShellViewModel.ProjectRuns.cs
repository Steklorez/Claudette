namespace Claudette.App.ViewModels;

/// <summary>
/// Project runs' logs in the main area (DESIGN.md §18, "Project tools", "A run's log"): each run of a tab's project job is
/// a tab of the panes without a composer, from when it starts until it's closed or its tab is. Its entry under the tab's
/// row shows it; the tab's own row goes back to the conversation. Runs aren't saved, so the app state keeps their tab.
/// </summary>
public sealed partial class ShellViewModel
{
    /// <summary>The focused tab when it's a run's log.</summary>
    public ProjectRunViewModel? SelectedRun => Selected as ProjectRunViewModel;

    /// <summary>The session tab <paramref name="run"/> belongs to.</summary>
    internal TabViewModel? TabOf(ProjectRunViewModel run) => AllTabs.FirstOrDefault(t => t.ProjectTools.Runs.Items.Contains(run));

    /// <summary>What the app state keeps for <paramref name="tab"/>: its own id, or for a run's or an editor's log, its tab's.</summary>
    private string? SavedId(IShellTab? tab) => tab is LogTabViewModel log ? TabOf(log)?.Id : tab?.Id;

    /// <summary>A run began: its log can be shown from now on, but starting doesn't take the main area.</summary>
    internal void AddRun(ProjectRunViewModel run)
    {
        if (!OpenTabs.Contains(run))
        {
            OpenTabs.Add(run);
        }
    }

    /// <summary>A run's entry clicked, <b>Show output…</b> or its notification: its log takes the focus.</summary>
    internal void ShowRun(ProjectRunViewModel run)
    {
        if (OpenTabs.Contains(run))
        {
            Selected = run;
        }
    }

    /// <summary>A run's log took the focus: it has no side panel for the sidebar to leave room for.</summary>
    internal void OnRunSelected() => Layout.IsSidePanelShown = false;

    /// <summary>
    /// A run was closed, by its × or with its tab: its log leaves the panes. While it had the focus, its tab takes its
    /// place, or the focus where it's on screen already.
    /// </summary>
    internal void RemoveRun(ProjectRunViewModel run, TabViewModel? tab)
    {
        if (!OpenTabs.Remove(run))
        {
            return;
        }
        var pane = _panes.IndexOf(run);
        if (!ReferenceEquals(Selected, run))
        {
            if (pane >= 0)
            {
                _panes.RemoveAt(pane);
                UpdatePanes();
            }
            return;
        }
        var back = tab is not null && OpenTabs.Contains(tab) ? tab : null;
        if (pane >= 0 && back is not null && !_panes.Contains(back))
        {
            _panes[pane] = back;
        }
        else if (pane >= 0)
        {
            _panes.RemoveAt(pane);
        }
        Selected = back ?? (_panes.Count > 0 ? _panes[Math.Min(Math.Max(pane, 0), _panes.Count - 1)] : null) ?? AllTabs.FirstOrDefault();
    }

    /// <summary>
    /// <c>Ctrl/Cmd+W</c> on a run's log: a finished run closes, as its × does. A running one goes back to its tab instead,
    /// so the key never stops a build.
    /// </summary>
    private void CloseSelectedRun(ProjectRunViewModel run)
    {
        if (run.CloseCommand.CanExecute(null))
        {
            run.CloseCommand.Execute(null);
        }
        else if (TabOf(run) is { } tab)
        {
            SelectedTab = tab;
        }
    }
}
