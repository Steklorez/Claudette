using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Claudette.App.Services;
using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.App.Views;

namespace Claudette.App.UiTests;

/// <summary>Find in files' popup, rendered (DESIGN.md §18, "Finding in files").</summary>
public sealed class FindInFilesUiTests
{
    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [AvaloniaFact]
    public async Task The_popup_shows_the_matches_and_the_selected_one_in_its_file()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        Write(Path.Combine(h.WorkFolder, "Source", "Player.cpp"), "float Health = 100.f;\nfloat Speed = 600.f;\n");
        Write(Path.Combine(h.WorkFolder, "Config.h"), "#define SPEED 600\n");
        await h.OpenTabAsync();
        var window = UiText.Show(new ShellView { DataContext = h.Shell }, width: 1300, height: 900);

        h.Shell.OpenReplaceInFilesCommand.Execute(null);
        var find = h.Shell.FindInFiles!;
        find.Query = "speed";
        h.Time.Advance(FindInFilesViewModel.SearchDelay);
        await Waiting.UntilAsync(() =>
        {
            UiText.Settle(window);
            return find is { IsSearching: false, HasPreview: true } && find.Hits.Count == 2;
        }, "the matches and the preview");
        UiText.Settle(window);

        var view = window.GetVisualDescendants().OfType<FindInFilesView>().Single();
        Assert.True(view.IsEffectivelyVisible);
        // The preview shows the selected match's file, with the match selected.
        var preview = view.GetVisualDescendants().OfType<TextEditor>().Single(e => e.Name == "PreviewEditor");
        Assert.Equal(find.SelectedHit!.Match, preview.SelectedText);
        // The keyboard is in its box.
        Assert.True(view.GetVisualDescendants().OfType<TextBox>().Single(b => b.Name == "QueryBox").IsFocused);

        // Verify resumes off the UI thread, so it comes last.
        await Verify(UiText.Describe(view, (h.Root, "{root}")));
    }
}
