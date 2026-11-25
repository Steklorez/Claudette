using System.Globalization;
using System.Net;

namespace Deventry.Core.Accounts;

/// <summary>
/// A server that runs models on the user's own computer and answers Anthropic's Messages API, so Claude Code can run on
/// it: Ollama, or llama.cpp's <c>llama-server</c> (DESIGN.md §18, "Ollama and llama.cpp accounts"). Each is a kind of
/// account of its own, with what its server needs.
/// </summary>
public sealed record LocalServerKind
{
    /// <summary>The account kind (<see cref="AccountKinds"/>).</summary>
    public required string Kind { get; init; }

    /// <summary>"Ollama", "llama.cpp": the account's kind as Settings and the header say it.</summary>
    public required string Name { get; init; }

    /// <summary>The program that serves the models: "Ollama", "llama-server".</summary>
    public required string ServerName { get; init; }

    /// <summary>The program's file name, without <c>.exe</c>.</summary>
    public required string Executable { get; init; }

    /// <summary>Where the server listens unless the account says otherwise.</summary>
    public required string DefaultAddress { get; init; }

    /// <summary>
    /// What <c>ANTHROPIC_AUTH_TOKEN</c> is set to for a server without a key. The server ignores it; Claude Code needs a
    /// credential to go without a sign-in, and this one isn't a secret.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>The project's own page.</summary>
    public required string ProjectUrl { get; init; }

    /// <summary>What the project says about running Claude Code on it.</summary>
    public required string DocsUrl { get; init; }
}

/// <summary>The servers an account's model can run on (DESIGN.md §18, "Ollama and llama.cpp accounts"), and their rules.</summary>
public static class LocalServers
{
    public static readonly LocalServerKind Ollama = new()
    {
        Kind = AccountKinds.Ollama,
        Name = "Ollama",
        ServerName = "Ollama",
        Executable = "ollama",
        DefaultAddress = "http://localhost:11434",
        Token = "ollama",
        ProjectUrl = "https://ollama.com",
        DocsUrl = "https://docs.ollama.com/integrations/claude-code",
    };

    public static readonly LocalServerKind LlamaCpp = new()
    {
        Kind = AccountKinds.LlamaCpp,
        Name = "llama.cpp",
        ServerName = "llama-server",
        Executable = "llama-server",
        // llama-server's default port since 1.0.
        DefaultAddress = "http://localhost:9931",
        Token = "llama.cpp",
        ProjectUrl = "https://github.com/ggml-org/llama.cpp",
        DocsUrl = "https://github.com/ggml-org/llama.cpp/blob/master/tools/server/README.md",
    };

    /// <summary>Every kind, in the order Settings lists them.</summary>
    public static readonly IReadOnlyList<LocalServerKind> All = [Ollama, LlamaCpp];

    public static LocalServerKind? Find(string? kind) => All.FirstOrDefault(s => s.Kind == kind);

    /// <summary>
    /// The window a model should run at for Claude Code, as Ollama's own docs ask: its instructions and tools take about
    /// 15,000 tokens before the conversation starts.
    /// </summary>
    public const int RecommendedWindow = 64_000;

    /// <summary>Under this, Claude Code would compact before every turn (<see cref="CompactionPoint"/>).</summary>
    public const int MinimumWindow = 32_000;

    /// <summary>The window an account starts with, and a server Deventry starts runs at, until it's told otherwise.</summary>
    public const int DefaultWindow = 65_536;

    /// <summary>
    /// The longest reply an account lets the model write unless changed (<c>CLAUDE_CODE_MAX_OUTPUT_TOKENS</c>). Claude
    /// Code keeps that much of the window free, so its own default of 32,000 would take half a 64,000-token window.
    /// </summary>
    public const int DefaultLongestReply = 8_192;

    public const int MinimumLongestReply = 1_024;

    public const int MaximumLongestReply = 32_000;

    /// <summary>
    /// Where Claude Code compacts the conversation: the window less the longest reply (up to 20,000) less 13,000, as the
    /// spike measured for Claude Code 2.1.332 (DESIGN.md §18, "The spike").
    /// </summary>
    public static int CompactionPoint(int window, int longestReply) => window - Math.Min(longestReply, 20_000) - 13_000;

    /// <summary>
    /// <paramref name="address"/> as an account keeps it: a scheme (<c>http://</c> when none is given) and no slash at the
    /// end. Null when it isn't an http or https address.
    /// </summary>
    public static string? NormalizeAddress(string? address)
    {
        if (address?.Trim() is not { Length: > 0 } trimmed)
        {
            return null;
        }
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "http://" + trimmed;
        }
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Host.Length > 0
            && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
            ? trimmed.TrimEnd('/')
            : null;
    }

    /// <summary>The address is on this computer, where Deventry can start the server: <c>localhost</c> or a loopback address.</summary>
    public static bool IsOnThisComputer(string? address) =>
        NormalizeAddress(address) is { } normalized && Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
        && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && IPAddress.IsLoopback(ip));

    /// <summary>The port <paramref name="address"/> names, or its scheme's.</summary>
    public static int PortOf(string address) => Uri.TryCreate(NormalizeAddress(address), UriKind.Absolute, out var uri) ? uri.Port : 80;

    /// <summary>
    /// Where the server is, as the header says it: "on this computer", or its host ("on gpu-box.local", "on ollama.com").
    /// </summary>
    public static string Where(string address) =>
        IsOnThisComputer(address) ? "on this computer"
        : Uri.TryCreate(NormalizeAddress(address), UriKind.Absolute, out var uri) ? $"on {uri.Host}" : "";

    /// <summary>
    /// What's wrong with <paramref name="local"/> for <paramref name="server"/>, as Settings says it; null when it's
    /// fine. Doesn't ask the server: whether it has the model is <see cref="LocalModels.ILocalModelServer"/>'s to say.
    /// </summary>
    public static string? Problem(LocalServerKind server, LocalServerSettings local)
    {
        if (NormalizeAddress(local.Address) is null)
        {
            return $"The address is where {server.ServerName} listens, such as {server.DefaultAddress}.";
        }
        if (local.Model.Trim().Length == 0)
        {
            return "Choose the model.";
        }
        if (new[] { local.Model, local.BackgroundModel }.Any(m => m is not null && m.Trim().Any(char.IsWhiteSpace)))
        {
            return "A model's name has a space in it.";
        }
        if (local.Window is > 0 and < MinimumWindow)
        {
            return $"The window is {local.Window.ToString("N0", CultureInfo.CurrentCulture)} tokens: Claude Code needs at least {MinimumWindow.ToString("N0", CultureInfo.CurrentCulture)}, or it compacts before every turn.";
        }
        if (local.LongestReply is < MinimumLongestReply or > MaximumLongestReply)
        {
            return $"The longest reply is between {MinimumLongestReply.ToString("N0", CultureInfo.CurrentCulture)} and {MaximumLongestReply.ToString("N0", CultureInfo.CurrentCulture)} tokens.";
        }
        if (local.Window > 0 && CompactionPoint(local.Window, local.LongestReply) < 16_000)
        {
            return $"With a {local.LongestReply.ToString("N0", CultureInfo.CurrentCulture)}-token longest reply, a {local.Window.ToString("N0", CultureInfo.CurrentCulture)}-token window leaves Claude Code too little room: make the longest reply shorter, or the window bigger.";
        }
        if (server == LlamaCpp && local.StartServer && IsOnThisComputer(local.Address) && local.ServerModel?.Trim() is not { Length: > 0 })
        {
            return "Choose the model llama-server loads when Deventry starts it: a .gguf file, or a model on Hugging Face.";
        }
        return null;
    }
}

/// <summary>
/// What an Ollama or llama.cpp account says about its server and model (DESIGN.md §18, "Ollama and llama.cpp
/// accounts"). Nothing here is secret: a key is in the OS credential store.
/// </summary>
public sealed class LocalServerSettings
{
    /// <summary>Where the server listens, such as <c>http://localhost:11434</c>, or <c>https://ollama.com</c>.</summary>
    public string Address { get; set; } = "";

    /// <summary>The model Claude Code runs on, by the name the server gives it.</summary>
    public string Model { get; set; } = "";

    /// <summary>The model for Claude Code's background work, its titles and other small jobs; null for <see cref="Model"/>.</summary>
    public string? BackgroundModel { get; set; }

    /// <summary>
    /// The tokens the model runs at on the server, which Claude Code is told (<c>CLAUDE_CODE_MAX_CONTEXT_TOKENS</c>) and a
    /// server Deventry starts runs at. 0 when it isn't known.
    /// </summary>
    public int Window { get; set; } = LocalServers.DefaultWindow;

    /// <summary>The longest reply the model may write (<c>CLAUDE_CODE_MAX_OUTPUT_TOKENS</c>).</summary>
    public int LongestReply { get; set; } = LocalServers.DefaultLongestReply;

    /// <summary>No telemetry, error reports, update checks or feature flags from Claude Code, and no WebFetch check with Anthropic.</summary>
    public bool KeepOffline { get; set; }

    /// <summary>The server takes a key, which is in the credential store: llama-server's <c>--api-key</c>, or ollama.com's.</summary>
    public bool HasKey { get; set; }

    /// <summary>Deventry starts the server when it isn't running, for an address on this computer.</summary>
    public bool StartServer { get; set; } = true;

    /// <summary>The server's program, to start it; null to look where its installer puts it, and on <c>PATH</c>.</summary>
    public string? ServerPath { get; set; }

    /// <summary>
    /// For llama.cpp, the model <c>llama-server</c> loads when Deventry starts it: a <c>.gguf</c> file, or a model on
    /// Hugging Face as <c>user/repo:quant</c>.
    /// </summary>
    public string? ServerModel { get; set; }

    /// <summary>The model for background work: <see cref="BackgroundModel"/>, else <see cref="Model"/>.</summary>
    public string BackgroundModelOrMain() => BackgroundModel?.Trim() is { Length: > 0 } background ? background : Model.Trim();

    public LocalServerSettings Copy() => (LocalServerSettings)MemberwiseClone();
}
