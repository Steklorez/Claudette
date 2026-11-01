using Deventry.Core.FromClaudette;
using Deventry.Core.LoginItems;

namespace Deventry.Platform.FromClaudette;

/// <summary>
/// The names of the login entries Claudette wrote (DESIGN.md §2, "Renaming to Deventry"), for
/// <see cref="CarriedOverLoginItems"/>.
/// </summary>
public static class ClaudetteLoginItems
{
    /// <summary>The Run key's value.</summary>
    public const string WindowsValueName = "Claudette";

    /// <summary>The XDG autostart file.</summary>
    public const string LinuxFileName = "claudette.desktop";

    /// <summary>Claudette's bundle identifier, which Deventry's isn't.</summary>
    public const string MacBundleIdentifier = "com.dotbunny.claudette";

    /// <summary>The LaunchAgent, named after Claudette's bundle identifier.</summary>
    public const string MacLabel = MacBundleIdentifier + ".login";

    public static ILoginItems CarriedOver(ILoginItems Deventrys, ILoginItems claudettes) => new CarriedOverLoginItems(Deventrys, claudettes);
}
