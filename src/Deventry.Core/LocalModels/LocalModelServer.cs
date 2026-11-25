using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Deventry.Core.Accounts;
using Deventry.Core.Json;
using Deventry.Core.Protocol;

namespace Deventry.Core.LocalModels;

/// <summary>Why a server couldn't be asked something, in words to show.</summary>
public sealed class LocalServerException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>A model a server offers, with what it says about it (DESIGN.md §18, "Ollama and llama.cpp accounts").</summary>
/// <param name="Name">The name Claude Code asks for it by.</param>
public sealed record LocalModel(string Name)
{
    /// <summary>Its size on disk, in bytes; null when the server doesn't say.</summary>
    public long? Size { get; init; }

    /// <summary>The longest context it was trained for; null when the server doesn't say.</summary>
    public int? TrainedWindow { get; init; }

    /// <summary>The window it runs at on the server, when the server says (a model's own <c>num_ctx</c>, <c>llama-server</c>'s <c>n_ctx</c>).</summary>
    public int? Window { get; init; }

    /// <summary>The server said what it can do: tools, thinking, images.</summary>
    public bool CapabilitiesKnown { get; init; }

    public bool CanUseTools { get; init; }

    public bool CanThink { get; init; }

    public bool CanSee { get; init; }

    /// <summary>One of Ollama's cloud models, which runs on ollama.com rather than this computer.</summary>
    public bool IsCloud { get; init; }
}

/// <summary>What a server said when asked: its version and its models.</summary>
public sealed record LocalServerReport(string Version, IReadOnlyList<LocalModel> Models);

/// <summary>
/// An Ollama or <c>llama-server</c> an account's model runs on (DESIGN.md §18, "Ollama and llama.cpp accounts"), asked
/// over its own API through <c>AppServices.Http</c>. Every method throws <see cref="LocalServerException"/> with words to
/// show when the server can't be asked.
/// </summary>
public interface ILocalModelServer
{
    LocalServerKind Kind { get; }

    /// <summary>Where it listens.</summary>
    string Address { get; }

    /// <summary>It answers, quickly: never throws.</summary>
    Task<bool> IsAnsweringAsync(CancellationToken cancellationToken = default);

    /// <summary>Its version and the models it has.</summary>
    Task<LocalServerReport> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>What it says about one model, with the window it runs at when that's known; null when it hasn't the model.</summary>
    Task<LocalModel?> DescribeAsync(string model, CancellationToken cancellationToken = default);

    /// <summary>
    /// Has it load <paramref name="model"/>, so the first reply doesn't wait for that, and says the window it loaded with;
    /// null when the server doesn't say.
    /// </summary>
    Task<int?> LoadAsync(string model, CancellationToken cancellationToken = default);
}

/// <summary>Makes the client for an account's server.</summary>
public static class LocalModelServers
{
    /// <param name="key">The server's key, from the credential store; null for one without.</param>
    public static ILocalModelServer For(LocalServerKind kind, string address, HttpClient http, TimeProvider time, string? key = null) =>
        kind == LocalServers.LlamaCpp ? new LlamaCppServer(address, http, time, key) : new OllamaServer(address, http, time, key);
}

/// <summary>The requests both servers' clients make, with their timeouts and their failures in words.</summary>
public abstract class LocalServerClient
{
    /// <summary>How long a quick question may take, such as whether the server answers.</summary>
    public static readonly TimeSpan QuickTimeout = TimeSpan.FromSeconds(3);

    /// <summary>How long an ordinary question may take, such as the models.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>How long loading a model may take: a large one from a slow disk takes minutes.</summary>
    public static readonly TimeSpan LoadTimeout = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly string? _key;

    protected LocalServerClient(LocalServerKind kind, string address, HttpClient http, TimeProvider time, string? key)
    {
        Kind = kind;
        Address = LocalServers.NormalizeAddress(address) ?? throw new ArgumentException($"'{address}' isn't an address.", nameof(address));
        _http = http;
        _time = time;
        _key = key?.Trim() is { Length: > 0 } trimmed ? trimmed : null;
    }

    public LocalServerKind Kind { get; }

    public string Address { get; }

    /// <summary>The address without its scheme, as messages say it: <c>localhost:11434</c>.</summary>
    protected string Where => Address[(Address.IndexOf("://", StringComparison.Ordinal) + 3)..];

    public async Task<bool> IsAnsweringAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, HealthPath, null, QuickTimeout, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (LocalServerException)
        {
            return false;
        }
    }

    /// <summary>What <see cref="IsAnsweringAsync"/> asks for.</summary>
    protected abstract string HealthPath { get; }

    /// <summary>Asks for <paramref name="path"/> and reads its answer as a JSON object.</summary>
    protected async Task<JsonObject> GetJsonAsync(HttpMethod method, string path, JsonObject? body, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(method, path, body, timeout, cancellationToken).ConfigureAwait(false);
        string text;
        try
        {
            text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new LocalServerException($"Couldn't read {Kind.ServerName}'s answer: {ex.Message}", ex);
        }
        if (!response.IsSuccessStatusCode)
        {
            throw new LocalServerException(Failure(response.StatusCode, text));
        }
        return JsonTree.ParseObject(text) ?? throw new LocalServerException($"{Kind.ServerName}'s answer to {path} couldn't be read.");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonObject? body, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, Address + path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (_key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        }
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        using var deadline = new CancellationTokenSource(timeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
        try
        {
            return await _http.SendAsync(request, linked.Token).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new LocalServerException($"{Kind.ServerName} isn't answering at {Where}: {ex.Message}", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new LocalServerException($"{Kind.ServerName} at {Where} didn't answer in time.", ex);
        }
    }

    /// <summary>A failed answer in words, with the server's own message when it gives one.</summary>
    private string Failure(HttpStatusCode status, string body)
    {
        var said = JsonTree.ParseObject(body) is { } json
            ? json.GetString("error") ?? json.GetObject("error")?.GetString("message")
            : null;
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return _key is null
                ? $"{Kind.ServerName} at {Where} wants an API key."
                : $"{Kind.ServerName} at {Where} turned the API key down{(said is null ? "." : $": {said}")}";
        }
        return $"{Kind.ServerName} answered {(int)status}{(said is null ? "." : $": {said}")}";
    }

    /// <summary>A count the server gave as a number, if it fits an int.</summary>
    protected static int? Count(long? value) => value is > 0 and <= int.MaxValue ? (int)value : null;
}
