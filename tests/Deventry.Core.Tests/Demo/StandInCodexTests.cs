using System.Text;
using System.Threading.Channels;
using Deventry.Core.Codex;
using Deventry.Core.Demo;
using Deventry.Core.Protocol;
using Deventry.Core.Sessions;
using Deventry.Core.Tests.Support;

namespace Deventry.Core.Tests.Demo;

/// <summary>
/// Deventry standing in for Codex in its demo (DESIGN.md §11, "Demo mode"; §18, "Codex, a second agent"), driven by a
/// real <see cref="CodexSession"/>: it says it's Codex, reads the demo tab's conversation back, and answers a message
/// with an example turn, running nothing.
/// </summary>
public sealed class StandInCodexTests : IDisposable
{
    private readonly TempFolder _root = new("deventry-stand-in-codex");

    public void Dispose() => _root.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>A <see cref="CodexSession"/>'s connection to a stand-in running in the test, through two channels.</summary>
    private sealed class InProcess : IClaudeTransport
    {
        private readonly Channel<string?> _toStandIn = Channel.CreateUnbounded<string?>();
        private readonly Channel<string> _fromStandIn = Channel.CreateUnbounded<string>();
        private readonly TaskCompletionSource<TransportExit> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public InProcess(string codexHome, string folder)
        {
            var standIn = new StandInCodex(new Reader(_toStandIn.Reader), new Writer(_fromStandIn.Writer), TimeProvider.System, codexHome, folder, TimeSpan.Zero);
            _ = Task.Run(async () =>
            {
                var code = await standIn.RunAsync(["app-server"]);
                _fromStandIn.Writer.TryComplete();
                _exit.TrySetResult(new TransportExit(code, ""));
            });
        }

        public ChannelReader<string> Output => _fromStandIn.Reader;

        public Task<TransportExit> Completion => _exit.Task;

        public ValueTask SendAsync(string line, CancellationToken cancellationToken = default) => _toStandIn.Writer.WriteAsync(line, cancellationToken);

        public void CloseInput() => _toStandIn.Writer.TryWrite(null);

        public void Terminate() => CloseInput();

        public ValueTask DisposeAsync()
        {
            CloseInput();
            return ValueTask.CompletedTask;
        }

        private sealed class Reader(ChannelReader<string?> lines) : TextReader
        {
            public override async Task<string?> ReadLineAsync() => await lines.ReadAsync();
        }

        private sealed class Writer(ChannelWriter<string> lines) : TextWriter
        {
            public override Encoding Encoding => Encoding.UTF8;

            public override Task WriteLineAsync(string? value)
            {
                lines.TryWrite(value ?? "");
                return Task.CompletedTask;
            }
        }
    }

    [Fact]
    public async Task It_says_its_version_and_that_its_signed_in()
    {
        var output = new StringWriter();
        var standIn = new StandInCodex(new StringReader(""), output, TimeProvider.System, _root.Path, _root.Path);

        Assert.Equal(0, await standIn.RunAsync(["--version"]));
        Assert.Equal(0, await standIn.RunAsync(["login", "status"]));

        Assert.Equal($"codex-cli {StandInCodex.Version}\nLogged in using ChatGPT\n", output.ToString().ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task The_demo_tab_resumes_reads_its_conversation_back_and_gets_an_example_turn()
    {
        var folder = _root.CreateFolder("tidepool");
        var codexHome = _root.Combine("codex-home");
        var tab = DemoCodex.Write(codexHome, folder, DateTimeOffset.Now);
        await using var session = new CodexSession(new InProcess(codexHome, folder), TimeProvider.System);
        var limits = Task.Run(() => session.ReadUntilAsync<RateLimitsReported>(), Token);

        await session.StartAsync(new CodexStartOptions { WorkingDirectory = folder, Resume = tab.SessionId }, Token);
        var turns = await session.ReadTurnsAsync(Token);

        Assert.Equal((DemoCodex.Thread, DemoCodex.Model), (session.SessionId, session.Model));
        Assert.Equal(DemoCodex.ModelName, Assert.Single(session.Initialization!.Models).DisplayName);
        Assert.Equal(2, turns.Count(t => t.Event is PromptReplayed));
        var calls = turns.Select(t => t.Event).OfType<AssistantMessageReceived>().SelectMany(a => a.Message.Content.OfType<ToolUseBlock>()).Select(u => u.Call.Kind).ToList();
        Assert.Equal([ToolKind.Read, ToolKind.Search, ToolKind.Write], calls);
        Assert.Equal(41.0, (await limits).Match.RateLimits["primary"]!["usedPercent"]!.GetValue<double>());

        await session.SendUserMessageAsync("Show me the demo", Token);
        var (done, _) = await session.ReadUntilAsync<TurnCompleted>();

        Assert.StartsWith("This is **Deventry's demo**, and I'm a stand-in for Codex", done.Result.Result, StringComparison.Ordinal);
        // Kept with the thread, as Codex keeps its turns.
        Assert.Equal(3, (await session.ReadTurnsAsync(Token)).Count(t => t.Event is PromptReplayed));
    }
}
