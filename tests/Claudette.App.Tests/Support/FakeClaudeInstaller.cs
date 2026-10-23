using Claudette.Core.Installation;

namespace Claudette.App.Tests.Support;

/// <summary>
/// Plays Claude Code's installer for the setup screen (DESIGN.md §12, "Installing Claude Code"), so no test runs the real
/// one: what it prints, whether it works, and what it does once it has.
/// </summary>
internal sealed class FakeClaudeInstaller : IClaudeInstaller
{
    public string CommandText => ClaudeInstaller.WindowsCommand;

    public int Installs { get; private set; }

    public string[] Output { get; set; } = ["Setting up Claude Code...", "✅ Installation complete!"];

    public bool Fails { get; set; }

    /// <summary>What installing does besides printing, such as putting a <c>claude</c> where Claudette looks.</summary>
    public Action? OnInstall { get; set; }

    public Task<ClaudeInstallResult> InstallAsync(Action<string>? onOutput = null, CancellationToken cancellationToken = default)
    {
        Installs++;
        foreach (var line in Output)
        {
            onOutput?.Invoke(line);
        }
        if (Fails)
        {
            return Task.FromResult(new ClaudeInstallResult(false, $"'{CommandText}' failed (exit 1). Failed to get latest version"));
        }
        OnInstall?.Invoke();
        return Task.FromResult(new ClaudeInstallResult(true, "Claude Code is installed."));
    }
}
