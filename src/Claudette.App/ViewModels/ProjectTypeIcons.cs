using Claudette.Core.ProjectTools.Godot;
using Claudette.Core.ProjectTools.Unity;
using Claudette.Core.ProjectTools.Unreal;

namespace Claudette.App.ViewModels;

/// <summary>
/// Each kind of project's icon (DESIGN.md §18, "Project types"): its page in Settings → Project tools, and the project's
/// row at the sidebar's foot. The keys of App.axaml's small vector icons: drawn for what each is known by, unless the
/// engine's maker lets Claudette show its logo, which is the maker's trademark (Settings → About lists them).
/// </summary>
public static class ProjectTypeIcons
{
    /// <summary>A folder with no project Claudette knows: the row at the sidebar's foot is named after it.</summary>
    public const string Folder = "IconToolFolder";

    /// <summary>A type without an icon of its own.</summary>
    public const string Other = "IconSettingsTools";

    /// <summary>Godot's logo, which the Godot Foundation's trademark policy lets Claudette use to refer to Godot.</summary>
    public const string Godot = "IconProjectGodot";

    /// <summary>The icons that are logos: filled as their makers draw them, where the others are strokes.</summary>
    private static readonly HashSet<string> Logos = [Godot];

    /// <summary>The icon for projects of <paramref name="kind"/>; for none, the folder's.</summary>
    public static string For(string? kind) => kind switch
    {
        null => Folder,
        // Unreal Engine: an engine's gear.
        UnrealProvider.KindId => "IconProjectUnreal",
        // Unity: a cube, what its projects are known by.
        UnityProvider.KindId => "IconProjectUnity",
        GodotProvider.KindId => Godot,
        _ => Other,
    };

    /// <summary>Whether the icon <paramref name="key"/> names is a logo, drawn filled.</summary>
    public static bool IsLogo(string? key) => key is not null && Logos.Contains(key);
}
