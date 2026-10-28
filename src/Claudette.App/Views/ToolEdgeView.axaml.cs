using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Claudette.App.ViewModels.ToolWindows;

namespace Claudette.App.Views;

/// <summary>
/// An edge's tool windows, docked or one slid out (DESIGN.md §3, "Tool windows"). The shell has one of each for every
/// edge; only the one on screen holds the window showing, so its page is made once.
/// </summary>
public partial class ToolEdgeView : UserControl
{
    /// <summary>The session's group color as a brush, for the dot beside its name; none without one.</summary>
    public static readonly IValueConverter ColorBrush = new FuncValueConverter<Color?, IBrush?>(c => c is { } color ? new SolidColorBrush(color) : null);

    /// <summary>It's the window slid out over the panes, rather than the edge docked beside them.</summary>
    public static readonly StyledProperty<bool> IsSlideProperty = AvaloniaProperty.Register<ToolEdgeView, bool>(nameof(IsSlide));

    private ToolEdgeViewModel? _edge;

    public ToolEdgeView() => InitializeComponent();

    public bool IsSlide
    {
        get => GetValue(IsSlideProperty);
        set => SetValue(IsSlideProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSlideProperty)
        {
            ShowWindow();
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_edge is not null)
        {
            _edge.PropertyChanged -= OnEdgeChanged;
        }
        _edge = DataContext as ToolEdgeViewModel;
        if (_edge is not null)
        {
            _edge.PropertyChanged += OnEdgeChanged;
        }
        ShowWindow();
    }

    private void OnEdgeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ToolEdgeViewModel.Showing) or nameof(ToolEdgeViewModel.IsSlidOut) or nameof(ToolEdgeViewModel.IsDocked)
            or nameof(ToolEdgeViewModel.IsAutoHide) or nameof(ToolEdgeViewModel.HasTabs))
        {
            ShowWindow();
        }
    }

    /// <summary>The window showing, while this one is on screen: docked while the edge is, slid out while one is.</summary>
    private void ShowWindow()
    {
        var onScreen = _edge is { } edge && (IsSlide ? edge.IsSlidOut : edge.IsDocked);
        Window.Content = onScreen ? _edge!.Showing : null;
    }
}