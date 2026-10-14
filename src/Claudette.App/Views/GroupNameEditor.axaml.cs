using Avalonia;
using Avalonia.Controls;

namespace Claudette.App.Views;

public partial class GroupNameEditor : UserControl
{
    public GroupNameEditor() => InitializeComponent();

    /// <summary>The name is ready to type over, as a tab's is when it's renamed.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        });
    }
}
