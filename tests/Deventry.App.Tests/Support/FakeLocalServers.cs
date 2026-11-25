using System.Net;
using System.Text.Json.Nodes;
using Deventry.Core.Tests.Support;

namespace Deventry.App.Tests.Support;

/// <summary>An Ollama and a llama-server for Ollama and llama.cpp accounts (DESIGN.md §18), answering as they did in the spike.</summary>
public static class FakeLocalServers
{
    public const string OllamaAddress = "http://localhost:11434";
    public const string LlamaAddress = "http://localhost:9931";

    /// <summary>An Ollama with a model that can use tools and one that can't, which runs models at 4,096 tokens, as on a 16 GB GPU.</summary>
    public static FakeHttpHandler Ollama(List<JsonNode>? created = null) => new FakeHttpHandler()
        .OnJson($"{OllamaAddress}/api/version", """{"version":"0.40.18"}""")
        .OnJson($"{OllamaAddress}/api/tags", """
            {"models":[
              {"name":"qwen3:8b","size":5225388164,"details":{"context_length":40960},"capabilities":["completion","tools","thinking"]},
              {"name":"llava:7b","size":4700000000,"details":{"context_length":32768},"capabilities":["completion","vision"]}
            ]}
            """)
        .On($"{OllamaAddress}/api/show", request =>
        {
            var model = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!["model"]!.GetValue<string>();
            return Json(model.EndsWith("-40k", StringComparison.Ordinal)
                ? """{"capabilities":["completion","tools","thinking"],"model_info":{"qwen3.context_length":40960},"parameters":"num_ctx 40960"}"""
                : """{"capabilities":["completion","tools","thinking"],"model_info":{"qwen3.context_length":40960},"parameters":"temperature 0.6"}""");
        })
        .OnJson($"{OllamaAddress}/api/generate", """{"done":true,"done_reason":"load"}""")
        .OnJson($"{OllamaAddress}/api/ps", """{"models":[{"name":"qwen3:8b","context_length":4096}]}""")
        .On($"{OllamaAddress}/api/create", request =>
        {
            created?.Add(JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!);
            return Json("""{"status":"success"}""");
        });

    /// <summary>A llama-server started with --alias and --api-key, which takes only <paramref name="key"/>.</summary>
    public static FakeHttpHandler Llama(string key) => new FakeHttpHandler()
        .OnJson($"{LlamaAddress}/health", """{"status":"ok"}""")
        .On($"{LlamaAddress}/props", r => Keyed(r, key, """{"default_generation_settings":{"n_ctx":65536},"build_info":"b12723","modalities":{"vision":false},"chat_template_caps":{"supports_tools":true}}"""))
        .On($"{LlamaAddress}/v1/models", r => Keyed(r, key, """{"data":[{"id":"qwen3-8b"}]}"""));

    private static HttpResponseMessage Keyed(HttpRequestMessage request, string key, string json) =>
        request.Headers.Authorization?.Parameter == key ? Json(json) : Json("""{"error":{"message":"Invalid API Key"}}""", HttpStatusCode.Unauthorized);

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json) };

}
