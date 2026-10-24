using System.ComponentModel;
using Avalonia.Controls;
using Claudette.App.Mascot;
using Claudette.App.ViewModels.Settings;

namespace Claudette.App.Views.Settings;

/// <summary>
/// Settings → Appearance's preview (DESIGN.md §14): a little of a tab in the chosen look. Claudette waves from the
/// message box's edge, at her size.
/// </summary>
public partial class AppearancePreview : UserControl
{
    /// <summary>She waves in the preview, standing still: it shows her size, not what she does.</summary>
    private const string Pose = "wave";

    private readonly MascotFigure _claudette = new(front: false) { Frame = new MascotFrame(Pose, 0, 0, []), IsHitTestVisible = false };
    private AppearancePage? _page;

    public AppearancePreview()
    {
        InitializeComponent();
        MascotHost.Children.Add(_claudette);
        Size(3);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_page is not null)
        {
            _page.PropertyChanged -= OnPageChanged;
        }
        _page = DataContext as AppearancePage;
        if (_page is not null)
        {
            _page.PropertyChanged += OnPageChanged;
            Size(_page.ClaudetteCell);
        }
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_page is not null && e.PropertyName is nameof(AppearancePage.ClaudetteCell) or "" or null)
        {
            Size(_page.ClaudetteCell);
        }
    }

    private void Size(double cell)
    {
        _claudette.Cell = cell;
        _claudette.Width = MascotArt.Width * cell;
        _claudette.Height = MascotArt.HeightOf(Pose) * cell;
    }
}
