using Claudette.Core.ProjectTools.Godot;
using Claudette.Core.ProjectTools.Unity;
using Claudette.Core.ProjectTools.Unreal;

namespace Claudette.App.ViewModels;

/// <summary>
/// Each kind of project's icon (DESIGN.md §18, "Project types"): its page in Settings → Project tools, and the project's
/// row at the sidebar's foot. The keys of App.axaml's small vector icons: drawn for what each is known by, unless the
/// engine's maker lets Claudette show its logo (<see cref="Logos"/>), which is the maker's trademark (Settings → About
/// lists them).
/// </summary>
public static class ProjectTypeIcons
{
    /// <summary>A folder with no project Claudette knows: the row at the sidebar's foot is named after it.</summary>
    public const string Folder = "IconToolFolder";

    /// <summary>A type without an icon of its own.</summary>
    public const string Other = "IconSettingsTools";

    /// <summary>The icon for projects of <paramref name="kind"/>; for none, the folder's.</summary>
    public static string For(string? kind) => kind switch
    {
        null => Folder,
        // Unreal Engine: an engine's gear.
        UnrealProvider.KindId => "IconProjectUnreal",
        // Unity: a cube, what its projects are known by.
        UnityProvider.KindId => "IconProjectUnity",
        GodotProvider.KindId => Logos.Godot,
        _ => Other,
    };
}
