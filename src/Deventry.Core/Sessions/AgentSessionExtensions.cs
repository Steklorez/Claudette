namespace Deventry.Core.Sessions;

/// <summary>The shorter ways to send a message, on any agent's session.</summary>
public static class AgentSessionExtensions
{
    /// <summary>Sends a message with no images, suffix or stamp: one of Deventry's own, such as a check-in.</summary>
    public static ValueTask SendUserMessageAsync(this IAgentSession session, string text, CancellationToken cancellationToken = default) =>
        session.SendUserMessageAsync(text, [], null, null, cancellationToken);

    /// <summary>Sends a message with images and no suffix or stamp.</summary>
    public static ValueTask SendUserMessageAsync(this IAgentSession session, string text, IReadOnlyList<Protocol.MessageImage> images, CancellationToken cancellationToken = default) =>
        session.SendUserMessageAsync(text, images, null, null, cancellationToken);
}
