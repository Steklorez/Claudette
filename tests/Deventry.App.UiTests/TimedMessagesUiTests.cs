using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels;
using Deventry.App.Views;
using Deventry.Core.TimedMessages;

namespace Deventry.App.UiTests;

/// <summary>DESIGN.md §18, "Timed messages": the cup's clock and menu, and Settings → Timed messages.</summary>
public class TimedMessagesUiTests
{
    private const string Text = "Triage the overnight issues.";

    private static TimedMessage Daily(TabTestHarness h, string name = "Morning triage") => new()
    {
        Name = name,
        Folder = h.WorkFolder,
        Text = Text,
        Schedule = new TimedSchedule { Kind = TimedScheduleKind.Days, Time = new TimeOnly(7, 0), Days = [.. TimedSchedule.EveryDay] },
    };

    [AvaloniaFact]
    public async Task The_cup_has_its_clock_while_a_message_is_on_and_its_menu_clears_one()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        h.Shell.Restore(null);
        var main = new MainWindowViewModel(h.Services);
        main.UseShell(h.Shell);
        var window = new MainWindow { DataContext = main, Width = 1200, Height = 800 };
        window.Show();
        UiText.Settle(window);
        var cup = window.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Name == "KeepAwakeToggle");
        var clock = cup.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "KeepAwakeClock");
        Assert.False(clock.IsEffectivelyVisible);

        h.Services.TimedMessages.Save(Daily(h));
        h.Services.TimedMessages.Save(Daily(h, "Build check"));
        UiText.Settle(window);

        Assert.True(clock.IsEffectivelyVisible);
        Assert.Contains("Next timed message: Morning triage in work, ", ToolTip.GetTip(cup) as string, StringComparison.Ordinal);
        var menu = Assert.IsType<ContextMenu>(cup.ContextMenu);
        menu.Open(cup);
        UiText.Settle(window);
        var entries = menu.Items.OfType<MenuItem>().Select(i => i.Header).OfType<string>().ToList();
        Assert.Equal("Timed messages", entries[0]);
        Assert.StartsWith("Morning triage · work · ", entries[1], StringComparison.Ordinal);
        Assert.StartsWith("Build check · work · ", entries[2], StringComparison.Ordinal);
        Assert.Equal(["Clear all…", "Add a timed message…", "Keep awake"], entries[3..6]);
        var triage = menu.Items.OfType<MenuItem>().Single(i => (i.Header as string)?.StartsWith("Morning triage", StringComparison.Ordinal) == true);
        Assert.Equal(["Edit…", "Clear…"], triage.Items.OfType<MenuItem>().Select(i => i.Header as string));

        var clear = triage.Items.OfType<MenuItem>().Last();
        clear.Command!.Execute(clear.CommandParameter);
        menu.Close();

        // It asks first.
        var confirmation = Assert.IsType<ConfirmationViewModel>(h.Shell.Confirmation);
        Assert.Equal("Clear Morning triage?", confirmation.Title);
        Assert.Equal(2, h.Services.Settings.TimedMessages.Messages.Count);
        await confirmation.ConfirmCommand.ExecuteAsync(null);
        UiText.Settle(window);
        Assert.Equal(["Build check"], h.Services.Settings.TimedMessages.Messages.Select(m => m.Name));
        Assert.True(clock.IsEffectivelyVisible);

        // The section is made again each time the menu opens.
        menu.Open(cup);
        UiText.Settle(window);
        Assert.Single(menu.Items.OfType<MenuItem>(), i => (i.Header as string)?.StartsWith("Build check", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), i => (i.Header as string)?.StartsWith("Morning triage", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), i => i.Header as string == "Clear all…");
        menu.Close();

        h.Services.TimedMessages.DeleteAll();
        UiText.Settle(window);
        Assert.False(clock.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_message_is_added_in_Settings_and_deleted_there()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        using var settings = new SettingsViewModel(h.Services, null, host: h.Shell) { SelectedCategory = SettingsCategory.TimedMessages };
        var window = new SettingsWindow { DataContext = settings, Width = 900, Height = 700 };
        window.Show();
        UiText.Settle(window);
        var page = settings.TimedMessages;
        Assert.False(page.HasRows);

        page.AddCommand.Execute(null);
        UiText.Settle(window);

        var editor = Assert.IsType<Deventry.App.ViewModels.Settings.TimedMessageEditor>(page.Editor);
        Assert.True(editor.IsNew);
        Assert.Equal("Give it a name.", editor.Problem);
        Assert.True(editor.IsDays);
        editor.Name = "Morning triage";
        editor.Folder = h.WorkFolder;
        Assert.Equal("Write its message.", editor.Problem);
        editor.Text = Text;
        editor.IsCron = true;
        editor.Cron = "0 25 * * *";
        Assert.Equal("Hour 25 is past 23.", editor.Problem);
        Assert.False(editor.CanSave);
        editor.Cron = "0 7 * * MON-FRI";
        Assert.StartsWith("Next: ", editor.Preview, StringComparison.Ordinal);
        Assert.True(editor.CanSave);
        // The editor's on the page, with its fields drawn.
        Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(), t => t.IsEffectivelyVisible && t.Text == "0 7 * * MON-FRI");

        editor.SaveCommand.Execute(null);
        UiText.Settle(window);

        Assert.Null(page.Editor);
        var row = Assert.Single(page.Rows);
        Assert.Equal("Morning triage", row.Name);
        Assert.Equal("work · 0 7 * * MON-FRI", row.Detail);
        Assert.True(row.IsOn);
        var saved = Assert.Single(h.Services.Settings.TimedMessages.Messages);
        Assert.Equal(TimedScheduleKind.Cron, saved.Schedule.Kind);

        row.Removal.AskCommand.Execute(null);
        Assert.Equal("Delete Morning triage? It won't be sent again.", row.RemoveText);
        await row.Removal.ConfirmCommand.ExecuteAsync(null);
        UiText.Settle(window);

        Assert.Empty(page.Rows);
        Assert.Empty(h.Services.Settings.TimedMessages.Messages);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_groups_Add_a_timed_message_opens_the_editor_in_its_folder()
    {
        await using var h = new TabTestHarness(dispatcher: new AvaloniaUiDispatcher());
        // What the group's menu asks for before it opens Settings.
        h.Services.TimedMessages.RequestEditor(TimedMessagesService.NewMessage(h.WorkFolder));

        using var settings = new SettingsViewModel(h.Services, null, host: h.Shell);

        var editor = Assert.IsType<Deventry.App.ViewModels.Settings.TimedMessageEditor>(settings.TimedMessages.Editor);
        Assert.Equal(h.WorkFolder, editor.Folder);
        Assert.Null(h.Services.TimedMessages.TakeEditorRequest());
    }
}
