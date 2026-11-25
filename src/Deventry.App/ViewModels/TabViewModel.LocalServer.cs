using System.Globalization;
using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.Core.Accounts;
using Deventry.Core.LocalModels;
using Deventry.Core.Protocol;

namespace Deventry.App.ViewModels;

/// <summary>
/// A tab on an Ollama or llama.cpp account (DESIGN.md §18, "Ollama and llama.cpp accounts"): its server is made to
/// answer before Claude Code starts, started if the account says to, with its model loaded and its window checked; the
/// tab starts on the account's model, without effort or a fallback model; and images go only to a model that can see.
/// </summary>
public sealed partial class TabViewModel
{
    /// <summary>The account's model, as its server described it as the tab started; null on any other account, or when it couldn't say.</summary>
    private LocalModel? _localModel;

    /// <summary>The last turn ended without reaching the server: the next message checks it first, and starts it if the account says to.</summary>
    private bool _localServerDown;

    /// <summary>The tab runs on a model the user's own Ollama or llama-server serves.</summary>
    public bool IsOnLocalModel => Account?.IsLocal == true;

    /// <summary>
    /// Before the tab's Claude Code starts: the server answers, or is started, or the start stops with why; the model is
    /// loaded, so the first reply doesn't wait for it, and the window it loaded with is checked against the account's.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server isn't answering, or hasn't the account's model.</exception>
    private async Task PrepareLocalServerAsync(CancellationToken cancellationToken)
    {
        if (Account is not { Server: { } kind, Local: { } local } account)
        {
            _localModel = null;
            return;
        }
        var readiness = await _services.LocalServers.EnsureRunningAsync(account, cancellationToken);
        if (!readiness.IsAnswering)
        {
            throw new InvalidOperationException(readiness.Problem ?? $"{kind.ServerName} isn't answering.");
        }
        _localServerDown = false;
        if (readiness.Started)
        {
            _conversation.AddNote($"Started {kind.ServerName} for {account.Name}. Deventry stops it when it closes.");
        }
        try
        {
            var server = await _services.LocalServers.ServerForAsync(account);
            var model = await server.DescribeAsync(local.Model, cancellationToken)
                ?? throw new InvalidOperationException(kind == LocalServers.Ollama
                    ? $"Ollama has no model called {local.Model}. Pull it with \"ollama pull {local.Model}\", or choose another in Settings → Agents → Claude Code → Accounts."
                    : $"llama-server doesn't serve {local.Model}. Choose its model in Settings → Agents → Claude Code → Accounts.");
            _localModel = model;
            // A cloud model runs at its whole window, on ollama.com.
            var runs = model.IsCloud ? null : model.Window ?? await server.LoadAsync(local.Model, cancellationToken);
            if (runs is { } window && local.Window > 0 && window < local.Window)
            {
                _conversation.AddNote(SmallWindowNote(account, kind, local, window), NoteKind.Warning);
            }
        }
        catch (LocalServerException ex)
        {
            // Claude Code can still try: the server answers, and says for itself what's wrong.
            _conversation.AddNote($"Deventry couldn't check {local.Model} on {kind.ServerName}: {ex.Message}", NoteKind.Warning);
        }
    }

    /// <summary>The server runs the model at a smaller window than Claude Code is told, so it would cut or refuse requests.</summary>
    internal static string SmallWindowNote(ClaudeAccount account, LocalServerKind kind, LocalServerSettings local, int runs) =>
        $"{kind.ServerName} runs {local.Model} with a {runs.ToString("N0", CultureInfo.CurrentCulture)}-token window here, but {account.Name} says {local.Window.ToString("N0", CultureInfo.CurrentCulture)}. " +
        (kind == LocalServers.Ollama
            ? "Ollama cuts the start off a request that doesn't fit without saying so, and Claude would forget what it was doing. Change Ollama's window, or make a copy of the model with a bigger one in Settings → Agents → Claude Code → Accounts."
            : "llama-server refuses a request that doesn't fit. Start it with -c and the account's window, or let Deventry start it.");

    /// <summary>
    /// The model a tab on a local account starts on: the one its Tab settings chose, if that's one of the server's rather
    /// than one of Claude's, else the account's.
    /// </summary>
    private string LocalModelToStart(string accountModel) =>
        State.Overrides.Model is { Length: > 0 } chosen && !chosen.StartsWith("claude", StringComparison.OrdinalIgnoreCase)
            && chosen is not ("default" or "opus" or "sonnet" or "haiku" or "fable" or "opusplan")
            ? chosen
            : accountModel;

    /// <summary>
    /// Why a message with images can't go: the account's model can't see them, and its server would refuse them (Ollama)
    /// or fail on them (llama-server, which Claude Code then retries for minutes). Null when they can go.
    /// </summary>
    internal string? ImagesRefusal(int images) =>
        images > 0 && Account is { Server: { } kind, Local: { } local } && _localModel is { CapabilitiesKnown: true, CanSee: false }
            ? $"{local.Model} can't see images, so {kind.ServerName} would turn this message down. Remove the {(images == 1 ? "image" : "images")}, or choose a model that can see in Settings → Agents → Claude Code → Accounts."
            : null;

    /// <summary>
    /// Before a message after a turn that couldn't reach the server: it answers again, or is started, or the message waits
    /// with why. True when it can't go.
    /// </summary>
    private async Task<bool> CheckLocalServerBeforeSendingAsync()
    {
        if (!_localServerDown || Account is not { Server: { } } account)
        {
            return false;
        }
        var readiness = await _services.LocalServers.EnsureRunningAsync(account, _closing.Token);
        if (!readiness.IsAnswering)
        {
            _conversation.AddNote(readiness.Problem ?? "The server isn't answering.", NoteKind.Error);
            return true;
        }
        _localServerDown = false;
        if (readiness.Started)
        {
            _conversation.AddNote($"Started {account.Server!.ServerName} for {account.Name}. Deventry stops it when it closes.");
        }
        return false;
    }

    /// <summary>A turn on a local account ended because the server stopped answering: the tab says so, and the next message checks it.</summary>
    private void OnTurnEndedForLocalServer(ResultMessage result)
    {
        if (!result.IsError || Account is not { Server: { } kind, Local: { } local } || result.Result is not { } said
            || !(said.Contains("ECONNREFUSED", StringComparison.Ordinal) || said.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }
        _localServerDown = true;
        _conversation.AddNote(local.StartServer && LocalServers.IsOnThisComputer(local.Address)
            ? $"{kind.ServerName} stopped answering at {LocalServersService.Display(local.Address)}. Deventry starts it again with your next message."
            : $"{kind.ServerName} stopped answering at {LocalServersService.Display(local.Address)}. Start it again, then send your message.", NoteKind.Error);
    }
}
