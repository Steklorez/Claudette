using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Claudette.App.Services.Automation;
using Microsoft.Extensions.Logging.Abstractions;

namespace Claudette.App.Tests;

/// <summary>
/// The demo's command channel (DESIGN.md §9, "Taking screenshots without the focus"): a named pipe (a Unix domain socket
/// on macOS and Linux) that takes a command per JSON line and answers each with one, a connection at a time.
/// </summary>
public class AutomationChannelTests
{
    // Short: on macOS a socket's path, the temporary folder's included, can't be much over 100 characters.
    private static string NewPipeName() => $"cl-{Guid.NewGuid().ToString("N")[..8]}";

    private static async Task<(StreamWriter Writer, StreamReader Reader, NamedPipeClientStream Client)> ConnectAsync(string name)
    {
        var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync((int)Waiting.Timeout.TotalMilliseconds, TestContext.Current.CancellationToken);
        var utf8 = new UTF8Encoding(false);
        return (new StreamWriter(client, utf8, leaveOpen: true) { AutoFlush = true }, new StreamReader(client, utf8, leaveOpen: true), client);
    }

    [Fact]
    public async Task Each_command_is_carried_out_and_answered_and_a_line_that_isnt_one_says_so()
    {
        var name = NewPipeName();
        var seen = new ConcurrentQueue<AutomationRequest>();
        using var channel = new AutomationChannel(name, request =>
        {
            seen.Enqueue(request);
            return Task.FromResult(request.Verb == "toggle" ? AutomationResponse.Done("On") : AutomationResponse.Failed("not that"));
        }, NullLogger.Instance);
        var (writer, reader, client) = await ConnectAsync(name);
        await using (client)
        {
            await writer.WriteLineAsync("""{"verb":"toggle","window":12,"name":"Show the process monitor","type":"Button","x":40.5,"y":7,"on":true}""".AsMemory(), TestContext.Current.CancellationToken);
            var toggled = JsonSerializer.Deserialize<AutomationResponse>((await reader.ReadLineAsync(TestContext.Current.CancellationToken))!, AutomationChannel.Json)!;
            await writer.WriteLineAsync("""{"verb":"click"}""".AsMemory(), TestContext.Current.CancellationToken);
            var refused = JsonSerializer.Deserialize<AutomationResponse>((await reader.ReadLineAsync(TestContext.Current.CancellationToken))!, AutomationChannel.Json)!;
            await writer.WriteLineAsync("not a command".AsMemory(), TestContext.Current.CancellationToken);
            var nonsense = JsonSerializer.Deserialize<AutomationResponse>((await reader.ReadLineAsync(TestContext.Current.CancellationToken))!, AutomationChannel.Json)!;

            Assert.Equal(new AutomationResponse(true, null, "On"), toggled);
            Assert.Equal(new AutomationResponse(false, "not that"), refused);
            Assert.False(nonsense.Ok);
            Assert.StartsWith("That isn't a command", nonsense.Error, StringComparison.Ordinal);
        }
        var first = seen.First();
        Assert.Equal(("toggle", 12L, "Show the process monitor", "Button", 40.5, 7.0, (bool?)true), (first.Verb, first.Window, first.Name, first.Type, first.X, first.Y, first.On));
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public async Task One_script_after_another_connects_and_a_command_that_fails_is_answered_with_why()
    {
        var name = NewPipeName();
        using var channel = new AutomationChannel(name, _ => throw new InvalidOperationException("the window is gone"), NullLogger.Instance);

        foreach (var attempt in Enumerable.Range(0, 2))
        {
            var (writer, reader, client) = await ConnectAsync(name);
            await using (client)
            {
                await writer.WriteLineAsync("""{"verb":"ping"}""".AsMemory(), TestContext.Current.CancellationToken);
                var answer = JsonSerializer.Deserialize<AutomationResponse>((await reader.ReadLineAsync(TestContext.Current.CancellationToken))!, AutomationChannel.Json)!;
                Assert.Equal(new AutomationResponse(false, "the window is gone"), answer);
            }
        }
    }

    [Fact]
    public async Task Once_disposed_it_stops_listening()
    {
        var channel = new AutomationChannel(NewPipeName(), _ => Task.FromResult(AutomationResponse.Done()), NullLogger.Instance);

        channel.Dispose();

        await channel.Listening.WaitAsync(Waiting.Timeout, TimeProvider.System, TestContext.Current.CancellationToken);
    }
}
