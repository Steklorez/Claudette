namespace Claudette.App.ViewModels;

/// <summary>
/// The logos Claudette shows in place of an icon it draws, each only where its maker allows it, and as the maker says
/// (DESIGN.md §14, "About", which names them): keys of App.axaml's geometries, filled as their makers draw them, where
/// Claudette's own icons are strokes.
/// </summary>
public static class Logos
{
    /// <summary>
    /// Godot's logo, which the Godot Foundation's trademark policy lets Claudette use to refer to Godot: in the color of
    /// the icons around it.
    /// </summary>
    public const string Godot = "IconProjectGodot";

    /// <summary>
    /// Perforce's symbol, which Perforce lets Claudette show as its brand guidelines say: only in Perforce's own colors,
    /// its mono black or white (App.axaml's PerforceMonoBrush) until its page is selected, then in full color
    /// (PerforceSymbolBrush).
    /// </summary>
    public const string Perforce = "LogoPerforceSymbol";

    /// <summary>Whether the icon <paramref name="key"/> names is a logo, drawn filled.</summary>
    public static bool IsLogo(string? key) => key is Godot or Perforce;
}
