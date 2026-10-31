using Deventry.Core.FromClaudette;
using Deventry.Core.LoginItems;

namespace Deventry.Platform.FromClaudette;

/// <summary>
/// The names of the login entries Claudette wrote (DESIGN.md §2, "Renaming to Deventry"), for
/// <see cref="CarriedOverLoginItems"/>. The macOS LaunchAgent is named after the bundle identifier, which stays.
/// </summary>
public static class ClaudetteLoginItems
{
    /// <summary>The Run key's value.</summary>
    public const string WindowsValueName = "Claudette";

    /// <summary>The XDG autostart file.</summary>
    public const string LinuxFileName = "claudette.desktop";

    public static ILoginItems CarriedOver(ILoginItems Deventrys, ILoginItems claudettes) => new CarriedOverLoginItems(Deventrys, claudettes);
}
