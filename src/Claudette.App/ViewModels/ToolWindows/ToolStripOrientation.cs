using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>How an auto-hidden edge's tabs lie (DESIGN.md §3, "Tool windows"): down the right edge, reading downwards, or along the bottom.</summary>
public static class ToolStripOrientation
{
    /// <summary>The right edge's tabs stack downwards; the bottom's run along it.</summary>
    public static readonly IValueConverter Converter = new FuncValueConverter<bool, Orientation>(right => right ? Orientation.Vertical : Orientation.Horizontal);

    /// <summary>A right edge tab's title turns to read downwards, as Visual Studio's do.</summary>
    public static readonly IValueConverter Rotation = new FuncValueConverter<ToolEdge, ITransform?>(edge => edge == ToolEdge.Right ? new RotateTransform(90) : null);
}
