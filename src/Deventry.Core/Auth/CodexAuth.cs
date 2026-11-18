using Deventry.Core.Claude;
using Deventry.Core.Processes;

namespace Deventry.Core.Auth;

/// <summary>Whether Codex is signed in, from <c>codex login status</c>, and how it says so.</summary>
public sealed record CodexSignIn(bool SignedIn, string Text);

/// <summary>
/// Codex's own sign-in (DESIGN.md §18, "Codex, a second agent", "Installing and signing in"): <c>codex login</c>, which
/// opens the browser, <c>codex login status</c> and <c>codex logout</c>, run as the user would in a terminal. Deventry
/// never signs in through the app-server's account methods.
/// </summary>
/// <param name="environmentOverrides">Variables set (or, with null, removed) on top of the user environment, as a tab's <c>codex</c> gets them.</param>
public sealed class CodexAuth(
    string codexPath,
    IProcessLauncher launcher,
    TimeProvider timeProvider,
    UserEnvironment? userEnvironment = null,
    IReadOnlyDictionary<string, string?>? environmentOverrides = null)
{
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(20);

    /// <summary>How long a browser sign-in may take before Deventry gives up waiting for it.</summary>
    public static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(10);

    public async Task<CodexSignIn> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["login", "status"], StatusTimeout, cancellationToken).ConfigureAwait(false);
        var text = (result.StandardOutput + result.StandardError).Trim();
        return new CodexSignIn(result.ExitCode == 0 && !text.Contains("Not logged in", StringComparison.OrdinalIgnoreCase), text.Length > 0 ? text : "Not logged in");
    }

    /// <summary>Signs in with the browser, as <c>codex login</c> does in a terminal; returns once Codex has.</summary>
    public async Task<CodexSignIn> LoginAsync(Action<string>? onOutput = null, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["login"], LoginTimeout, cancellationToken, onOutput).ConfigureAwait(false);
        return result.ExitCode == 0 ? await GetStatusAsync(cancellationToken).ConfigureAwait(false) : new CodexSignIn(false, (result.StandardError + result.StandardOutput).Trim());
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default) =>
        await RunAsync(["logout"], StatusTimeout, cancellationToken).ConfigureAwait(false);

    private async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken, Action<string>? onOutput = null)
    {
        var baseEnvironment = userEnvironment is null ? null : await userEnvironment.GetAsync(cancellationToken).ConfigureAwait(false);
        var spec = new ProcessStartSpec(codexPath, arguments) { Environment = ClaudeEnvironment.From(baseEnvironment, environmentOverrides) };
        return await ProcessRunner.RunAsync(launcher, spec, timeout, timeProvider, cancellationToken, onOutput).ConfigureAwait(false);
    }
}
