using System.Text;
using Deventry.Core.Demo;

namespace Deventry.App.Demo;

/// <summary>
/// Deventry run as its demo's <c>codex</c> (DESIGN.md §11, "Demo mode"): <c>Program.Main</c> runs this before anything
/// else when <see cref="StandInCodex.Variable"/> is set, so it opens no window and hands nothing to a running Deventry.
/// </summary>
internal static class StandInCodexLaunch
{
    public static bool IsStandIn => Environment.GetEnvironmentVariable(StandInCodex.Variable) == "1";

    public static int Run(string[] args)
    {
        // UTF-8 both ways, as Codex's JSON-RPC is.
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var input = new StreamReader(Console.OpenStandardInput(), utf8);
        using var output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false };
        // The demo's own CODEX_HOME, which its Deventry was started with: never the user's.
        var home = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } folder ? folder : Path.Combine(Environment.CurrentDirectory, ".codex-demo");
        var standIn = new StandInCodex(input, output, TimeProvider.System, home, Environment.CurrentDirectory);
        return standIn.RunAsync(args).GetAwaiter().GetResult();
    }
}
