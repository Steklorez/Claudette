using Deventry.Core.Installation;

namespace Deventry.App.Tests.Support;

/// <summary>
/// Plays Codex's installer for Settings → Agents → Codex (DESIGN.md §18, "Installing and signing in"), so no test runs
/// the real one: what it prints, whether it works, and what it does once it has.
/// </summary>
internal sealed class FakeCodexInstaller : ICodexInstaller
{
    public string CommandText => CodexInstaller.WindowsCommand;

    public int Installs { get; private set; }

    public string[] Output { get; set; } = ["Installing Codex...", "Installed codex 0.169.0"];

    public bool Fails { get; set; }

    /// <summary>What installing does besides printing, such as making Deventry find a <c>codex</c>.</summary>
    public Action? OnInstall { get; set; }

    public Task<CodexInstallResult> InstallAsync(Action<string>? onOutput = null, CancellationToken cancellationToken = default)
    {
        Installs++;
        foreach (var line in Output)
        {
            onOutput?.Invoke(line);
        }
        if (Fails)
        {
            return Task.FromResult(new CodexInstallResult(false, $"'{CommandText}' failed (exit code 1). Couldn't download codex"));
        }
        OnInstall?.Invoke();
        return Task.FromResult(new CodexInstallResult(true, "Codex is installed."));
    }
}
