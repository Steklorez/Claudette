using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Deventry.App.Mascot;
using Deventry.App.ViewModels;

namespace Deventry.App.Views;

/// <summary>
/// The closing card (DESIGN.md §13, "What closing does, on screen"). Joulene stands on its top edge in her size, on
/// whole screen pixels as on the composer, and her figure follows the frame the view model gives her.
/// </summary>
public partial class ClosingView : UserControl
{
    private readonly MascotFigure _figure = new(front: false) { IsHitTestVisible = false, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
    private ClosingViewModel? _viewModel;

    public ClosingView()
    {
        InitializeComponent();
        Stand.Children.Add(_figure);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }
        _viewModel = DataContext as ClosingViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
        }
        ShowJoulene();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // Her cell size depends on the screen's scale.
        ShowJoulene();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClosingViewModel.Frame))
        {
            _figure.Frame = _viewModel?.Frame;
        }
    }

    private void ShowJoulene()
    {
        var frame = _viewModel?.Frame;
        Stand.IsVisible = frame is not null;
        if (_viewModel is null || frame is null)
        {
            return;
        }
        // A cell is a whole number of the screen's pixels, near her size at the display's scale, as on the composer.
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        var cell = Math.Max(1, Math.Round(_viewModel.PixelsPerCell * scale, MidpointRounding.AwayFromZero)) / scale;
        _figure.Cell = cell;
        _figure.Width = MascotArt.Width * cell;
        _figure.Height = MascotArt.Height * cell;
        Stand.Height = MascotArt.Height * cell;
        _figure.Frame = frame;
    }
}
