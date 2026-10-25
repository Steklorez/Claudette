using System.Reflection;
using System.Xml.Linq;

namespace Claudette.App.ViewModels.Settings;

/// <summary>Something Claudette ships that someone else made: a package, the runtime, the font or a logo.</summary>
/// <param name="Name">The package's id, or what it is: "Avalonia", "Inter", "Godot Engine logo".</param>
/// <param name="Version">The package's version; empty for what isn't a package.</param>
/// <param name="License">Its SPDX license, such as "MIT" or "Apache-2.0"; empty when its package only has a file.</param>
/// <param name="Copyright">Whose it is, as its package says.</param>
/// <param name="Url">Its project's page or repository; empty when its package names neither.</param>
/// <param name="Note">What it is to Claudette, for what isn't a package.</param>
/// <param name="LicenseText">The license's text, with the package's own copyright where it has its own; null for a license that asks only for a link.</param>
/// <param name="LicenseUrl">Where the license is, for one that asks only for a link (CC BY).</param>
/// <param name="NoticesText">The third-party notices its package carries, for what it's built from in turn.</param>
public sealed record ThirdPartyLibrary(
    string Name, string Version, string License, string Copyright, string Url, string Note, string? LicenseText, string? LicenseUrl, string? NoticesText);

/// <summary>
/// The licenses of what Claudette ships (DESIGN.md §14, "About"), from the ThirdPartyNotices.xml that the build writes
/// from the packages and Licenses/extras.xml (ThirdPartyNotices.targets) and embeds in the app.
/// </summary>
public static class ThirdPartyNotices
{
    private const string ResourceName = "ThirdPartyNotices.xml";

    /// <summary>Every library, by name, ignoring case.</summary>
    public static IReadOnlyList<ThirdPartyLibrary> Load()
    {
        using var stream = typeof(ThirdPartyNotices).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} isn't in the app: ThirdPartyNotices.targets writes it.");
        return Read(XDocument.Load(stream));
    }

    /// <summary>Claudette's own license, LICENSE at the top of its repository.</summary>
    public static string ClaudetteLicense()
    {
        using var stream = typeof(ThirdPartyNotices).Assembly.GetManifestResourceStream("License.txt")
            ?? throw new InvalidOperationException("License.txt isn't in the app: Claudette.App.csproj embeds the repository's LICENSE.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    /// <summary>Whose Claudette is: the copyright the build gives it (Directory.Build.props).</summary>
    public static string ClaudetteCopyright =>
        typeof(ThirdPartyNotices).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    internal static IReadOnlyList<ThirdPartyLibrary> Read(XDocument document)
    {
        var root = document.Root!;
        var texts = root.Elements("text").ToDictionary(t => (string)t.Attribute("id")!, t => t.Value);
        string? Text(XElement library, string name) => (string?)library.Attribute(name) is { } id ? texts.GetValueOrDefault(id) : null;
        string Value(XElement library, string name) => (string?)library.Attribute(name) ?? "";
        return
        [
            .. root.Elements("library").Select(l => new ThirdPartyLibrary(
                Value(l, "name"), Value(l, "version"), Value(l, "license"), Value(l, "copyright"), Value(l, "url"), Value(l, "note"),
                Text(l, "licenseText"), (string?)l.Attribute("licenseUrl"), Text(l, "noticesText"))),
        ];
    }
}
