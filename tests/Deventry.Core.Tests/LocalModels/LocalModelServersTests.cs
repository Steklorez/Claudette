using System.Net;
using System.Text.Json.Nodes;
using Deventry.Core.Accounts;
using Deventry.Core.LocalModels;
using Deventry.Core.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.LocalModels;

/// <summary>Asking Ollama and llama-server what an account needs to know (DESIGN.md §18, "Ollama and llama.cpp accounts").</summary>
public class LocalModelServersTests
{
    private const string Ollama = "http://localhost:11434";
    private const string Llama = "http://localhost:9931";

    private static readonly FakeTimeProvider Time = new();

    private static string Body(HttpRequestMessage request) => request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

    // Answers as Ollama 0.40.18 did in the spike.
    private const string Tags = """
        {"models":[
          {"name":"qwen3:8b","model":"qwen3:8b","size":5225388164,"details":{"format":"gguf","family":"qwen3","context_length":40960},"capabilities":["completion","tools","thinking"]},
          {"name":"llava:7b","model":"llava:7b","size":4700000000,"details":{"context_length":32768},"capabilities":["completion","vision"]},
          {"name":"gpt-oss:120b-cloud","model":"gpt-oss:120b-cloud","remote_host":"https://ollama.com:443","details":{"context_length":131072},"capabilities":["completion","tools","thinking"]}
        ]}
        """;

    private const string Show = """
        {"capabilities":["completion","tools","thinking"],"model_info":{"general.architecture":"qwen3","qwen3.context_length":40960},
         "parameters":"num_ctx                        32768\nrepeat_penalty                 1\nstop                           \"<|im_end|>\"\ntemperature                    0.6"}
        """;

    // ---- Ollama ------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Ollama_lists_its_models_with_their_windows_and_what_they_can_do()
    {
        using var handler = new FakeHttpHandler().OnJson($"{Ollama}/api/version", """{"version":"0.40.18"}""").OnJson($"{Ollama}/api/tags", Tags);
        using var http = new HttpClient(handler);

        var report = await new OllamaServer(Ollama, http, Time).CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal("0.40.18", report.Version);
        Assert.Equal(["qwen3:8b", "llava:7b", "gpt-oss:120b-cloud"], report.Models.Select(m => m.Name));
        var qwen = report.Models[0];
        Assert.Equal(40_960, qwen.TrainedWindow);
        Assert.Equal(5_225_388_164, qwen.Size);
        Assert.True(qwen is { CapabilitiesKnown: true, CanUseTools: true, CanThink: true, CanSee: false, IsCloud: false });
        Assert.True(report.Models[1] is { CanUseTools: false, CanSee: true });
        Assert.True(report.Models[2].IsCloud);
    }

    [Fact]
    public async Task An_older_Ollama_without_capabilities_in_its_list_is_asked_for_each_model()
    {
        using var handler = new FakeHttpHandler()
            .OnJson($"{Ollama}/api/version", """{"version":"0.12.0"}""")
            .OnJson($"{Ollama}/api/tags", """{"models":[{"name":"qwen3:8b","size":1}]}""")
            .OnJson($"{Ollama}/api/show", Show);
        using var http = new HttpClient(handler);

        var model = Assert.Single((await new OllamaServer(Ollama, http, Time).CheckAsync(TestContext.Current.CancellationToken)).Models);

        Assert.True(model is { CapabilitiesKnown: true, CanUseTools: true, TrainedWindow: 40_960 });
    }

    [Fact]
    public async Task A_models_own_window_is_its_num_ctx()
    {
        using var handler = new FakeHttpHandler().On($"{Ollama}/api/show", request =>
        {
            Assert.Equal("qwen3-8b-32k", JsonNode.Parse(Body(request))!["model"]!.GetValue<string>());
            return Json(Show);
        });
        using var http = new HttpClient(handler);

        var model = await new OllamaServer(Ollama, http, Time).DescribeAsync("qwen3-8b-32k", TestContext.Current.CancellationToken);

        Assert.Equal(32_768, model!.Window);
        Assert.Equal(40_960, model.TrainedWindow);
        Assert.Null(OllamaServer.NumCtx("temperature 0.6"));
    }

    [Fact]
    public async Task A_model_the_server_hasnt_is_none()
    {
        using var handler = new FakeHttpHandler().On($"{Ollama}/api/show", _ => Json("""{"error":"model 'nope' not found"}""", HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);

        Assert.Null(await new OllamaServer(Ollama, http, Time).DescribeAsync("nope", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Loading_a_model_says_the_window_Ollama_loaded_it_with()
    {
        var loaded = new List<JsonNode>();
        using var handler = new FakeHttpHandler()
            .On($"{Ollama}/api/generate", request =>
            {
                loaded.Add(JsonNode.Parse(Body(request))!);
                return Json("""{"model":"qwen3:8b","response":"","done":true,"done_reason":"load"}""");
            })
            // A model without a tag is listed with :latest.
            .OnJson($"{Ollama}/api/ps", """{"models":[{"name":"mistral:latest","context_length":8192},{"name":"qwen3:8b","model":"qwen3:8b","context_length":4096}]}""");
        using var http = new HttpClient(handler);
        var server = new OllamaServer(Ollama, http, Time);

        Assert.Equal(4_096, await server.LoadAsync("qwen3:8b", TestContext.Current.CancellationToken));
        Assert.Equal(8_192, await server.LoadAsync("mistral", TestContext.Current.CancellationToken));
        // An empty prompt only loads it.
        Assert.Equal(["qwen3:8b", "mistral"], loaded.Select(l => l["model"]!.GetValue<string>()));
        Assert.All(loaded, l => Assert.Equal("", l["prompt"]!.GetValue<string>()));
    }

    [Fact]
    public async Task A_copy_with_a_window_is_made_from_the_model_and_named_after_it()
    {
        JsonNode? asked = null;
        using var handler = new FakeHttpHandler().On($"{Ollama}/api/create", request =>
        {
            asked = JsonNode.Parse(Body(request));
            return Json("""{"status":"success"}""");
        });
        using var http = new HttpClient(handler);

        var name = await new OllamaServer(Ollama, http, Time).CopyWithWindowAsync("qwen3:8b", 65_536, TestContext.Current.CancellationToken);

        Assert.Equal("qwen3:8b-64k", name);
        Assert.Equal("qwen3:8b", asked!["from"]!.GetValue<string>());
        Assert.Equal(65_536, asked["parameters"]!["num_ctx"]!.GetValue<int>());
        Assert.Equal("mistral:latest-40000", OllamaServer.CopyName("mistral", 40_000));
    }

    [Fact]
    public async Task Ollamas_cloud_is_asked_with_its_key_as_a_bearer_token()
    {
        using var handler = new FakeHttpHandler().OnJson("https://ollama.com/api/version", """{"version":"0.40.18"}""");
        using var http = new HttpClient(handler);

        Assert.True(await new OllamaServer("https://ollama.com", http, Time, "olk-123").IsAnsweringAsync(TestContext.Current.CancellationToken));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("olk-123", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task A_server_that_isnt_running_isnt_answering_and_says_so()
    {
        using var http = new HttpClient(new RefusingHandler());
        var server = new OllamaServer(Ollama, http, Time);

        Assert.False(await server.IsAnsweringAsync(TestContext.Current.CancellationToken));
        var failure = await Assert.ThrowsAsync<LocalServerException>(() => server.CheckAsync(TestContext.Current.CancellationToken));
        Assert.StartsWith("Ollama isn't answering at localhost:11434", failure.Message);
    }

    // ---- llama-server ----------------------------------------------------------------------------------------------------

    // Answers as llama-server 1.0.0 (b12723) did in the spike, started without --alias.
    private const string Props = """
        {"default_generation_settings":{"n_ctx":40960},"total_slots":4,"model_alias":"C:\\models\\qwen3-8b.gguf","build_info":"b12723-abca69200",
         "modalities":{"vision":false,"video":false,"audio":false},
         "chat_template_caps":{"supports_tools":true,"supports_tool_calls":true,"supports_system_role":true}}
        """;

    [Fact]
    public async Task Llama_server_says_its_model_its_window_and_what_it_can_do()
    {
        using var handler = new FakeHttpHandler()
            .OnJson($"{Llama}/props", Props)
            .OnJson($"{Llama}/v1/models", """{"object":"list","data":[{"id":"C:\\models\\qwen3-8b.gguf","object":"model"}]}""");
        using var http = new HttpClient(handler);

        var report = await new LlamaCppServer(Llama, http, Time).CheckAsync(TestContext.Current.CancellationToken);

        Assert.Equal("b12723-abca69200", report.Version);
        var model = Assert.Single(report.Models);
        // Named by its file, not its path, which would put spaces and slashes in Claude Code's arguments.
        Assert.Equal("qwen3-8b", model.Name);
        Assert.True(model is { Window: 40_960, CanUseTools: true, CanSee: false, CapabilitiesKnown: true });
    }

    [Fact]
    public async Task Llama_servers_window_is_the_one_it_was_started_with()
    {
        using var handler = new FakeHttpHandler().OnJson($"{Llama}/props", Props.Replace("40960", "8192", StringComparison.Ordinal));
        using var http = new HttpClient(handler);

        Assert.Equal(8_192, await new LlamaCppServer(Llama, http, Time).LoadAsync("qwen3", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_key_llama_server_wants_is_sent_and_one_it_turns_down_says_so()
    {
        using var handler = new FakeHttpHandler().On($"{Llama}/props", request => request.Headers.Authorization?.Parameter == "right"
            ? Json(Props)
            : Json("""{"error":{"message":"Invalid API Key","type":"authentication_error","code":401}}""", HttpStatusCode.Unauthorized));
        using var http = new HttpClient(handler);

        var withoutKey = await Assert.ThrowsAsync<LocalServerException>(() => new LlamaCppServer(Llama, http, Time).LoadAsync("m", TestContext.Current.CancellationToken));
        var wrongKey = await Assert.ThrowsAsync<LocalServerException>(() => new LlamaCppServer(Llama, http, Time, "wrong").LoadAsync("m", TestContext.Current.CancellationToken));

        Assert.Equal("llama-server at localhost:9931 wants an API key.", withoutKey.Message);
        Assert.Equal("llama-server at localhost:9931 turned the API key down: Invalid API Key", wrongKey.Message);
        Assert.Equal(40_960, await new LlamaCppServer(Llama, http, Time, "right").LoadAsync("m", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Llama_servers_health_answers_without_the_key()
    {
        using var handler = new FakeHttpHandler().OnJson($"{Llama}/health", """{"status":"ok"}""");
        using var http = new HttpClient(handler);

        Assert.True(await new LlamaCppServer(Llama, http, Time).IsAnsweringAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("qwen3:8b", "qwen3:8b")]
    [InlineData("unsloth/Qwen3-8B-GGUF:Q4_K_M", "unsloth/Qwen3-8B-GGUF:Q4_K_M")]
    [InlineData("/models/qwen3-8b.gguf", "qwen3-8b")]
    [InlineData("C:\\Users\\me\\models\\blobs\\sha256-a3de", "sha256-a3de")]
    public void A_model_named_by_its_path_goes_by_its_files_name(string id, string name) => Assert.Equal(name, LlamaCppServer.NameOf(id));

    [Fact]
    public void Each_kind_has_its_client()
    {
        using var http = new HttpClient(new RefusingHandler());

        Assert.IsType<OllamaServer>(LocalModelServers.For(LocalServers.Ollama, Ollama, http, Time));
        Assert.IsType<LlamaCppServer>(LocalModelServers.For(LocalServers.LlamaCpp, Llama, http, Time));
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json) };

    /// <summary>Nothing is listening: every request fails as a refused connection does.</summary>
    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
    }
}
