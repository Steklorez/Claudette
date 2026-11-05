using Avalonia.Controls;

namespace Deventry.App.Views;

public partial class CloneRepositoryView : UserControl
{
    public CloneRepositoryView()
    {
        InitializeComponent();
        // Once the overlay showing it is visible too: the view is attached before the overlay is.
        AttachedToVisualTree += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => UrlBox.Focus());
    }
}
