using Claudette.App.ViewModels;

namespace Claudette.App.Tests.Support;

/// <summary>A log's lines as the file has them, for tests that look for a line's text.</summary>
internal static class LogLines
{
    public static IReadOnlyList<string> Lines(this LogTabViewModel log) => log.Output.Select(l => l.Text).ToArray();
}
