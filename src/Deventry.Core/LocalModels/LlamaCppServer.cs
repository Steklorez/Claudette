using System.Text.Json.Nodes;
using Deventry.Core.Accounts;
using Deventry.Core.Protocol;

namespace Deventry.Core.LocalModels;

/// <summary>
/// llama.cpp's <c>llama-server</c>, through its own API (DESIGN.md §18, "Ollama and llama.cpp accounts"): <c>/health</c>,
/// which answers without a key, then <c>/props</c> for its build, the window the model runs at and what it can do, and
/// <c>/v1/models</c> for the model's name. It loads its model as it starts, so loading one is only asking its window.
/// </summary>
public sealed class LlamaCppServer(string address, HttpClient http, TimeProvider time, string? key = null)
    : LocalServerClient(LocalServers.LlamaCpp, address, http, time, key), ILocalModelServer
{
    protected override string HealthPath => "/health";

    public async Task<LocalServerReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var props = await GetJsonAsync(HttpMethod.Get, "/props", null, Timeout, cancellationToken).ConfigureAwait(false);
        var listed = await GetJsonAsync(HttpMethod.Get, "/v1/models", null, Timeout, cancellationToken).ConfigureAwait(false);
        var model = Describe(props);
        var names = listed.GetArray("data")?.OfType<JsonObject>().Select(m => m.GetString("id")).OfType<string>().Where(id => id.Length > 0).ToList() ?? [];
        if (names.Count == 0 && props.GetString("model_alias") is { Length: > 0 } alias)
        {
            names.Add(alias);
        }
        return new LocalServerReport(props.GetString("build_info") ?? "", [.. names.Select(n => model with { Name = NameOf(n) })]);
    }

    /// <summary>
    /// What <c>/props</c> says about the model it serves: the window it runs at (<c>n_ctx</c>), whether its chat template
    /// takes tools, and whether it can see.
    /// </summary>
    internal static LocalModel Describe(JsonObject props)
    {
        var caps = props.GetObject("chat_template_caps");
        return new LocalModel("")
        {
            Window = Count(props.GetObject("default_generation_settings")?.GetLong("n_ctx")),
            CapabilitiesKnown = caps is not null,
            CanUseTools = caps?.GetBool("supports_tools") ?? caps?.GetBool("supports_tool_calls") ?? false,
            // Every chat template llama-server takes passes a model's thinking through.
            CanThink = true,
            CanSee = props.GetObject("modalities")?.GetBool("vision") ?? false,
        };
    }

    /// <summary>
    /// The name a model goes by. Without <c>--alias</c> llama-server names it by its file's path, which would put spaces
    /// in Claude Code's arguments; with one model it answers to any name, so the file's name stands in.
    /// </summary>
    internal static string NameOf(string id) =>
        id.Contains('/', StringComparison.Ordinal) && id.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
        || id.Contains('\\', StringComparison.Ordinal)
            ? Path.GetFileNameWithoutExtension(id.Replace('\\', '/').Split('/')[^1])
            : id;

    public async Task<LocalModel?> DescribeAsync(string model, CancellationToken cancellationToken = default)
    {
        var report = await CheckAsync(cancellationToken).ConfigureAwait(false);
        return report.Models.FirstOrDefault(m => m.Name == model) ?? (report.Models.Count == 1 ? report.Models[0] with { Name = model } : null);
    }

    public async Task<int?> LoadAsync(string model, CancellationToken cancellationToken = default) =>
        Describe(await GetJsonAsync(HttpMethod.Get, "/props", null, Timeout, cancellationToken).ConfigureAwait(false)).Window;
}
