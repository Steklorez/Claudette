namespace Deventry.App.Services;

/// <summary>One frame of an <see cref="AppIconAnimation"/>: a PNG, and how long it shows.</summary>
public sealed record AppIconFrame(byte[] Png, TimeSpan Duration);

/// <summary>An animation for the Dock icon or the taskbar button's overlay (DESIGN.md §10).</summary>
/// <param name="Description">What it means, for screen readers.</param>
public sealed record AppIconAnimation(string Name, string Description, IReadOnlyList<AppIconFrame> Frames);

/// <summary>
/// The icon's animations (DESIGN.md §10). <c>packaging/icon/build-icons.mjs</c> draws their frames into
/// <c>Assets/AppIcon</c>, embedded as <c>AppIcon.&lt;name&gt;-&lt;frame&gt;.png</c>.
/// </summary>
public static class AppIconAnimations
{
    /// <summary>
    /// The taskbar overlay while tabs work (Windows): three chevrons in a lighter shade of the sign's blue, lighting one
    /// after another, like an arrow board's.
    /// </summary>
    public static AppIconAnimation Chevrons { get; } = Load("chevrons", "Claude is working", ArrowBoard);

    /// <summary>The Dock icon while tabs work (macOS): the sign's prompt becomes an arrow board's chevrons, lighting in turn.</summary>
    public static AppIconAnimation Working { get; } = Load("working", "Claude is working", ArrowBoard);

    /// <summary>
    /// The Dock icon while a tab needs input (macOS): the sign shows a "?" for a second, then a "!", a "?" and a "!"
    /// quickly, and again.
    /// </summary>
    public static AppIconAnimation Asking { get; } = Load("asking", "A tab needs your input", Milliseconds(1000, 350, 350, 350));

    /// <summary>
    /// The taskbar overlay while a tab waits for a usage limit to reset (Windows): an hourglass whose sand runs slowly
    /// down, rests, and turns over.
    /// </summary>
    public static AppIconAnimation Hourglass { get; } = Load("hourglass", "Waiting for a usage limit to reset", SandRunning);

    /// <summary>The Dock icon while a tab waits for a usage limit to reset (macOS): the sign shows the hourglass.</summary>
    public static AppIconAnimation Waiting { get; } = Load("waiting", "Waiting for a usage limit to reset", SandRunning);

    /// <summary>The hourglass's frames: full, three as the sand runs, empty, and turning over.</summary>
    private static TimeSpan[] SandRunning => Milliseconds(800, 700, 700, 700, 1000, 250);

    /// <summary>The chevrons' frames: one lit, two, all three, held.</summary>
    private static TimeSpan[] ArrowBoard => Milliseconds(300, 300, 300, 700);

    private static TimeSpan[] Milliseconds(params int[] each) => [.. each.Select(ms => TimeSpan.FromMilliseconds(ms))];

    private static AppIconAnimation Load(string name, string description, IReadOnlyList<TimeSpan> durations) =>
        new(name, description, [.. durations.Select((duration, i) => new AppIconFrame(Read($"{name}-{i}.png"), duration))]);

    private static byte[] Read(string file)
    {
        using var stream = typeof(AppIconAnimations).Assembly.GetManifestResourceStream($"AppIcon.{file}")
            ?? throw new InvalidOperationException($"The icon frame {file} isn't embedded. Run packaging/icon/build-icons.mjs.");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
