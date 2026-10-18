using Claudette.Core.Settings;

namespace Claudette.App.ViewModels.Settings;

/// <summary>
/// Settings → Editor (DESIGN.md §18, "Project files"): which side the Project Files tab's explorer is on, where files
/// and images open, where a file from a conversation shows, word wrap, and the indentation for files that have none.
/// </summary>
public sealed class EditorPage(SettingsContext context) : SettingsPage(context, SettingsCategory.Editor)
{
    public override IEnumerable<SettingsSearchResult> SearchEntries =>
    [
        Entry("Explorer side", pageText: "The explorer is on the"),
        Entry("Open files in Claudette's editor", pageText: "Open files in"),
        Entry("Open images in Claudette's image viewer", pageText: "Open images in"),
        Entry("Show files from a conversation", pageText: "Show a file clicked in a conversation"),
        Entry("Open in the diff window", pageText: "Open in the diff window"),
        Entry("Word wrap", pageText: "Wrap long lines"),
        Entry("Indentation", pageText: "Indent new lines with"),
    ];

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

    /// <summary>A diff tool is set in Settings → Diff tool, for <b>Open in the diff window</b> to use.</summary>
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

    protected override void ResetSettings()
    {
        Settings.Editor = new EditorSettings();
        Save();
    }
}
