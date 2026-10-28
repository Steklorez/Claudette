using System.Collections.ObjectModel;
using Claudette.Core.Composer;
using Claudette.Core.Development;
using Claudette.Core.Protocol;
using Claudette.Core.Sessions;
using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>An image attached in the composer, shown as a removable thumbnail (DESIGN.md §5, "Attachments").</summary>
public sealed class ComposerAttachment(MessageImage image, string name)
{
    public MessageImage Image { get; } = image;

    public string Name { get; } = name;

    public byte[] Data => Image.Data;
}

/// <summary>A large paste kept as an attachment rather than in the box (DESIGN.md §5, "Attachments").</summary>
public sealed class PastedTextAttachment(string text)
{
    public string Text { get; } = text;

    /// <summary><c>Pasted text · 1,204 lines · 48 KB</c>.</summary>
    public string Label { get; } = PastedText.Describe(text);

    /// <summary>Its first lines, for the tooltip.</summary>
    public string Preview { get; } = PastedText.Preview(text);
}

/// <summary>The composer's autocomplete and attachments (DESIGN.md §5, "Composer").</summary>
public sealed partial class TabViewModel
{
    private SlashCommandCatalog? _slashCommands;
    private InitializeResult? _slashCommandsFrom;
    private ProjectFileIndex? _fileIndex;
    private ComposerCompletions? _completions;

    /// <summary>The tab's earlier prompts, for Up and Down in the composer (DESIGN.md §5, "Composer").</summary>
    private readonly PromptRecall _recall = new();

    /// <summary>The composer's text is being set to a recalled prompt, which isn't the user typing.</summary>
    private bool _recalling;

    /// <summary>Up on the composer's first line: the prompt before, keeping what was typed. False when there's none.</summary>
    internal bool RecallOlderPrompt() => _recall.Older(ComposerText) is { } prompt && ShowRecalled(prompt);

    /// <summary>Down on its last line, while going back through them: the prompt after, then what was typed.</summary>
    internal bool RecallNewerPrompt() => _recall.Newer() is { } prompt && ShowRecalled(prompt);

    /// <summary>Going back through earlier prompts: Down moves through them rather than the text.</summary>
    internal bool IsRecallingPrompt => _recall.IsRecalling;

    private bool ShowRecalled(string prompt)
    {
        _recalling = true;
        try
        {
            ComposerText = prompt;
        }
        finally
        {
            _recalling = false;
        }
        return true;
    }

    partial void OnComposerTextChanged(string value)
    {
        if (!_recalling)
        {
            // Typing, or sending: the next Up starts from the newest again.
            _recall.Reset();
        }
        if (value.Length > 0)
        {
            // Typing dismisses the suggestion, as in the terminal (DESIGN.md §5, "Prompt suggestions").
            PromptSuggestion = null;
        }
        DraftChanged();
        // Typing wakes Claudette on the composer from a nap, and her name has her wave back (DESIGN.md §5).
        TellMascot(mascot => mascot.Nudge());
        GreetMascot(value);
    }

    /// <summary>The <c>/</c> and <c>@</c> popup.</summary>
    public ComposerCompletions Completions => _completions ??= new ComposerCompletions(() => SlashCommands, () => MentionFiles, _services.Dispatcher,
        MentionTargets, _services.LiveSessions.Refresh);

    /// <summary>Where the <c>@</c> list finds files: the working folder, the extra folders, and folders typed after it.</summary>
    internal MentionFiles MentionFiles => new(FileIndex, ExtraFileIndexes, _services.MentionFolders, _services.HomeFolder);

    private IReadOnlyList<ProjectFileIndex> _extraFileIndexes = [];

    /// <summary>The extra folders' listings for <c>@</c>, shared as the working folder's is, and found again when they change.</summary>
    private IReadOnlyList<ProjectFileIndex> ExtraFileIndexes
    {
        get
        {
            var folders = ExtraFolders;
            if (_extraFileIndexes.Count != folders.Count || _extraFileIndexes.Where((index, i) => index.Folder != folders[i]).Any())
            {
                _extraFileIndexes = [.. folders.Select(_services.FileIndexFor)];
            }
            return _extraFileIndexes;
        }
    }

    /// <summary>The session's slash commands: from <c>initialize</c>, then each turn's <c>system/init</c>.</summary>
    internal SlashCommandCatalog SlashCommands
    {
        get
        {
            _slashCommands ??= new SlashCommandCatalog();
            if (_session?.Initialization is { } initialization && !ReferenceEquals(initialization, _slashCommandsFrom))
            {
                _slashCommandsFrom = initialization;
                _slashCommands.SetDescribed(initialization.Commands);
            }
            return _slashCommands;
        }
    }

    /// <summary>
    /// The working folder's files, for <c>@</c>: shared with the other tabs in the folder, so it's listed once. Found
    /// again if the tab moves to another folder.
    /// </summary>
    internal ProjectFileIndex FileIndex =>
        _fileIndex is { } index && index.Folder == Folder ? index : _fileIndex = _services.FileIndexFor(Folder);

    /// <summary>Keeps the command list and file index current from the session's events.</summary>
    private void ObserveForComposer(SessionEvent sessionEvent)
    {
        switch (sessionEvent)
        {
            case TurnStarted started:
                SlashCommands.SetAccepted(started.Init);
                break;
            case SystemNotice { Message.Subtype: "commands_changed" } changed:
                // The full new list. Reading SlashCommands first takes initialize's list, which this then replaces.
                SlashCommands.SetDescribed(SlashCommandInfo.ParseList(changed.Message.Raw.GetArray("commands")));
                break;
            case TurnCompleted:
                // The turn may have added or removed files.
                _fileIndex?.Invalidate();
                foreach (var index in _extraFileIndexes)
                {
                    index.Invalidate();
                }
                break;
            case PromptSuggested suggested:
                // Only for an empty composer, with nothing sent since the turn ended.
                PromptSuggestion = _services.Settings.ClaudeCode.PromptSuggestions && ComposerText.Length == 0 && !IsReadOnly
                    && _session?.State == SessionState.Idle
                    ? suggested.Suggestion
                    : null;
                break;
            case StateChanged { State: SessionState.Working or SessionState.Exited } or TurnStarted or ConversationReset or SessionExited:
                // Whatever goes next, it's not the turn it was suggested after.
                PromptSuggestion = null;
                break;
        }
    }

    // ---- Prompt suggestions (DESIGN.md §5, "Prompt suggestions") ------------------------------------------------------

    /// <summary>
    /// The next prompt Claude Code predicts after a turn, which the empty composer shows until something is typed or
    /// sent. Null when there's none.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ComposerPlaceholder))]
    public partial string? PromptSuggestion { get; private set; }

    /// <summary>The composer's placeholder: the suggestion, with the key that takes it, or which keys send.</summary>
    public string ComposerPlaceholder => PromptSuggestion is { } suggestion ? $"{suggestion}  (Tab to use)" : Tips.ComposerPlaceholder;

    /// <summary>
    /// The send key chip in the composer's bar (DESIGN.md §5, "Composer"): Enter sends, or Ctrl/Cmd+Enter does. It's
    /// Settings → Keyboard → Send with (DESIGN.md §14), saved as that page saves it, so every tab's chip and placeholder,
    /// an open Settings window and settings sync follow.
    /// </summary>
    [RelayCommand]
    private void SwitchSendKey()
    {
        var keyboard = _services.Settings.Keyboard;
        keyboard.SendKey = keyboard.SendKey == SendKey.PrimaryEnter ? SendKey.Enter : SendKey.PrimaryEnter;
        _services.SaveSettings();
    }

    /// <summary>
    /// Tab or Right in the empty composer: the suggestion goes in the box, to send as it is or change first. False when
    /// there's none.
    /// </summary>
    internal bool AcceptPromptSuggestion()
    {
        if (PromptSuggestion is not { } suggestion || ComposerText.Length > 0)
        {
            return false;
        }
        // Which dismisses it.
        ComposerText = suggestion;
        return true;
    }

    // ---- Attachments (DESIGN.md §5, "Attachments") ---------------------------------------------------------

    public ObservableCollection<ComposerAttachment> Attachments { get; } = [];

    /// <summary>Why the last dropped or pasted file wasn't attached.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAttachmentError))]
    public partial string? AttachmentError { get; set; }

    public bool HasAttachmentError => !string.IsNullOrEmpty(AttachmentError);

    [RelayCommand]
    private void RemoveAttachment(ComposerAttachment? attachment)
    {
        if (attachment is not null && Attachments.Remove(attachment))
        {
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void DismissAttachmentError() => AttachmentError = null;

    /// <summary>
    /// Attaches dropped or pasted files: images as thumbnails, other files and folders as <c>@path</c> mentions.
    /// Returns the mentions for the view to insert at the caret, or null when there are none.
    /// </summary>
    public async Task<string?> AddFilesAsync(IReadOnlyList<string> paths)
    {
        var mentions = new List<string>();
        var errors = new List<string>();
        foreach (var path in paths)
        {
            var attachment = await Core.Composer.Attachments.FromPathAsync(path, Folder);
            if (attachment.Image is { } image)
            {
                if (TryAttach(image, attachment.Name) is { } error)
                {
                    errors.Add(error);
                }
            }
            else if (attachment.Mention is { } mention)
            {
                mentions.Add(mention);
            }
            else if (attachment.Error is { } error)
            {
                errors.Add(error);
            }
        }
        AttachmentError = errors.Count > 0 ? string.Join(" ", errors.Distinct(StringComparer.Ordinal)) : null;
        if (errors.Count < paths.Count)
        {
            // Claudette on the composer catches it (DESIGN.md §5).
            TellMascot(mascot => mascot.Caught());
        }
        return mentions.Count > 0 ? string.Join(" ", mentions) + " " : null;
    }

    /// <summary>Attaches an image from the clipboard or a drag, given as bytes. False when it can't be.</summary>
    public bool AddImage(byte[] data, string name)
    {
        var attachment = Core.Composer.Attachments.FromImageBytes(data, name);
        var error = attachment.Image is { } image ? TryAttach(image, name) : attachment.Error;
        AttachmentError = error;
        return error is null;
    }

    /// <summary>
    /// A paste into the composer: copied files are attached, then text is left to paste as usual (or kept as an
    /// attachment when it's large), then an image (such as a screenshot) is attached. Text wins over an image because
    /// apps often put both, as an image of the same text; text that's only white space doesn't, since it says nothing
    /// the image doesn't. Returns what to insert (mentions, or nothing), or null to let the text box paste the text itself.
    /// </summary>
    public async Task<string?> PasteAttachmentsAsync()
    {
        var platform = _services.Platform;
        if (await platform.GetClipboardFilesAsync() is { Count: > 0 } files)
        {
            return await AddFilesAsync(files) ?? "";
        }
        if (await platform.GetClipboardTextAsync() is { } text && !string.IsNullOrWhiteSpace(text))
        {
            if (PastedText.IsLarge(text))
            {
                // Too much for the box to stay quick and easy to edit around: an attachment (DESIGN.md §5, "Attachments").
                AddPastedText(text);
                return "";
            }
            return null;
        }
        if (await platform.GetClipboardImageAsync() is { } image)
        {
            if (AddImage(image, "Pasted image"))
            {
                TellMascot(mascot => mascot.Caught());
            }
            return "";
        }
        return null;
    }

    /// <summary>The composer's attach button: a file picker. Returns a mention to insert, as <see cref="AddFilesAsync"/>.</summary>
    public async Task<string?> PickAttachmentAsync() =>
        await _services.Platform.PickFileAsync("Attach a file") is { } path ? await AddFilesAsync([path]) : null;

    /// <returns>Why it wasn't attached, or null.</returns>
    private string? TryAttach(MessageImage image, string name)
    {
        if (Attachments.Count >= Core.Composer.Attachments.MaxImagesPerMessage)
        {
            return $"A message can have up to {Core.Composer.Attachments.MaxImagesPerMessage} images.";
        }
        Attachments.Add(new ComposerAttachment(image, name));
        SendCommand.NotifyCanExecuteChanged();
        return null;
    }

    /// <summary>Takes the attached images for a message being sent, and clears them from the composer.</summary>
    private IReadOnlyList<MessageImage> TakeAttachments()
    {
        var images = Attachments.Select(a => a.Image).ToArray();
        Attachments.Clear();
        AttachmentError = null;
        SendCommand.NotifyCanExecuteChanged();
        return images;
    }

    // ---- Large pastes (DESIGN.md §5, "Attachments") ---------------------------------------------------------------

    /// <summary>Pastes too large for the box, sent after what's typed.</summary>
    public ObservableCollection<PastedTextAttachment> PastedTexts { get; } = [];

    /// <summary>Keeps <paramref name="text"/> as an attachment rather than in the box.</summary>
    public void AddPastedText(string text)
    {
        PastedTexts.Add(new PastedTextAttachment(text));
        SendCommand.NotifyCanExecuteChanged();
        // Claudette on the composer staggers under it (DESIGN.md §5).
        TellMascot(mascot => mascot.HeavyPaste());
    }

    [RelayCommand]
    private void RemovePastedText(PastedTextAttachment? paste)
    {
        if (paste is not null && PastedTexts.Remove(paste))
        {
            SendCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary><c>Ctrl/Cmd+Shift+V</c>: the clipboard's text, however long, to paste into the box as it is. Null when there's none.</summary>
    public Task<string?> ClipboardTextAsync() => _services.Platform.GetClipboardTextAsync();

    /// <summary>Takes the pastes for a message being sent, and clears them from the composer.</summary>
    private IReadOnlyList<string> TakePastedTexts()
    {
        var pastes = PastedTexts.Select(p => p.Text).ToArray();
        PastedTexts.Clear();
        return pastes;
    }

    /// <summary>The attached images, for the draft a restart into a new build keeps (DESIGN.md §9, "Working on Claudette").</summary>
    private List<DraftImage> DraftImages() => [.. Attachments.Select(a => new DraftImage(a.Name, a.Data))];

    /// <summary>Puts back a draft's images, checked again as when they were attached.</summary>
    private void RestoreDraftImages(IReadOnlyList<DraftImage>? images)
    {
        foreach (var image in images ?? [])
        {
            AddImage(image.Data, image.Name);
        }
        AttachmentError = null;
    }
}
