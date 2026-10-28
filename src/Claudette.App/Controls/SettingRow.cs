using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Claudette.App.ViewModels.Settings;

namespace Claudette.App.Controls;

/// <summary>
/// One setting's row in Settings and Tab settings… (DESIGN.md §14): its title and one line of what it does, the rest
/// behind <b>More</b>, its control at the right (or under the words, <see cref="IsStacked"/>), and under them
/// <see cref="Details"/>, such as a choice's options or the settings that depend on it, indented. It finds its
/// <see cref="SettingItem"/> by <see cref="Key"/> on the page (an <see cref="ISettingsSource"/>), which gives it its
/// words, unless the row sets its own, and its marks: changed from its default (with <b>Reset</b>), this computer's alone,
/// and tabs that have their own. Its look is in Themes/SettingsStyles.axaml.
/// </summary>
/// <remarks>
/// <c>:resettable</c> is a row whose setting has a default to go back to, which keeps <b>Reset</b>'s room whether it
/// shows or not; <c>:nestedpointerover</c> is a row with a row in it pointed at, which shows that row's <b>Reset</b>
/// rather than its own.
/// </remarks>
[PseudoClasses(":changed", ":highlighted", ":stacked", ":nested", ":divided", ":resettable", ":nestedpointerover")]
public sealed class SettingRow : ContentControl
{
    public static readonly StyledProperty<string?> KeyProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Key));

    public static readonly StyledProperty<SettingItem?> SettingProperty = AvaloniaProperty.Register<SettingRow, SettingItem?>(nameof(Setting));

    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Title));

    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(Description));

    public static readonly StyledProperty<string?> MoreProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(More));

    public static readonly StyledProperty<object?> DetailsProperty = AvaloniaProperty.Register<SettingRow, object?>(nameof(Details));

    public static readonly StyledProperty<bool> IsStackedProperty = AvaloniaProperty.Register<SettingRow, bool>(nameof(IsStacked));

    public static readonly StyledProperty<bool> IsMoreOpenProperty = AvaloniaProperty.Register<SettingRow, bool>(nameof(IsMoreOpen));

    public static readonly StyledProperty<bool> IsChangedProperty = AvaloniaProperty.Register<SettingRow, bool>(nameof(IsChanged));

    public static readonly StyledProperty<bool> ShowsMachineMarkProperty = AvaloniaProperty.Register<SettingRow, bool>(nameof(ShowsMachineMark));

    public static readonly StyledProperty<string?> TabsNoteProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(TabsNote));

    public static readonly StyledProperty<string?> TabsTipProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(TabsTip));

    public static readonly StyledProperty<ICommand?> ResetCommandProperty = AvaloniaProperty.Register<SettingRow, ICommand?>(nameof(ResetCommand));

    public static readonly StyledProperty<string?> ResetNameProperty = AvaloniaProperty.Register<SettingRow, string?>(nameof(ResetName));

    static SettingRow()
    {
        KeyProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.FindSetting());
        SettingProperty.Changed.AddClassHandler<SettingRow>((row, e) => row.UseSetting(e.OldValue as SettingItem, e.NewValue as SettingItem));
        IsStackedProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.PseudoClasses.Set(":stacked", row.IsStacked));
        IsChangedProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.PseudoClasses.Set(":changed", row.IsChanged));
        IsPointerOverProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.TellRowsAbove());
        DividedPanel.HasDividerAboveProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.PseudoClasses.Set(":divided", DividedPanel.GetHasDividerAbove(row)));
        ContentProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.NameControl());
        TitleProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.NameControl());
        DescriptionProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.PlaceMore());
        MoreProperty.Changed.AddClassHandler<SettingRow>((row, _) => row.PlaceMore());
        // What's under the words belongs to the row, as its content does: it takes the row's data, and a row in it is nested.
        DetailsProperty.Changed.AddClassHandler<SettingRow>((row, e) =>
        {
            row._detailsShown?.Dispose();
            row._detailsShown = null;
            if (e.OldValue is Control old)
            {
                row.LogicalChildren.Remove(old);
            }
            if (e.NewValue is Control details)
            {
                row.LogicalChildren.Add(details);
                // A note that only sometimes shows takes no room while it's hidden.
                row._detailsShown = details.GetObservable(IsVisibleProperty).Subscribe(new Avalonia.Reactive.AnonymousObserver<bool>(shown => row.ShowsDetails = shown));
            }
            else
            {
                row.ShowsDetails = e.NewValue is not null;
            }
        });
    }

    private IDisposable? _detailsShown;

    public static readonly StyledProperty<bool> ShowsMoreAfterTitleProperty = AvaloniaProperty.Register<SettingRow, bool>(nameof(ShowsMoreAfterTitle));

    public static readonly StyledProperty<bool> ShowsMoreAfterLineProperty = AvaloniaProperty.Register<SettingRow, bool>(nameof(ShowsMoreAfterLine));

    /// <summary><b>More</b> follows the title: there's more to say, and no line under the title to follow.</summary>
    public bool ShowsMoreAfterTitle
    {
        get => GetValue(ShowsMoreAfterTitleProperty);
        private set => SetValue(ShowsMoreAfterTitleProperty, value);
    }

    /// <summary><b>More</b> follows the line under the title.</summary>
    public bool ShowsMoreAfterLine
    {
        get => GetValue(ShowsMoreAfterLineProperty);
        private set => SetValue(ShowsMoreAfterLineProperty, value);
    }

    private void PlaceMore()
    {
        var more = !string.IsNullOrEmpty(More);
        var line = !string.IsNullOrEmpty(Description);
        ShowsMoreAfterTitle = more && !line;
        ShowsMoreAfterLine = more && line;
        WriteLine();
    }

    private TextBlock? _line;

    /// <summary><b>More</b> at the end of the line, where its last word ends however the line wraps.</summary>
    private readonly ToggleButton _moreAfterLine = new() { Classes = { "morelink" } };

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _moreAfterLine[!ToggleButton.IsCheckedProperty] = new Binding(nameof(IsMoreOpen)) { Source = this, Mode = BindingMode.TwoWay };
        _line = e.NameScope.Find<TextBlock>("PART_Description");
        WriteLine();
    }

    private void WriteLine()
    {
        if (_line is null)
        {
            return;
        }
        _line.Inlines ??= [];
        _line.Inlines.Clear();
        _line.Inlines.Add(new Run(Description));
        if (ShowsMoreAfterLine)
        {
            _line.Inlines.Add(new Run("  "));
            _line.Inlines.Add(new InlineUIContainer(_moreAfterLine) { BaselineAlignment = BaselineAlignment.TextBottom });
        }
    }

    public static readonly StyledProperty<bool> ShowsDetailsProperty = AvaloniaProperty.Register<SettingRow, bool>(nameof(ShowsDetails));

    /// <summary>There are details, and they're shown.</summary>
    public bool ShowsDetails
    {
        get => GetValue(ShowsDetailsProperty);
        private set => SetValue(ShowsDetailsProperty, value);
    }

    /// <summary>The setting's key on the page, such as <c>appearance.theme</c>; links and search scroll to it.</summary>
    public string? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    /// <summary>The setting the row shows: found by <see cref="Key"/>, or bound.</summary>
    public SettingItem? Setting
    {
        get => GetValue(SettingProperty);
        set => SetValue(SettingProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>One line of what it does.</summary>
    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>The rest, shown by <b>More</b>.</summary>
    public string? More
    {
        get => GetValue(MoreProperty);
        set => SetValue(MoreProperty, value);
    }

    /// <summary>Under the words: a choice's options, what's going on now, or the settings that depend on this one.</summary>
    public object? Details
    {
        get => GetValue(DetailsProperty);
        set => SetValue(DetailsProperty, value);
    }

    /// <summary>The control goes under the words, at full width, as a path or a message does, rather than at the right.</summary>
    public bool IsStacked
    {
        get => GetValue(IsStackedProperty);
        set => SetValue(IsStackedProperty, value);
    }

    public bool IsMoreOpen
    {
        get => GetValue(IsMoreOpenProperty);
        set => SetValue(IsMoreOpenProperty, value);
    }

    public bool IsChanged
    {
        get => GetValue(IsChangedProperty);
        set => SetValue(IsChangedProperty, value);
    }

    public bool ShowsMachineMark
    {
        get => GetValue(ShowsMachineMarkProperty);
        set => SetValue(ShowsMachineMarkProperty, value);
    }

    public string? TabsNote
    {
        get => GetValue(TabsNoteProperty);
        set => SetValue(TabsNoteProperty, value);
    }

    public string? TabsTip
    {
        get => GetValue(TabsTipProperty);
        set => SetValue(TabsTipProperty, value);
    }

    public ICommand? ResetCommand
    {
        get => GetValue(ResetCommandProperty);
        set => SetValue(ResetCommandProperty, value);
    }

    /// <summary>What a screen reader calls the row's <b>Reset</b>: "Reset Theme to its default".</summary>
    public string? ResetName
    {
        get => GetValue(ResetNameProperty);
        set => SetValue(ResetNameProperty, value);
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        PseudoClasses.Set(":nested", this.GetLogicalAncestors().OfType<SettingRow>().Any());
        FindSetting();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        FindSetting();
    }

    /// <summary>Its setting, by key, on the nearest page or dialog that has one.</summary>
    private void FindSetting()
    {
        if (Key is not { } key)
        {
            return;
        }
        var source = DataContext as ISettingsSource
            ?? this.GetLogicalAncestors().OfType<Control>().Select(c => c.DataContext).OfType<ISettingsSource>().FirstOrDefault();
        if (source?.FindSetting(key) is { } setting)
        {
            Setting = setting;
        }
    }

    private void UseSetting(SettingItem? old, SettingItem? setting)
    {
        if (old is not null)
        {
            old.PropertyChanged -= OnSettingChanged;
        }
        PseudoClasses.Set(":resettable", setting?.CanReset == true);
        if (setting is null)
        {
            return;
        }
        setting.PropertyChanged += OnSettingChanged;
        // Below the row's own: a row that words a setting its own way, or says what's going on now, keeps its words.
        SetValue(TitleProperty, setting.Title, BindingPriority.Style);
        SetValue(DescriptionProperty, setting.Description, BindingPriority.Style);
        SetValue(MoreProperty, setting.More, BindingPriority.Style);
        SetValue(ResetCommandProperty, setting.ResetCommand, BindingPriority.Style);
        SetValue(ResetNameProperty, $"Reset {setting.Title} to its default", BindingPriority.Style);
        ShowMarks(setting);
    }

    /// <summary>
    /// The rows this one is nested in learn whether a row in them is pointed at: the pointer is over them too, but the
    /// <b>Reset</b> that shows is the nested row's, not theirs.
    /// </summary>
    private void TellRowsAbove()
    {
        foreach (var above in this.GetLogicalAncestors().OfType<SettingRow>())
        {
            above.PseudoClasses.Set(":nestedpointerover", above.GetLogicalDescendants().OfType<SettingRow>().Any(r => r.IsPointerOver));
        }
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is SettingItem setting && ReferenceEquals(setting, Setting))
        {
            ShowMarks(setting);
        }
    }

    private void ShowMarks(SettingItem setting)
    {
        IsChanged = setting.IsChanged;
        ShowsMachineMark = setting.ShowsMachineMark;
        TabsNote = setting.TabsNote;
        TabsTip = setting.TabsTip;
        PseudoClasses.Set(":highlighted", setting.IsHighlighted);
    }

    /// <summary>
    /// A screen reader names the row's control by the row's title, unless it has a name of its own: a switch or a
    /// dropdown beside a title is otherwise read without one.
    /// </summary>
    private void NameControl()
    {
        if (Content is Control control && Title is { Length: > 0 } title
            && (control.GetValue(AutomationProperties.NameProperty) is not { Length: > 0 } || control.IsSet(NamedByRowProperty)))
        {
            control.SetValue(AutomationProperties.NameProperty, title);
            control.SetValue(NamedByRowProperty, true);
        }
    }

    /// <summary>The control's accessible name came from its row, so a new title replaces it.</summary>
    private static readonly AttachedProperty<bool> NamedByRowProperty = AvaloniaProperty.RegisterAttached<SettingRow, Control, bool>("NamedByRow");
}
