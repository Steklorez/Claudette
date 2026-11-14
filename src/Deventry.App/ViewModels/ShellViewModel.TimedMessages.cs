using Deventry.Core.Settings;
using Deventry.Core.TimedMessages;

namespace Deventry.App.ViewModels;

/// <summary>The tabs timed messages open (DESIGN.md §18, "Timed messages").</summary>
public sealed partial class ShellViewModel
{
    /// <summary>
    /// A new tab for <paramref name="message"/>, in its folder or a worktree named after it (in the folder when it isn't
    /// in a git repository), with its mode, model and effort, without taking the selection from the tab the user is in;
    /// started. Null when it couldn't be opened.
    /// </summary>
    internal async Task<TabViewModel?> OpenTimedMessageTabAsync(TimedMessage message)
    {
        if (!Directory.Exists(message.Folder))
        {
            return null;
        }
        var overrides = new TabOverrides { PermissionMode = message.PermissionMode, Model = message.Model, Effort = message.Effort };
        TabViewModel? opened = null;
        void Opened(TabViewModel tab) => opened = tab;
        if (message.InWorktree && await _services.Git.GetRepositoryRootAsync(FolderHistory.Normalize(message.Folder)) is not null)
        {
            await OpenWorktreeTabAsync(message.Folder, Opened, message.WorktreeName, overrides, select: false);
        }
        else
        {
            await OpenFolderAsync(message.Folder, Opened, overrides, select: false);
        }
        return opened;
    }
}
