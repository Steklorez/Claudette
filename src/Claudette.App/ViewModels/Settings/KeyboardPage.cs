using System.Collections.ObjectModel;
using Claudette.Core.Settings;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels.Settings;

/// <summary>One command in Settings → Keyboard (DESIGN.md §14).</summary>
public sealed class ShortcutRow(ShortcutCommand command, KeyboardSettings settings) : ShortcutEditor
{
    public ShortcutCommand Command { get; } = command;

    public string Label => Command.Label;

    /// <summary>Its row's key: its setting, a binding in <c>keyboard.bindings</c>.</summary>
    public string Key => KeyboardPage.KeyOf(Command);

    public override KeyChord? Shortcut => KeyboardShortcuts.Resolve(settings, Command.Id);

    /// <summary>Changed from the default, so <b>Reset</b> applies.</summary>
    public bool IsCustomized => settings.Bindings.ContainsKey(Command.Id);

    protected override void OnRefresh() => OnPropertyChanged(nameof(IsCustomized));
}

/// <summary>
/// Settings → Keyboard (DESIGN.md §14, "Keyboard shortcuts"): the key that sends a message, every command's shortcut,
/// each rebindable, and the quick suffixes with theirs. Click a shortcut and press the new keys; the window passes them
/// to the <see cref="ShortcutRecorder"/>.
/// </summary>
public sealed partial class KeyboardPage : SettingsPage
{
    public KeyboardPage(SettingsContext context) : base(context, SettingsCategory.Keyboard)
    {
        Section("sending", "Sending",
        [
            Setting("keyboard.sendKey", "Send with", s => s.Keyboard.SendKey, (s, v) => s.Keyboard.SendKey = v,
                more: $"With {PrimaryEnter}, it still answers a waiting prompt when nothing is typed, as Allow the waiting prompt does.",
                keywords: ["enter", "new line", "submit"]),
        ]);
        Section("shortcuts", "Shortcuts",
        [
            .. KeyboardShortcuts.All.Select(command => Setting(KeyOf(command), command.Label,
                () => !Settings.Keyboard.Bindings.ContainsKey(command.Id), () => Settings.Keyboard.Bindings.Remove(command.Id),
                keywords: ["shortcut", "keys", "hotkey"])),
            Info("keyboard.fixedKeys", "Keys that can't be changed", keywords: ["paste as text", "1–9"]),
        ], description: "Click a shortcut, then press the keys you want; Esc cancels.");
        Section("quickSuffixes", "Quick suffixes",
        [
            Setting("quickSuffixes", "Quick suffixes", s => s.QuickSuffixes, (s, v) => s.QuickSuffixes = v,
                keywords: ["snippets", "add suffix", "suffix shortcuts", "append"]),
        ], confirm: "Put back the built-in suffixes? Your own are deleted.");
        LoadRows();
        LoadSuffixes();
        context.Shortcuts.Saved += OnShortcutsSaved;
    }

    public override string IconKey => "IconSettingsKeyboard";

    public override string Description => "Sending a message, every shortcut, and the snippets you add to messages.";

    /// <summary>A command's setting: its binding in <c>keyboard.bindings</c>.</summary>
    internal static string KeyOf(ShortcutCommand command) => $"keyboard.bindings.{command.Id}";

    // ---- Send with (DESIGN.md §14, "Keyboard shortcuts") ----------------------------------------------------------

    /// <summary>Enter sends, Shift+Enter starts a new line.</summary>
    public bool SendWithEnter
    {
        get => Settings.Keyboard.SendKey == SendKey.Enter;
        set
        {
            if (value)
            {
                SetSendKey(SendKey.Enter);
            }
        }
    }

    /// <summary>Ctrl/Cmd+Enter sends, Enter starts a new line.</summary>
    public bool SendWithPrimaryEnter
    {
        get => Settings.Keyboard.SendKey == SendKey.PrimaryEnter;
        set
        {
            if (value)
            {
                SetSendKey(SendKey.PrimaryEnter);
            }
        }
    }

    public string PrimaryEnterLabel => $"{PrimaryEnter}, with Enter for a new line";

    /// <summary>Ctrl+Enter, or ⌘↩ on a Mac.</summary>
    private static string PrimaryEnter => new KeyChord(ChordModifiers.Primary, "Enter").Display(Claudette.App.Services.Shortcuts.IsMac);

    /// <summary>The keys that can't be rebound, behind More on their row.</summary>
    public string FixedKeysText =>
        $"In the message box, {new KeyChord(ChordModifiers.Primary | ChordModifiers.Shift, "V").Display(Claudette.App.Services.Shortcuts.IsMac)} pastes text as it is, however long. "
        + "In the new tab picker, 1–9 pick a folder; in the quick suffixes menu, 1–9 pick a suffix. "
        + EditorKeysText;

    /// <summary>The Project Files tab's own keys (DESIGN.md §18, "Project files", "Keyboard").</summary>
    private static string EditorKeysText
    {
        get
        {
            var mac = Claudette.App.Services.Shortcuts.IsMac;
            string Keys(ChordModifiers modifiers, string key) => new KeyChord(modifiers, key).Display(mac);
            var replace = mac ? Keys(ChordModifiers.Primary | ChordModifiers.Alt, "F") : Keys(ChordModifiers.Primary, "H");
            var files = mac ? $"{Keys(ChordModifiers.Primary | ChordModifiers.Alt, "Right")} and {Keys(ChordModifiers.Primary | ChordModifiers.Alt, "Left")}" : "Ctrl+PageDown and Ctrl+PageUp";
            return $"In a Project Files tab, {Keys(ChordModifiers.Primary, "S")} saves the file and {Keys(ChordModifiers.Primary | ChordModifiers.Alt, "S")} every file, "
                + $"{replace} replaces, Ctrl+G goes to a line, {(mac ? "Option" : "Alt")}+Z wraps long lines, and {files} go to the next and previous file.";
        }
    }

    private void SetSendKey(SendKey key)
    {
        if (Settings.Keyboard.SendKey == key)
        {
            return;
        }
        Settings.Keyboard.SendKey = key;
        OnPropertyChanged(nameof(SendWithEnter));
        OnPropertyChanged(nameof(SendWithPrimaryEnter));
        Save();
    }

    // ---- Shortcuts ---------------------------------------------------------------------------------------------------

    public ObservableCollection<ShortcutRow> ShortcutRows { get; } = [];

    private void LoadRows()
    {
        ShortcutRows.Clear();
        foreach (var command in KeyboardShortcuts.All)
        {
            ShortcutRows.Add(new ShortcutRow(command, Settings.Keyboard));
        }
    }

    [RelayCommand]
    private void StartRecording(ShortcutEditor? editor) => Context.Shortcuts.Start(editor);

    /// <summary>Removes a shortcut: the command is then only in menus and buttons.</summary>
    [RelayCommand]
    private void ClearShortcut(ShortcutEditor? editor) => Context.Shortcuts.Clear(editor);

    private void OnShortcutsSaved()
    {
        foreach (var row in ShortcutRows)
        {
            row.Refresh();
        }
        foreach (var suffix in Suffixes)
        {
            suffix.Refresh();
        }
        OnPropertyChanged(nameof(SuffixesShortcutText));
    }

    /// <summary>A shortcut being recorded stops, and the rows and suffixes show what they are now.</summary>
    protected override void OnReset()
    {
        Context.Shortcuts.Cancel();
        LoadSuffixes();
        OnShortcutsSaved();
    }
}
