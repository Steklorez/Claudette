using Deventry.Core.Accounts;
using Deventry.Core.LocalModels;

namespace Deventry.Core.Tests.LocalModels;

/// <summary>Starting an account's server when it isn't running (DESIGN.md §18, "Ollama and llama.cpp accounts").</summary>
public class LocalServerLaunchTests
{
    private static readonly Dictionary<string, string> UserEnvironment = new() { ["PATH"] = "/usr/bin", ["LLAMA_API_KEY"] = "users-own", ["OLLAMA_MODELS"] = "/data/models" };

    // ---- Finding the program -------------------------------------------------------------------------------------------

    [Fact]
    public void The_program_the_account_names_is_used_only_if_its_there()
    {
        Assert.Equal("/opt/ollama", LocalServerLaunch.Find(LocalServers.Ollama, "/opt/ollama", "/usr/bin", p => p == "/opt/ollama"));
        Assert.Null(LocalServerLaunch.Find(LocalServers.Ollama, "/opt/gone", "/usr/bin", _ => false));
    }

    [Fact]
    public void Otherwise_its_found_on_the_path_before_where_its_installer_puts_it()
    {
        var name = OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server";
        var onPath = Path.Combine("tools", "llama", name);
        var known = LocalServerLaunch.KnownPaths(LocalServers.LlamaCpp)[0];

        Assert.Equal(onPath, LocalServerLaunch.Find(LocalServers.LlamaCpp, null, Path.Combine("tools", "llama"), p => p == onPath || p == known));
        Assert.Equal(known, LocalServerLaunch.Find(LocalServers.LlamaCpp, " ", "elsewhere", p => p == known));
        Assert.Null(LocalServerLaunch.Find(LocalServers.LlamaCpp, null, "elsewhere", _ => false));
    }

    [Fact]
    public void Ollamas_installer_puts_it_in_its_own_place_on_each_system()
    {
        var known = LocalServerLaunch.KnownPaths(LocalServers.Ollama);

        if (OperatingSystem.IsWindows())
        {
            Assert.EndsWith(Path.Combine("Programs", "Ollama", "ollama.exe"), known[0]);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.Contains("/Applications/Ollama.app/Contents/Resources/ollama", known);
        }
        else
        {
            Assert.Contains("/usr/local/bin/ollama", known);
        }
    }

    // ---- The command -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Ollama_serves_on_the_accounts_port_with_its_window_as_the_default()
    {
        var local = new LocalServerSettings { Address = "http://localhost:11500", Model = "qwen3:8b", Window = 65_536 };

        var spec = LocalServerLaunch.Command(LocalServers.Ollama, "/usr/local/bin/ollama", local, UserEnvironment);

        Assert.Equal("/usr/local/bin/ollama", spec.FileName);
        Assert.Equal(["serve"], spec.Arguments);
        Assert.Equal("127.0.0.1:11500", spec.Environment!["OLLAMA_HOST"]);
        Assert.Equal("65536", spec.Environment["OLLAMA_CONTEXT_LENGTH"]);
        // The user's own configuration passes through.
        Assert.Equal("/data/models", spec.Environment["OLLAMA_MODELS"]);
        Assert.True(spec.TrackProcessTree);
    }

    [Fact]
    public void Llama_server_loads_the_accounts_model_by_its_name_at_its_window_on_this_computer_alone()
    {
        var file = Path.Combine(Path.GetTempPath(), "qwen3-8b.gguf");
        var local = new LocalServerSettings { Address = "http://127.0.0.1:9931", Model = "qwen3-8b", Window = 40_960, ServerModel = file, HasKey = true };

        var spec = LocalServerLaunch.Command(LocalServers.LlamaCpp, "llama-server", local, UserEnvironment);

        Assert.Equal(["-m", file, "--alias", "qwen3-8b", "-c", "40960", "--host", "127.0.0.1", "--port", "9931"], spec.Arguments);
        // It listens on this computer alone, so it's started without a key, the user's own included.
        Assert.False(spec.Environment!.ContainsKey("LLAMA_API_KEY"));
    }

    [Fact]
    public void A_model_on_Hugging_Face_is_downloaded_by_llama_server()
    {
        var local = new LocalServerSettings { Address = "http://localhost:9931", Model = "qwen3-8b-q4_k_m", Window = 0, ServerModel = "unsloth/Qwen3-8B-GGUF:Q4_K_M" };

        var spec = LocalServerLaunch.Command(LocalServers.LlamaCpp, "llama-server", local, UserEnvironment);

        Assert.Equal(["-hf", "unsloth/Qwen3-8B-GGUF:Q4_K_M", "--alias", "qwen3-8b-q4_k_m", "--host", "127.0.0.1", "--port", "9931"], spec.Arguments);
    }

    [Fact]
    public void A_server_elsewhere_or_llama_server_without_a_model_isnt_started()
    {
        Assert.Throws<InvalidOperationException>(() => LocalServerLaunch.Command(LocalServers.Ollama, "ollama",
            new LocalServerSettings { Address = "http://gpu-box:11434", Model = "m" }, UserEnvironment));
        Assert.Throws<InvalidOperationException>(() => LocalServerLaunch.Command(LocalServers.LlamaCpp, "llama-server",
            new LocalServerSettings { Address = "http://localhost:9931", Model = "m" }, UserEnvironment));
    }

    [Theory]
    [InlineData("/models/Qwen3-8B-Q4_K_M.gguf", "Qwen3-8B-Q4_K_M")]
    [InlineData("C:\\models\\qwen3.gguf", "qwen3")]
    [InlineData("unsloth/Qwen3-8B-GGUF:Q4_K_M", "qwen3-8b-q4_k_m")]
    public void A_model_to_load_gives_the_account_a_name_for_it(string serverModel, string name) =>
        Assert.Equal(name, LocalServerLaunch.ModelName(serverModel));

    [Fact]
    public void Llama_server_loading_a_large_model_is_given_longer_to_answer() =>
        Assert.True(LocalServerLaunch.StartTimeout(LocalServers.LlamaCpp) > LocalServerLaunch.StartTimeout(LocalServers.Ollama));
}
