using Deventry.App.Services;
using Deventry.Core.Settings;
using Deventry.Core.TimedMessages;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels;

/// <summary>The tabs timed messages open, and making and clearing them from outside Settings (DESIGN.md §18, "Timed messages").</summary>
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

    /// <summary>Settings → Timed messages, on an editor for <paramref name="message"/>.</summary>
    private Task OpenTimedMessageEditorAsync(TimedMessage message)
    {
        _services.TimedMessages.RequestEditor(message);
        return OpenSettingsAtAsync(SettingsCategory.TimedMessages);
    }

    /// <summary><b>Add a timed message…</b>: the cup's menu and the command palette.</summary>
    [RelayCommand]
    private Task NewTimedMessageAsync() => OpenTimedMessageEditorAsync(TimedMessagesService.NewMessage(null));

    /// <summary><b>Add a timed message…</b> in a group's menu, with its folder filled in.</summary>
    [RelayCommand]
    private Task NewTimedMessageInGroupAsync(TabGroupViewModel? group) =>
        group is null ? Task.CompletedTask : OpenTimedMessageEditorAsync(TimedMessagesService.NewMessage(group.Folder));

    /// <summary><b>Edit…</b> under a message in the cup's menu.</summary>
    [RelayCommand]
    private Task EditTimedMessageAsync(TimedMessageRow? row) =>
        row is null ? Task.CompletedTask : OpenTimedMessageEditorAsync(row.Message.Clone());

    /// <summary><b>Clear…</b> under a message in the cup's menu: asks, then deletes it.</summary>
    [RelayCommand]
    private void ClearTimedMessage(TimedMessageRow? row)
    {
        if (row is null)
        {
            return;
        }
        Confirm($"Clear {row.Name}?", "It won't be sent again.", "Clear", () =>
        {
            _services.TimedMessages.Delete(row.Message.Id);
            return Task.CompletedTask;
        });
    }

    /// <summary><b>Clear all…</b> in the cup's menu.</summary>
    [RelayCommand]
    private void ClearAllTimedMessages() =>
        Confirm("Clear every timed message?", "None of them will be sent again.", "Clear all", () =>
        {
            _services.TimedMessages.DeleteAll();
            return Task.CompletedTask;
        });
}
