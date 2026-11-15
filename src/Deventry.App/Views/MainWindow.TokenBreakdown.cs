using Deventry.App.Services;
using Deventry.App.ViewModels;

namespace Deventry.App.Views;

/// <summary>The tabs' token breakdown windows (DESIGN.md §18, "The window"): one per tab, beside the main window.</summary>
public partial class MainWindow
{
    private readonly Dictionary<string, TokenBreakdownWindow> _tokenWindows = new(StringComparer.Ordinal);
    private ShellViewModel? _tokensShell;

    /// <summary>Follows the shell the main window shows, for its tabs' requests.</summary>
    private void UseShellForTokens(ShellViewModel? shell)
    {
        if (ReferenceEquals(shell, _tokensShell))
        {
            return;
        }
        if (_tokensShell is not null)
        {
            _tokensShell.TokenBreakdownRequested -= ShowTokenBreakdown;
        }
        _tokensShell = shell;
        if (shell is not null)
        {
            shell.TokenBreakdownRequested += ShowTokenBreakdown;
        }
    }

    /// <summary>A tab's breakdown: its window, or a new one, brought to the front, on the turn asked for.</summary>
    private void ShowTokenBreakdown(TabViewModel tab, string? turnId, int? number)
    {
        if (_tokenWindows.TryGetValue(tab.Id, out var open))
        {
            (open.DataContext as TokenBreakdownViewModel)?.Show(turnId, number);
            UnfocusedWindows.Activate(open);
            return;
        }
        if (_viewModel is not { } main)
        {
            return;
        }
        var breakdown = new TokenBreakdownViewModel(main.Services, tab);
        var window = new TokenBreakdownWindow { DataContext = breakdown };
        _tokenWindows[tab.Id] = window;
        window.Closed += (_, _) =>
        {
            _tokenWindows.Remove(tab.Id);
            breakdown.Dispose();
        };
        breakdown.Show(turnId, number);
        breakdown.Refresh();
        window.Show(this);
    }
}
