using System.Text.Json.Nodes;

namespace Deventry.App.Tests;

/// <summary>
/// The image window decodes animations with SkiaSharp (DESIGN.md §18, "Opening images"), which Avalonia renders with
/// too, so the app's SkiaSharp is the version Avalonia.Skia takes. Linux's native library comes only through
/// Avalonia.Skia, and a SkiaSharp of another version won't load it, so nothing draws there, while Windows' and macOS's
/// native libraries follow the app's SkiaSharp and carry on. This checks the lock file, so it fails on every system.
/// </summary>
public class SkiaSharpVersionTests
{
    [Fact]
    public void SkiaSharp_and_its_native_libraries_are_the_version_Avalonia_Skia_takes()
    {
        var lockFile = JsonNode.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "src", "Deventry.App", "packages.lock.json")))!;
        var mismatched = new List<string>();
        foreach (var (target, packages) in lockFile["dependencies"]!.AsObject())
        {
            var avalonia = (string?)packages!["Avalonia.Skia"]?["dependencies"]?["SkiaSharp"];
            Assert.NotNull(avalonia);
            mismatched.AddRange(packages.AsObject()
                .Where(p => p.Key == "SkiaSharp" || p.Key.StartsWith("SkiaSharp.NativeAssets.", StringComparison.Ordinal))
                .Select(p => (Name: p.Key, Version: (string?)p.Value!["resolved"]))
                .Where(p => p.Version != avalonia)
                .Select(p => $"{target}: {p.Name} {p.Version}, but Avalonia.Skia takes {avalonia}"));
        }

        Assert.Empty(mismatched);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Deventry.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("Couldn't find the repository root.");
    }
}
