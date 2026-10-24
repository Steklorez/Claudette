using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Data;
using Avalonia.LogicalTree;
using Claudette.App.ViewModels.Settings;

namespace Claudette.App.Controls;

/// <summary>
/// A section of a Settings page or of Tab settings… (DESIGN.md §14): a heading, a line under it, its rows, and its own
/// <b>Reset to defaults</b>. It finds its <see cref="SettingsGroup"/> by <see cref="Key"/>, which says whether its settings
/// are all this computer's (said once, in the heading) and whether any differ from their defaults. Its rows are drawn
/// one under another with a line between them, and in the Claude style in a rounded box, as the Claude apps' settings
/// are. Its look is in Themes/SettingsStyles.axaml.
/// </summary>
[PseudoClasses(":highlighted")]
public sealed class SettingsSection : ItemsControl
{
    public static readonly StyledProperty<string?> KeyProperty = AvaloniaProperty.Register<SettingsSection, string?>(nameof(Key));

    public static readonly StyledProperty<SettingsGroup?> GroupProperty = AvaloniaProperty.Register<SettingsSection, SettingsGroup?>(nameof(Group));

    public static readonly StyledProperty<string?> HeaderProperty = AvaloniaProperty.Register<SettingsSection, string?>(nameof(Header));

    public static readonly StyledProperty<string?> DescriptionProperty = AvaloniaProperty.Register<SettingsSection, string?>(nameof(Description));

    public static readonly StyledProperty<bool> IsMachineOnlyProperty = AvaloniaProperty.Register<SettingsSection, bool>(nameof(IsMachineOnly));

    public static readonly StyledProperty<string> ResetTextProperty = AvaloniaProperty.Register<SettingsSection, string>(nameof(ResetText), "Reset to defaults");

    static SettingsSection()
    {
        KeyProperty.Changed.AddClassHandler<SettingsSection>((section, _) => section.FindGroup());
        GroupProperty.Changed.AddClassHandler<SettingsSection>((section, e) => section.UseGroup(e.OldValue as SettingsGroup, e.NewValue as SettingsGroup));
    }

    /// <summary>The section's key on the page; links and search scroll to it.</summary>
    public string? Key
    {
        get => GetValue(KeyProperty);
        set => SetValue(KeyProperty, value);
    }

    public SettingsGroup? Group
    {
        get => GetValue(GroupProperty);
        set => SetValue(GroupProperty, value);
    }

    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>All its settings are this computer's: "This computer" shows once, beside the heading.</summary>
    public bool IsMachineOnly
    {
        get => GetValue(IsMachineOnlyProperty);
        set => SetValue(IsMachineOnlyProperty, value);
    }

    /// <summary>Its reset's label: <b>Reset to defaults</b>, or <b>Use defaults</b> in Tab settings….</summary>
    public string ResetText
    {
        get => GetValue(ResetTextProperty);
        set => SetValue(ResetTextProperty, value);
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        FindGroup();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        FindGroup();
    }

    private void FindGroup()
    {
        if (Key is not { } key)
        {
            return;
        }
        var source = DataContext as ISettingsSource
            ?? this.GetLogicalAncestors().OfType<Control>().Select(c => c.DataContext).OfType<ISettingsSource>().FirstOrDefault();
        if (source?.FindGroup(key) is { } group)
        {
            Group = group;
        }
    }

    private void UseGroup(SettingsGroup? old, SettingsGroup? group)
    {
        if (old is not null)
        {
            old.PropertyChanged -= OnGroupChanged;
        }
        if (group is null)
        {
            return;
        }
        group.PropertyChanged += OnGroupChanged;
        SetValue(HeaderProperty, group.Title, BindingPriority.Style);
        SetValue(DescriptionProperty, group.Description, BindingPriority.Style);
        ShowMarks(group);
    }

    private void OnGroupChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is SettingsGroup group && ReferenceEquals(group, Group))
        {
            ShowMarks(group);
        }
    }

    private void ShowMarks(SettingsGroup group)
    {
        IsMachineOnly = group.ShowsMachineMark;
        PseudoClasses.Set(":highlighted", group.IsHighlighted);
    }

}
