using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// The session the windows that follow it show (DESIGN.md §3, "Tool windows", "Whose data"): the focused session tab's,
/// or with a Project Files tab or a log focused, its group's last focused session tab's. A window whose kind shows only
/// when the session has something for it has a tab while it has.
/// </summary>
public sealed partial class ToolDockViewModel
{
    /// <summary>The session tab the windows that follow the session show; null with none open.</summary>
    [ObservableProperty]
    public partial TabViewModel? Session { get; private set; }

    /// <summary>The windows name the session they show, which might not be the one the user is looking at.</summary>
    public bool NamesSession { get; private set; }

    /// <summary>The session's group color, beside its name.</summary>
    public Avalonia.Media.Color? SessionColor { get; private set; }

    /// <summary>Stops listening to the session for its windows' tabs.</summary>
    private readonly List<Action> _unwatch = [];

    /// <summary>
    /// The shell's choice of session: the windows that follow it show <paramref name="session"/>, naming it when
    /// <paramref name="named"/> (the user may be looking at another tab), with its group's <paramref name="color"/>.
    /// </summary>
    internal void ShowSession(TabViewModel? session, bool named, Avalonia.Media.Color? color)
    {
        if (!ReferenceEquals(session, Session))
        {
            Unwatch();
            Session = session;
            foreach (var window in _windows.Where(w => w.Kind.FollowsSession))
            {
                window.Content = session;
            }
            Watch(session);
            WatchAdornments(session);
        }
        NamesSession = named && session is not null;
        SessionColor = color;
        Refresh();
    }

    /// <summary>Each window whose kind shows only when the session has something for it hears when it has.</summary>
    private void Watch(TabViewModel? session)
    {
        foreach (var window in _windows.Where(w => w.Kind is { FollowsSession: true, ShowsWhen: not null }))
        {
            var condition = window.Kind.ShowsWhen!;
            if (session is null)
            {
                window.IsAvailable = false;
                continue;
            }
            var source = condition.Source(session);
            void OnChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == condition.Property)
                {
                    window.IsAvailable = condition.Holds(session);
                }
            }
            source.PropertyChanged += OnChanged;
            _unwatch.Add(() => source.PropertyChanged -= OnChanged);
            window.IsAvailable = condition.Holds(session);
        }
    }

    private void Unwatch()
    {
        foreach (var unwatch in _unwatch)
        {
            unwatch();
        }
        _unwatch.Clear();
    }
}
