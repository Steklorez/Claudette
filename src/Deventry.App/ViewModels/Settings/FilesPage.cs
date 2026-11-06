using Deventry.Core.Settings;

namespace Deventry.App.ViewModels.Settings;

/// <summary>
/// Settings → Files and diffs (DESIGN.md §18, "Project files"; §8, "External diff tool"): where files and images open,
/// where a file from a conversation shows, the Project Files tab's explorer, word wrap and the indentation for files
/// that have none, and the diff tool.
/// </summary>
public sealed partial class FilesPage : SettingsPage
{
    public FilesPage(SettingsContext context) : base(context, SettingsCategory.Files)
    {
        Section("opening", "Opening files",
        [
            Setting("editor.openFilesIn", "Open files in", s => s.Editor.OpenFilesIn, (s, v) => s.Editor.OpenFilesIn = v,
                "A file named in Claude's replies, Changed files' Open, and the files on Claude's tool cards.",
                "Programs and binary files still go to the app the OS uses for them.", ["editor", "external editor"]),
            Setting("editor.filesFromConversation", "Show a file clicked in a conversation", s => s.Editor.FilesFromConversation, (s, v) => s.Editor.FilesFromConversation = v,
                keywords: ["beside", "split", "pane"]),
            Setting("editor.openImagesIn", "Open images in", s => s.Editor.OpenImagesIn, (s, v) => s.Editor.OpenImagesIn = v,
                "PNG, JPEG, GIF, WebP and BMP; other kinds go to the app the OS uses.",
                "An image file named in Claude's replies, a link to an image on the web, Changed files' Open, the files on Claude's tool cards and the explorer.",
                ["image viewer", "pictures"]),
        ]);
        Section("projectFiles", "Project Files tab",
        [
            Setting("editor.explorerSide", "The explorer is on the", s => s.Editor.ExplorerSide, (s, v) => s.Editor.ExplorerSide = v,
                keywords: ["explorer side", "left", "right", "tree"]),
            Setting("editor.wordWrap", "Wrap long lines", s => s.Editor.WordWrap, (s, v) => s.Editor.WordWrap = v, keywords: ["word wrap"]),
            // Two values in settings.json, one choice here.
            Setting("editor.indent", "Indent new lines with",
                () => Settings.Editor.IndentSize == new EditorSettings().IndentSize && Settings.Editor.IndentWithTabs == new EditorSettings().IndentWithTabs,
                () => (Settings.Editor.IndentSize, Settings.Editor.IndentWithTabs) = (new EditorSettings().IndentSize, new EditorSettings().IndentWithTabs),
                "In a file that has no indentation to go by; one that does is indented as it is.", keywords: ["indentation", "tabs", "spaces"]),
            // Git's before, so it keeps its key.
            Setting("git.ignoredFiles", "Ignored files", s => s.Git.IgnoredFiles, (s, v) => s.Git.IgnoredFiles = v,
                "What git or Perforce's P4IGNORE ignores, in the explorer. Its ⋯ menu changes it too, and a change applies to open Files tabs at once.",
                "Outside a git repository and a Perforce workspace, the folders the @ list skips, such as node_modules, bin and obj.",
                ["gitignore", ".gitignore", "p4ignore", ".p4ignore", "ignored", "explorer", "hide", "dim", "node_modules"]),
            Setting("editor.hiddenFiles", "Hidden files", s => s.Editor.HiddenFiles, (s, v) => s.Editor.HiddenFiles = v,
                "Dotfiles, such as .gitignore and .github, and what the system hides, in the explorer. Its ⋯ menu changes it too.",
                "Everything in a hidden folder is hidden too.",
                ["dotfiles", "hidden", "explorer", "hide", "dim"]),
        ], description: "Each folder group's tab of its project's folders and files, opened with the button beside its +.");
        Section("diffTool", "Diff tool",
        [
            Setting("diffTool.kind", "Diff tool", () => Settings.DiffTool.Kind == "builtIn", () => Settings.DiffTool = new DiffToolSettings(),
                "How changed files open. Tools installed on this machine are listed.",
                "With a tool set, double-click a changed file to open it there; the built-in view is still in the file's menu.",
                ["beyond compare", "meld", "kdiff3", "winmerge", "compare"]),
            Setting("diffTool.customCommand", "Command", s => s.DiffTool.CustomCommand, (s, v) => s.DiffTool.CustomCommand = v,
                "A program and its arguments, with {left} and {right}.",
                "{left} is the file before Claude's changes (a read-only copy), {right} is the file now; {leftTitle} and {rightTitle} are their titles.",
                ["custom diff command"]),
            Setting("editor.diffWindowOpen", "Open in the diff window", s => s.Editor.DiffWindowOpen, (s, v) => s.Editor.DiffWindowOpen = v,
                "The diff window closes when the file opens in Deventry's editor, so the editor isn't hidden behind it.", keywords: ["diff window"]),
        ]);
    }

    public override string IconKey => "IconFiles";

    public override string Description => "Where files open, the Project Files tab, and the tool that shows Claude's changes.";

    private EditorSettings Editor => Settings.Editor;

    public bool ExplorerOnLeft
    {
        get => Editor.ExplorerSide == ExplorerSide.Left;
        set
        {
            if (value)
            {
                SetSide(ExplorerSide.Left);
            }
        }
    }

    public bool ExplorerOnRight
    {
        get => Editor.ExplorerSide == ExplorerSide.Right;
        set
        {
            if (value)
            {
                SetSide(ExplorerSide.Right);
            }
        }
    }

    /// <summary><b>Ignored files</b> are dimmed in the explorer.</summary>
    public bool IgnoredFilesDimmed
    {
        get => Settings.Git.IgnoredFiles == IgnoredFiles.Dimmed;
        set
        {
            if (value)
            {
                SetIgnoredFiles(IgnoredFiles.Dimmed);
            }
        }
    }

    /// <summary><b>Ignored files</b> are left out of the explorer.</summary>
    public bool IgnoredFilesHidden
    {
        get => Settings.Git.IgnoredFiles == IgnoredFiles.Hidden;
        set
        {
            if (value)
            {
                SetIgnoredFiles(IgnoredFiles.Hidden);
            }
        }
    }

    private void SetIgnoredFiles(IgnoredFiles files)
    {
        if (Settings.Git.IgnoredFiles == files)
        {
            return;
        }
        Settings.Git.IgnoredFiles = files;
        OnPropertyChanged(nameof(IgnoredFilesDimmed));
        OnPropertyChanged(nameof(IgnoredFilesHidden));
        Save();
    }

    /// <summary><b>Hidden files</b> are dimmed in the explorer.</summary>
    public bool HiddenFilesDimmed
    {
        get => Editor.HiddenFiles == HiddenFiles.Dimmed;
        set
        {
            if (value)
            {
                SetHiddenFiles(HiddenFiles.Dimmed);
            }
        }
    }

    /// <summary><b>Hidden files</b> are left out of the explorer.</summary>
    public bool HiddenFilesHidden
    {
        get => Editor.HiddenFiles == HiddenFiles.Hidden;
        set
        {
            if (value)
            {
                SetHiddenFiles(HiddenFiles.Hidden);
            }
        }
    }

    private void SetHiddenFiles(HiddenFiles files)
    {
        if (Editor.HiddenFiles == files)
        {
            return;
        }
        Editor.HiddenFiles = files;
        OnPropertyChanged(nameof(HiddenFilesDimmed));
        OnPropertyChanged(nameof(HiddenFilesHidden));
        Save();
    }

    private void SetSide(ExplorerSide side)
    {
        Editor.ExplorerSide = side;
        OnPropertyChanged(nameof(ExplorerOnLeft));
        OnPropertyChanged(nameof(ExplorerOnRight));
        Save();
    }

    public bool OpenInEditor
    {
        get => Editor.OpenFilesIn == OpenFilesIn.Editor;
        set
        {
            if (value)
            {
                SetOpenIn(OpenFilesIn.Editor);
            }
        }
    }

    public bool OpenInSystem
    {
        get => Editor.OpenFilesIn == OpenFilesIn.System;
        set
        {
            if (value)
            {
                SetOpenIn(OpenFilesIn.System);
            }
        }
    }

    private void SetOpenIn(OpenFilesIn where)
    {
        Editor.OpenFilesIn = where;
        OnPropertyChanged(nameof(OpenInEditor));
        OnPropertyChanged(nameof(OpenInSystem));
        Save();
    }

    public bool ImagesInViewer
    {
        get => Editor.OpenImagesIn == OpenImagesIn.Viewer;
        set
        {
            if (value)
            {
                SetImagesIn(OpenImagesIn.Viewer);
            }
        }
    }

    public bool ImagesInSystem
    {
        get => Editor.OpenImagesIn == OpenImagesIn.System;
        set
        {
            if (value)
            {
                SetImagesIn(OpenImagesIn.System);
            }
        }
    }

    private void SetImagesIn(OpenImagesIn where)
    {
        Editor.OpenImagesIn = where;
        OnPropertyChanged(nameof(ImagesInViewer));
        OnPropertyChanged(nameof(ImagesInSystem));
        Save();
    }

    public bool FilesBeside
    {
        get => Editor.FilesFromConversation == FilesFromConversation.Beside;
        set
        {
            if (value)
            {
                SetFromConversation(FilesFromConversation.Beside);
            }
        }
    }

    public bool FilesInPlace
    {
        get => Editor.FilesFromConversation == FilesFromConversation.InPlace;
        set
        {
            if (value)
            {
                SetFromConversation(FilesFromConversation.InPlace);
            }
        }
    }

    private void SetFromConversation(FilesFromConversation where)
    {
        Editor.FilesFromConversation = where;
        OnPropertyChanged(nameof(FilesBeside));
        OnPropertyChanged(nameof(FilesInPlace));
        Save();
    }

    public bool DiffOpensAsAbove
    {
        get => Editor.DiffWindowOpen == DiffWindowOpen.AsOpenFilesIn;
        set
        {
            if (value)
            {
                SetDiffWindowOpen(DiffWindowOpen.AsOpenFilesIn);
            }
        }
    }

    public bool DiffOpensInSystem
    {
        get => Editor.DiffWindowOpen == DiffWindowOpen.System;
        set
        {
            if (value)
            {
                SetDiffWindowOpen(DiffWindowOpen.System);
            }
        }
    }

    public bool DiffOpensInDiffTool
    {
        get => Editor.DiffWindowOpen == DiffWindowOpen.DiffTool;
        set
        {
            if (value)
            {
                SetDiffWindowOpen(DiffWindowOpen.DiffTool);
            }
        }
    }

    /// <summary>A diff tool is set, for <b>Open in the diff window</b> to use.</summary>
    public bool HasDiffTool => Settings.DiffTool.Kind != "builtIn";

    private void SetDiffWindowOpen(DiffWindowOpen what)
    {
        Editor.DiffWindowOpen = what;
        OnPropertyChanged(nameof(DiffOpensAsAbove));
        OnPropertyChanged(nameof(DiffOpensInSystem));
        OnPropertyChanged(nameof(DiffOpensInDiffTool));
        Save();
    }

    public bool WordWrap
    {
        get => Editor.WordWrap;
        set => Set(value, v => Editor.WordWrap = v);
    }

    /// <summary>The choices for files that have no indentation to go by.</summary>
    public IReadOnlyList<string> IndentChoices { get; } = ["2 spaces", "4 spaces", "8 spaces", "Tabs"];

    public string SelectedIndent
    {
        get => Editor.IndentWithTabs ? "Tabs" : $"{Editor.IndentSize} spaces";
        set
        {
            if (value is null)
            {
                return;
            }
            Editor.IndentWithTabs = value == "Tabs";
            if (!Editor.IndentWithTabs && int.TryParse(value.Split(' ')[0], out var size))
            {
                Editor.IndentSize = size;
            }
            OnPropertyChanged();
            Save();
        }
    }
}
