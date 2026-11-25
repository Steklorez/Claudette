using System.Globalization;
using System.Text.Json.Nodes;
using Deventry.Core.Accounts;
using Deventry.Core.Protocol;

namespace Deventry.Core.LocalModels;

/// <summary>
/// Ollama, through its own API (DESIGN.md §18, "Ollama and llama.cpp accounts"): <c>/api/version</c>, the models in
/// <c>/api/tags</c> and <c>/api/show</c>, loading one with <c>/api/generate</c> and reading the window it loaded with
/// from <c>/api/ps</c>, and a copy of a model with a window of its own (<c>/api/create</c>). The same API answers at
/// ollama.com, with a key.
/// </summary>
public sealed class OllamaServer(string address, HttpClient http, TimeProvider time, string? key = null)
    : LocalServerClient(LocalServers.Ollama, address, http, time, key), ILocalModelServer
{
    protected override string HealthPath => "/api/version";

    public async Task<LocalServerReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        var version = (await GetJsonAsync(HttpMethod.Get, "/api/version", null, Timeout, cancellationToken).ConfigureAwait(false)).GetString("version") ?? "";
        var tags = await GetJsonAsync(HttpMethod.Get, "/api/tags", null, Timeout, cancellationToken).ConfigureAwait(false);
        var models = new List<LocalModel>();
        foreach (var entry in tags.GetArray("models")?.OfType<JsonObject>() ?? [])
        {
            if (FromTags(entry) is not { } model)
            {
                continue;
            }
            // Ollamas before capabilities were in the list say them only for one model at a time.
            if (!model.CapabilitiesKnown && await DescribeAsync(model.Name, cancellationToken).ConfigureAwait(false) is { } described)
            {
                model = model with
                {
                    CapabilitiesKnown = described.CapabilitiesKnown,
                    CanUseTools = described.CanUseTools,
                    CanThink = described.CanThink,
                    CanSee = described.CanSee,
                    TrainedWindow = model.TrainedWindow ?? described.TrainedWindow,
                };
            }
            models.Add(model);
        }
        return new LocalServerReport(version, models);
    }

    /// <summary>A model as <c>/api/tags</c> lists it.</summary>
    internal static LocalModel? FromTags(JsonObject entry)
    {
        if ((entry.GetString("name") ?? entry.GetString("model")) is not { Length: > 0 } name)
        {
            return null;
        }
        var capabilities = entry["capabilities"] is JsonArray ? entry.GetStringList("capabilities") : null;
        return WithCapabilities(new LocalModel(name)
        {
            Size = entry.GetLong("size"),
            TrainedWindow = Count(entry.GetObject("details")?.GetLong("context_length")),
            IsCloud = IsCloudModel(name, entry),
        }, capabilities);
    }

    public async Task<LocalModel?> DescribeAsync(string model, CancellationToken cancellationToken = default)
    {
        JsonObject show;
        try
        {
            show = await GetJsonAsync(HttpMethod.Post, "/api/show", new JsonObject { ["model"] = model }, Timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (LocalServerException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains(" 404", StringComparison.Ordinal))
        {
            return null;
        }
        return FromShow(model, show);
    }

    /// <summary>A model as <c>/api/show</c> describes it: its window from its own <c>num_ctx</c>, when it has one.</summary>
    internal static LocalModel FromShow(string model, JsonObject show)
    {
        var info = show.GetObject("model_info");
        var trained = info?.FirstOrDefault(p => p.Key.EndsWith(".context_length", StringComparison.Ordinal)).Value is JsonValue value
            && value.TryGetValue<long>(out var length) ? Count(length) : null;
        return WithCapabilities(new LocalModel(model)
        {
            TrainedWindow = trained,
            Window = NumCtx(show.GetString("parameters")),
            IsCloud = IsCloudModel(model, show),
        }, show["capabilities"] is JsonArray ? show.GetStringList("capabilities") : null);
    }

    /// <summary>The <c>num_ctx</c> in a model's parameters, which Ollama gives as lines of a name and a value.</summary>
    internal static int? NumCtx(string? parameters)
    {
        foreach (var line in (parameters ?? "").Split('\n'))
        {
            var parts = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts is ["num_ctx", var number] && int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var window) && window > 0)
            {
                return window;
            }
        }
        return null;
    }

    private static LocalModel WithCapabilities(LocalModel model, IReadOnlyList<string>? capabilities) => capabilities is null ? model : model with
    {
        CapabilitiesKnown = true,
        CanUseTools = capabilities.Contains("tools"),
        CanThink = capabilities.Contains("thinking"),
        CanSee = capabilities.Contains("vision"),
    };

    /// <summary>A cloud model runs on ollama.com: its name says so (<c>gpt-oss:120b-cloud</c>, <c>glm-4.7:cloud</c>), or the entry names where.</summary>
    private static bool IsCloudModel(string name, JsonObject entry) =>
        name.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase)
        || entry.GetString("remote_host") is { Length: > 0 } || entry.GetString("remote_model") is { Length: > 0 };

    public async Task<int?> LoadAsync(string model, CancellationToken cancellationToken = default)
    {
        // An empty prompt loads the model and says nothing; it stays loaded half an hour.
        await GetJsonAsync(HttpMethod.Post, "/api/generate", new JsonObject { ["model"] = model, ["prompt"] = "", ["keep_alive"] = "30m", ["stream"] = false },
            LoadTimeout, cancellationToken).ConfigureAwait(false);
        var running = await GetJsonAsync(HttpMethod.Get, "/api/ps", null, Timeout, cancellationToken).ConfigureAwait(false);
        return running.GetArray("models")?.OfType<JsonObject>()
            .Where(m => SameModel(m.GetString("name"), model) || SameModel(m.GetString("model"), model))
            .Select(m => Count(m.GetLong("context_length")))
            .FirstOrDefault(w => w is not null);
    }

    /// <summary>Ollama writes a model without a tag as <c>:latest</c>.</summary>
    internal static bool SameModel(string? a, string b) =>
        a is not null && string.Equals(WithTag(a), WithTag(b), StringComparison.OrdinalIgnoreCase);

    private static string WithTag(string name) => name.Contains(':', StringComparison.Ordinal) ? name : name + ":latest";

    /// <summary>
    /// Makes a copy of <paramref name="model"/> that runs at <paramref name="window"/> tokens whatever Ollama's own setting
    /// is, named after it (<c>qwen3:8b-64k</c>). It shares the model's files: nothing is downloaded or copied.
    /// </summary>
    /// <returns>The copy's name.</returns>
    public async Task<string> CopyWithWindowAsync(string model, int window, CancellationToken cancellationToken = default)
    {
        var name = CopyName(model, window);
        var answer = await GetJsonAsync(HttpMethod.Post, "/api/create",
            new JsonObject { ["model"] = name, ["from"] = model, ["parameters"] = new JsonObject { ["num_ctx"] = window }, ["stream"] = false },
            LoadTimeout, cancellationToken).ConfigureAwait(false);
        return answer.GetString("status") == "success"
            ? name
            : throw new LocalServerException($"Ollama didn't make {name}: {answer.GetString("status") ?? answer.GetString("error") ?? "it said nothing"}.");
    }

    /// <summary>A copy's name: <c>qwen3:8b</c> at 65,536 tokens is <c>qwen3:8b-64k</c>, and <c>mistral</c> <c>mistral:latest-64k</c>.</summary>
    public static string CopyName(string model, int window) =>
        $"{WithTag(model)}-{(window % 1024 == 0 ? $"{window / 1024}k" : window.ToString(CultureInfo.InvariantCulture))}";
}
