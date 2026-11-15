using Avalonia;
using Avalonia.Controls;
using Deventry.App.ViewModels;

namespace Deventry.App.Views;

/// <summary>A tab's token breakdown in a window of its own (DESIGN.md §18, "The window").</summary>
public partial class TokenBreakdownWindow : Window
{
    /// <summary>Narrower than this, the right column goes under the left.</summary>
    private const double TwoColumns = 1100;

    private TokenBreakdownViewModel? _viewModel;

    public TokenBreakdownWindow()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
        {
            _viewModel.TurnShown -= OnTurnShown;
        }
        _viewModel = DataContext as TokenBreakdownViewModel;
        if (_viewModel is not null)
        {
            _viewModel.TurnShown += OnTurnShown;
        }
    }

    /// <summary>The columns side by side, or the right one under the left in a narrow window.</summary>
    private void Arrange(double width)
    {
        var narrow = width < TwoColumns;
        Grid.SetColumn(RightColumn, narrow ? 0 : 2);
        Grid.SetRow(RightColumn, narrow ? 1 : 0);
        Grid.SetColumnSpan(LeftColumn, narrow ? 3 : 1);
        Grid.SetColumnSpan(RightColumn, narrow ? 3 : 1);
        RightColumn.Margin = narrow ? new Thickness(0, 12, 0, 0) : default;
    }

    /// <summary>A turn asked for, from the chart or a finding: its row, opened, brought into view.</summary>
    private void OnTurnShown(TokenTurnRow row) =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (TurnList.ContainerFromItem(row) is { } container)
            {
                container.BringIntoView(new Rect(0, 0, container.Bounds.Width, Math.Min(container.Bounds.Height, 120)));
            }
        }, Avalonia.Threading.DispatcherPriority.Loaded);
}
