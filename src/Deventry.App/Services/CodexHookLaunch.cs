using Deventry.Core.Codex;

namespace Deventry.App.Services;

/// <summary>
/// Deventry started by Codex as a tab's hook before a patch (DESIGN.md §18, "Perforce"): it passes what Codex gave it on
/// standard input to the tab whose <c>codex</c> ran it, prints the tab's answer, and exits. No window, and no hand-off
/// to a running Deventry: Codex reads the answer from this process's output.
/// </summary>
internal static class CodexHookLaunch
{
    /// <returns>The exit code: 0 always, since a hook that fails would stop the patch.</returns>
    public static int Run(TextReader? input = null, TextWriter? output = null, string? pipeName = null)
    {
        input ??= Console.In;
        output ??= Console.Out;
        var answer = CodexHooks.RelayAsync(input.ReadToEnd(), pipeName ?? Environment.GetEnvironmentVariable(CodexHooks.PipeVariable), TimeProvider.System)
            .GetAwaiter().GetResult();
        output.Write(answer);
        output.Flush();
        return 0;
    }
}
