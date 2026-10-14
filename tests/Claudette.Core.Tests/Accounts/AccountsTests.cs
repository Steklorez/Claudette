using System.Text.Json.Nodes;
using Claudette.Core.Accounts;
using Claudette.Core.Sessions;
using Claudette.Core.Settings;

namespace Claudette.Core.Tests.Accounts;

/// <summary>Accounts besides the Default one, and how an API key reaches Claude Code (DESIGN.md §4, "Accounts").</summary>
public class AccountsTests
{
    private const string Id = "0a1b2c3d4e5f";

    // ---- The key helper -----------------------------------------------------------------------------------------------

    [Fact]
    public void The_key_helper_is_Claudette_itself_named_in_the_sessions_own_settings()
    {
        var settings = ApiKeyHelper.Settings(@"C:\Program Files\Claudette\Claudette.exe", Id);

        Assert.Equal($"\"C:\\Program Files\\Claudette\\Claudette.exe\" --api-key-helper {Id}", JsonNode.Parse(settings)!["apiKeyHelper"]!.GetValue<string>());
        Assert.Equal(["apiKeyHelper"], JsonNode.Parse(settings)!.AsObject().Select(p => p.Key));
        Assert.True(ApiKeyHelper.TryGetAccountId(["--api-key-helper", Id], out var id));
        Assert.Equal(Id, id);
    }

    [Theory]
    [InlineData("/Applications/Claudette.app/Contents/MacOS/Claudette", true)]
    [InlineData(@"C:\Users\me\AppData\Local\Claudette\Claudette.exe", true)]
    [InlineData("/home/me/bin/clau\"dette", false)]
    [InlineData("/home/$USER/Claudette", false)]
    [InlineData("/home/me/`id`/Claudette", false)]
    [InlineData(@"C:\%TEMP%\Claudette.exe", false)]
    [InlineData("", false)]
    public void Only_a_path_a_shell_reads_as_it_is_goes_in_the_command(string path, bool safe)
    {
        Assert.Equal(safe, ApiKeyHelper.IsSafePath(path));
        if (!safe)
        {
            Assert.Throws<ArgumentException>(() => ApiKeyHelper.Command(path, Id));
        }
    }

    [Theory]
    [InlineData("abc123", true)]
    [InlineData("a-b-c", true)]
    [InlineData("abc; rm -rf ~", false)]
    [InlineData("abc def", false)]
    [InlineData("", false)]
    public void Only_a_plain_id_goes_in_the_command_or_starts_the_helper(string id, bool safe)
    {
        Assert.Equal(safe, ApiKeyHelper.IsSafeId(id));
        Assert.Equal(safe, ApiKeyHelper.TryGetAccountId(["--api-key-helper", id], out _));
    }

    [Fact]
    public void The_helper_starts_only_on_exactly_its_option_and_an_id()
    {
        Assert.False(ApiKeyHelper.TryGetAccountId([], out _));
        Assert.False(ApiKeyHelper.TryGetAccountId(["--api-key-helper"], out _));
        Assert.False(ApiKeyHelper.TryGetAccountId(["--folder", "x", "--api-key-helper", Id], out _));
        Assert.False(ApiKeyHelper.TryGetAccountId(["--api-key-helper", Id, "--folder"], out _));
    }

    [Fact]
    public void An_API_key_tab_goes_without_the_variables_that_outrank_its_helper() =>
        Assert.Equal(["ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN"], ApiKeyHelper.OutrankingVariables);

    [Fact]
    public void The_sessions_settings_go_on_the_command_line()
    {
        var args = ClaudeArguments.ForStreamingSession(new ClaudeLaunchOptions { WorkingDirectory = ".", Settings = """{"apiKeyHelper":"x"}""" });
        var plain = ClaudeArguments.ForStreamingSession(new ClaudeLaunchOptions { WorkingDirectory = "." });

        Assert.Equal(["--settings", """{"apiKeyHelper":"x"}"""], args.SkipWhile(a => a != "--settings").Take(2));
        Assert.DoesNotContain("--settings", plain);
    }

    // ---- Which account a folder uses ------------------------------------------------------------------------------------

    [Fact]
    public void A_folder_uses_its_groups_account_else_the_one_new_folders_use_else_Default()
    {
        var work = new ClaudeAccount { Id = "work", Name = "Work" };
        var side = new ClaudeAccount { Id = "side", Name = "Side" };
        var state = new AppState { Accounts = [work, side] };
        var api = Path.Combine(Path.GetTempPath(), "api");
        var web = Path.Combine(Path.GetTempPath(), "web");

        Assert.Null(AccountState.ForFolder(state, api));

        state.NewFolderAccount = "side";
        Assert.Equal("side", AccountState.ForFolder(state, api));

        // Set for the folder, however its path was written; Default too, which the one new folders use doesn't override.
        AccountState.RememberForFolder(state, api + Path.DirectorySeparatorChar, "work");
        AccountState.RememberForFolder(state, web, null);
        Assert.Equal("work", AccountState.ForFolder(state, api));
        Assert.Null(AccountState.ForFolder(state, web));

        // Remembered once, under the spelling given last.
        AccountState.RememberForFolder(state, api, "side");
        Assert.Equal([(api, "side"), (web, AccountState.DefaultId)], state.FolderAccounts.Select(p => (p.Key, p.Value)).Order());
    }

    [Fact]
    public void A_removed_account_leaves_its_folders_on_Default()
    {
        var state = new AppState { Accounts = [new ClaudeAccount { Id = "work", Name = "Work" }], NewFolderAccount = "work" };
        var api = Path.Combine(Path.GetTempPath(), "api");
        AccountState.RememberForFolder(state, api, "work");

        AccountState.Forget(state, "work");

        Assert.Empty(state.Accounts);
        Assert.Null(state.NewFolderAccount);
        Assert.Null(AccountState.ForFolder(state, api));
        Assert.Null(AccountState.Find(state, "work"));
    }

    [Fact]
    public void An_account_a_later_Claudette_added_or_one_with_an_unsafe_id_is_left_alone()
    {
        var state = new AppState
        {
            Accounts = [new ClaudeAccount { Id = "later", Kind = "somethingNew" }, new ClaudeAccount { Id = "bad id; rm", Name = "Bad" }],
            FolderAccounts = { [Path.GetTempPath()] = "later" },
        };

        Assert.Null(AccountState.Find(state, "later"));
        Assert.Null(AccountState.Find(state, "bad id; rm"));
        Assert.Null(AccountState.ForFolder(state, Path.GetTempPath()));
        // Kept as written, for the Claudette that knows it.
        Assert.Equal(2, state.Accounts.Count);
    }

    // ---- What a switch costs ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("claude-opus-5-5", 150_000, 0.75)]
    [InlineData("claude-opus-5-5[1m]", 150_000, 0.75)]
    [InlineData("claude-opus-5", 100_000, 0.625)]
    [InlineData("claude-sonnet-5-5", 150_000, 0.375)]
    [InlineData("claude-fable-5-1", 150_000, 1.875)]
    [InlineData("claude-haiku-4-5", 1_000_000, 1.25)]
    public void A_switch_re_reads_the_conversation_once_at_the_cache_write_rate(string model, long tokens, double dollars) =>
        Assert.Equal(dollars, SwitchCost.Estimate(model, tokens)!.Value, precision: 6);

    [Fact]
    public void A_model_without_a_known_price_gets_no_estimate()
    {
        Assert.Null(SwitchCost.Estimate("some-other-model", 1000));
        Assert.Null(SwitchCost.Estimate(null, 1000));
        Assert.Null(SwitchCost.Estimate("claude-opus-5-5", 0));
    }

    [Fact]
    public void The_confirmation_says_what_the_switch_costs()
    {
        var toKey = SwitchCost.Describe("Work", 150_000, "claude-opus-5-5", billedByToken: true);
        var toPlan = SwitchCost.Describe("Default", 150_000, "claude-opus-5-5", billedByToken: false);
        var unknown = SwitchCost.Describe("Work", 0, null, billedByToken: true);

        Assert.Contains("again under Work, without the prompt cache, once: about $0.75 at list price.", toKey, StringComparison.Ordinal);
        Assert.EndsWith(", which counts toward its plan's limits.", toPlan, StringComparison.Ordinal);
        Assert.Equal("Claude reads the conversation again under Work, without the prompt cache, once, billed to its API key.", unknown);
    }
}
