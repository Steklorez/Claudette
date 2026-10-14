using Claudette.App.Conversation;
using Claudette.App.Tests.Support;
using Claudette.Core.Installation;
using Claudette.Core.Processes;
using Claudette.Core.Protocol;
using Claudette.Core.Sessions;
using Claudette.MockApi;

namespace Claudette.App.Tests;

/// <summary>
/// A tab driving the real <c>claude</c> against the mock Messages API (DESIGN.md §18, "Images from Claude's tools"): an
/// image Claude reads shows under the Read's card, named after its file. No account and no tokens. Skipped when Claude
/// Code isn't installed.
/// </summary>
[Trait("Category", "RealCli")]
public sealed class ToolImageRealCliTests
{
    /// <summary>A 1×1 PNG.</summary>
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    [Fact]
    public async Task An_image_Claude_reads_shows_under_the_Reads_card()
    {
        var located = await new ClaudeLocator(new ProcessLauncher(), TimeProvider.System).LocateAsync(null, TestContext.Current.CancellationToken);
        RealCli.SkipUnlessInstalled(located.IsUsable);
        await using var api = await MockAnthropicApi.StartAsync();
        await using var h = new TabTestHarness();
        var config = Directory.CreateDirectory(Path.Combine(h.Root, "claude-config")).FullName;
        h.Services.UseSessionFactory(new MockedFactory(new ClaudeSessionFactory(located.Install!.Path, new ProcessLauncher(), TimeProvider.System), new Dictionary<string, string?>
        {
            ["ANTHROPIC_BASE_URL"] = api.BaseAddress.ToString().TrimEnd('/'),
            ["ANTHROPIC_API_KEY"] = "sk-ant-mock-000",
            ["ANTHROPIC_AUTH_TOKEN"] = null,
            ["CLAUDE_CODE_OAUTH_TOKEN"] = null,
            ["CLAUDE_CONFIG_DIR"] = config,
            ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1",
        }));
        var path = Path.Combine(h.WorkFolder, "pixel.png");
        await File.WriteAllBytesAsync(path, Png, TestContext.Current.CancellationToken);
        var tab = await h.OpenTabAsync();

        tab.ComposerText = $"READ_FILE {path}";
        await tab.SendCommand.ExecuteAsync(null);

        await Claudette.Tests.Waiting.UntilAsync(
            () => InlineDispatcher.Read(() => tab.Items.OfType<ToolUseItem>().Any(t => t is { Name: "Read", IsComplete: true })), "the Read's result", TimeSpan.FromSeconds(60));
        var card = InlineDispatcher.Read(() => tab.Items.OfType<ToolUseItem>().Single(t => t.Name == "Read"));
        Assert.False(card.IsError, card.ResultSummary);
        var image = Assert.Single(card.Images);
        Assert.Equal(("pixel.png", path), (image.Name, image.FilePath));
        // As Claude Code sent it on: it may re-encode an image, but it's still one the API takes.
        Assert.Equal(image.Image.MediaType, MessageImage.DetectMediaType(image.Image.Data));
    }

    /// <summary>Starts the real <c>claude</c> pointed at the mock, keeping the tab's own variables.</summary>
    private sealed class MockedFactory(IClaudeSessionFactory inner, IReadOnlyDictionary<string, string?> environment) : IClaudeSessionFactory
    {
        public Task<ClaudeSession> StartAsync(ClaudeLaunchOptions options, CancellationToken cancellationToken = default)
        {
            var merged = new Dictionary<string, string?>(options.EnvironmentOverrides);
            foreach (var (name, value) in environment)
            {
                merged[name] = value;
            }
            return inner.StartAsync(options with { EnvironmentOverrides = merged, Model = "claude-haiku-4-5" }, cancellationToken);
        }
    }
}
