using System.ComponentModel;
using Deventry.Core.Processes;

namespace Deventry.App.Tests.Support;

/// <summary>
/// Around the harness's launcher, real processes or the test's own: everything but <c>p4</c> and <c>codex</c>, which are
/// never run, as if they weren't installed. A real <c>codex</c> would read the user's own Codex folder and sign-in. A Files tab and a session tab look for their Perforce workspace by themselves (DESIGN.md §18,
/// "Finding the workspace"), and on a machine with Perforce set up that would reach a real server, or be one more
/// process for a test counting what its launcher started. Tests that want Perforce give the harness <c>FakeP4</c>.
/// </summary>
internal sealed class NoRealP4Launcher(IProcessLauncher inner) : IProcessLauncher
{
    public IRunningProcess Start(ProcessStartSpec spec)
    {
        var name = Path.GetFileNameWithoutExtension(spec.FileName);
        if (string.Equals(name, "p4", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "p4v", StringComparison.OrdinalIgnoreCase))
        {
            throw new Win32Exception(2, "Tests never run the real p4.");
        }
        if (string.Equals(name, "codex", StringComparison.OrdinalIgnoreCase))
        {
            throw new Win32Exception(2, "Tests never run the real codex.");
        }
        return inner.Start(spec);
    }
}
