using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Claudette.App.ViewModels.ToolWindows;

namespace Claudette.App.Views;

/// <summary>What a tool window shows (DESIGN.md §3, "Tool windows"): for a kind that follows the session, its page of the session tab.</summary>
public partial class ToolWindowView : UserControl
{
    public static readonly DirectProperty<ToolWindowView, bool> HasSessionProperty =
        AvaloniaProperty.RegisterDirect<ToolWindowView, bool>(nameof(HasSession), v => v.HasSession);

    public static readonly DirectProperty<ToolWindowView, bool> HasNoSessionProperty =
        AvaloniaProperty.RegisterDirect<ToolWindowView, bool>(nameof(HasNoSession), v => v.HasNoSession);

    private ToolWindowViewModel? _window;
    private bool _hasSession;
    private bool _hasNoSession;

    public ToolWindowView() => InitializeComponent();

    /// <summary>It follows the session, and there's a session tab to follow.</summary>
    public bool HasSession
    {
        get => _hasSession;
        private set => SetAndRaise(HasSessionProperty, ref _hasSession, value);
    }

    /// <summary>It follows the session, and no tab is open.</summary>
    public bool HasNoSession
    {
        get => _hasNoSession;
        private set => SetAndRaise(HasNoSessionProperty, ref _hasNoSession, value);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowChanged;
        }
        _window = DataContext as ToolWindowViewModel;
        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowChanged;
        }
        Update();
    }

    private void OnWindowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ToolWindowViewModel.Content))
        {
            Update();
        }
    }

    private void Update()
    {
        var follows = _window?.Kind.FollowsSession == true;
        Pages.Page = follows ? _window!.Kind.Id : null;
        HasSession = follows && _window!.Content is not null;
        HasNoSession = follows && _window!.Content is null;
    }
}
