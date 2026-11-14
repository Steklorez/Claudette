namespace Deventry.App.ViewModels;

/// <summary>A timed message sent to the tab it opened (DESIGN.md §18, "Timed messages").</summary>
public sealed partial class TabViewModel
{
    /// <summary>What a timed message's card says over it: <i>Timed message: Morning triage</i>.</summary>
    public static string TimedMessageLabel(string name) => $"Timed message: {name}";

    /// <summary>
    /// Sends a timed message as typing it would: the library check, held and queued messages, mentions; but the composer,
    /// its draft and its quick suffixes stay as they are. Its card says it's a timed message. Returns why it wasn't sent,
    /// or null when it was, or waits for the folder to be trusted (<see cref="IsAskingTrust"/>).
    /// </summary>
    internal async Task<string?> SendTimedAsync(string text, string name)
    {
        var message = text.Trim();
        if (message.Length == 0)
        {
            return "It has no message.";
        }
        if (IsReadOnly)
        {
            return $"{DisplayName} can only be read here.";
        }
        if (Status == TabStatus.Error && !IsWaitingForSignIn)
        {
            return ErrorMessage is { Length: > 0 } error ? error : "Claude Code stopped with an error.";
        }
        if (await CheckLibraryBeforeSendingAsync())
        {
            return $"Another machine's turns just reached {DisplayName}.";
        }
        await SendTypedAsync(message, message, null, [], [], via: null, label: TimedMessageLabel(name));
        return null;
    }
}
