using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Deventry.Core.Protocol;

namespace Deventry.Core.Codex;

/// <summary>A request that Codex answered with an error.</summary>
public sealed class CodexRequestException(string method, int code, string error)
    : Exception($"Codex rejected '{method}': {error}")
{
    public string Method { get; } = method;

    /// <summary>The JSON-RPC error code: <c>-32601</c> for a method this Codex doesn't know, <c>-32600</c> for one it refused.</summary>
    public int Code { get; } = code;

    public string Error { get; } = error;

    /// <summary>The method isn't one this Codex knows, or needs the experimental API: an older or newer Codex.</summary>
    public bool IsUnknownMethod => Code == -32601 || Error.Contains("unknown method", StringComparison.OrdinalIgnoreCase)
        || Error.Contains("requires experimentalApi", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The JSON-RPC side of <c>codex app-server</c> (DESIGN.md §18, "Codex, a second agent"): sends requests and matches
/// Codex's responses to them by <c>id</c>, as <see cref="Protocol.ControlChannel"/> does for Claude Code. Ids are 1, 2,
/// … in send order, which keeps recorded protocol fixtures replayable. Codex leaves <c>"jsonrpc":"2.0"</c> off the
/// wire, and so does this.
/// </summary>
internal sealed class CodexRpc(Func<string, CancellationToken, ValueTask> sendLine, TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<long, Pending> _pending = new();
    private long _nextId;
    private Exception? _failure;

    public async Task<JsonObject> RequestAsync(string method, JsonObject? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var pending = new Pending(method);
        _pending[id] = pending;
        // Started before the request goes out, so the deadline exists by the time anything can see the request.
        using var timeoutSource = new CancellationTokenSource(timeout, timeProvider);
        try
        {
            IOException? unsent = null;
            if (Volatile.Read(ref _failure) is { } failure)
            {
                pending.Completion.TrySetException(failure);
            }
            else
            {
                var request = new JsonObject { ["method"] = method, ["id"] = id };
                if (parameters is not null)
                {
                    request["params"] = parameters;
                }
                try
                {
                    await sendLine(request.ToJsonString(), cancellationToken).ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    // Its input closed because it's exiting: its exit fails the request with why, which a broken pipe doesn't say.
                    unsent = ex;
                }
            }
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            try
            {
                return await pending.Completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                if (unsent is not null)
                {
                    throw new IOException($"Couldn't send '{method}' to Codex: {unsent.Message}", unsent);
                }
                throw new TimeoutException($"Codex didn't answer '{method}' within {timeout}.");
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Sends a notification, which has no answer.</summary>
    public ValueTask NotifyAsync(string method, JsonObject? parameters, CancellationToken cancellationToken)
    {
        var notification = new JsonObject { ["method"] = method };
        if (parameters is not null)
        {
            notification["params"] = parameters;
        }
        return sendLine(notification.ToJsonString(), cancellationToken);
    }

    /// <summary>Completes the request a response is for. False when nothing was waiting for it.</summary>
    public bool TryComplete(JsonObject response)
    {
        if (response["id"] is not JsonValue idValue || !TryId(idValue, out var id) || !_pending.TryRemove(id, out var pending))
        {
            return false;
        }
        if (response["error"] is JsonObject error)
        {
            var code = error["code"] is JsonValue c && c.TryGetValue<int>(out var value) ? value : 0;
            pending.Completion.TrySetException(new CodexRequestException(pending.Method, code, error.GetString("message") ?? "unknown error"));
        }
        else
        {
            pending.Completion.TrySetResult(response["result"] as JsonObject ?? []);
        }
        return true;
    }

    /// <summary>Fails every outstanding request, and every later one, because the process exited.</summary>
    public void FailAll(Exception exception)
    {
        Volatile.Write(ref _failure, exception);
        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var pending))
            {
                pending.Completion.TrySetException(exception);
            }
        }
    }

    /// <summary>Answers one of Codex's own requests (an approval, a question, a tool call), by its id as Codex sent it.</summary>
    public ValueTask RespondAsync(JsonNode id, JsonObject result, CancellationToken cancellationToken) =>
        sendLine(new JsonObject { ["id"] = id.DeepClone(), ["result"] = result }.ToJsonString(), cancellationToken);

    /// <summary>Refuses one of Codex's requests.</summary>
    public ValueTask RespondErrorAsync(JsonNode id, int code, string message, CancellationToken cancellationToken) =>
        sendLine(new JsonObject { ["id"] = id.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString(), cancellationToken);

    private static bool TryId(JsonValue value, out long id)
    {
        if (value.TryGetValue(out id))
        {
            return true;
        }
        return value.TryGetValue<string>(out var text) && long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out id);
    }

    private sealed class Pending(string method)
    {
        public string Method { get; } = method;

        public TaskCompletionSource<JsonObject> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
