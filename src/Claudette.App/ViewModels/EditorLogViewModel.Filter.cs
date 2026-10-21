using System.Collections.ObjectModel;
using System.Globalization;
using Claudette.App.Services;
using Claudette.Core.ProjectTools.EditorLogs;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// An editor's log's filter (DESIGN.md §18, "Editor logs", "Filtering it"): a chip for each of its engine's levels with
/// how many entries it has, Unreal's categories in a menu with a search box, and a text filter. The levels and
/// categories hidden are the project's: they're remembered, and the project's next log starts with them. The text
/// filter is the log's own.
/// </summary>
public sealed partial class EditorLogViewModel
{
    /// <summary>The project's levels hidden, as their names: <c>Display,Verbose</c>.</summary>
    internal const string HiddenLevelsKey = "logs.hiddenLevels";

    /// <summary>The project's categories hidden: <c>LogNet,LogStreaming</c>.</summary>
    internal const string HiddenCategoriesKey = "logs.hiddenCategories";

    /// <summary>The widest the category column gets, in characters; a longer name pushes its message along.</summary>
    public const int MaxCategoryColumn = 24;

    private readonly bool[] _hiddenLevels = new bool[Enum.GetValues<LogVerbosity>().Length];
    private readonly HashSet<string> _hiddenCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LogCategoryChoice> _categories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Choices being changed together, such as by <b>Show all</b>: one change to the filter once they're done.</summary>
    private bool _changingTogether;

    /// <summary>A chip for each of the engine's levels, in its order: Errors, Warnings, then Display, Log and Verbose, or Messages.</summary>
    public IReadOnlyList<LogLevelChip> Levels { get; private set; } = [];

    /// <summary>Its lines have Unreal's categories, which the Categories menu filters by.</summary>
    public bool HasCategories => _source.HasCategories;

    /// <summary>The categories its entries are in, by name.</summary>
    public ObservableCollection<LogCategoryChoice> Categories { get; } = [];

    /// <summary>Those the menu's search box finds: every one while it's empty.</summary>
    public ObservableCollection<LogCategoryChoice> FoundCategories { get; } = [];

    /// <summary>The Categories menu's search box.</summary>
    [ObservableProperty]
    public partial string CategorySearch { get; set; } = "";

    partial void OnCategorySearchChanged(string value) => FindCategories();

    /// <summary>The text filter: only lines with it are shown, ignoring case.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiltered), nameof(ShownNote))]
    public partial string TextFilter { get; set; } = "";

    partial void OnTextFilterChanged(string value) => OnFilterChanged();

    /// <summary>"Categories", or "Categories (38 of 41)" while some are hidden.</summary>
    public string CategoriesLabel
    {
        get
        {
            var shown = Categories.Count(c => c.IsShown);
            return shown == Categories.Count
                ? "Categories"
                : $"Categories ({shown.ToString("N0", CultureInfo.CurrentCulture)} of {Categories.Count.ToString("N0", CultureInfo.CurrentCulture)})";
        }
    }

    /// <summary>The category column's width in characters: its longest name's, to <see cref="MaxCategoryColumn"/>.</summary>
    [ObservableProperty]
    public partial int CategoryColumn { get; private set; }

    /// <summary>Its lines have times, so Unreal's columns start with one.</summary>
    [ObservableProperty]
    public partial bool HasTimeColumn { get; private set; }

    /// <summary>Some lines are hidden: a level or a category is, or there's text to filter by.</summary>
    public bool IsFiltered => TextFilter.Length > 0 || _hiddenLevels.Any(h => h) || Categories.Any(c => !c.IsShown);

    /// <summary>"Showing 1,204 of 20,000 lines" while it's filtered.</summary>
    public string? ShownNote => IsFiltered
        ? $"Showing {Output.Count(Shows).ToString("N0", CultureInfo.CurrentCulture)} of {Output.Count.ToString("N0", CultureInfo.CurrentCulture)} lines"
        : null;

    public override bool Shows(LogLine line) =>
        !_hiddenLevels[(int)line.Verbosity]
        && (line.Category is not { } category || !_hiddenCategories.Contains(category))
        && (TextFilter.Length == 0 || line.Text.Contains(TextFilter, StringComparison.OrdinalIgnoreCase));

    /// <summary>The engine's levels as chips, with the project's hidden levels and categories hidden.</summary>
    private void CreateFilter()
    {
        var project = _services.ProjectTools.State;
        var hiddenLevels = Names(project.Get(ProjectPath, HiddenLevelsKey));
        foreach (var name in Names(project.Get(ProjectPath, HiddenCategoriesKey)))
        {
            _hiddenCategories.Add(name);
        }
        Levels = _source.Levels.Select(level =>
        {
            var hidden = hiddenLevels.Contains(level.Verbosity.ToString());
            _hiddenLevels[(int)level.Verbosity] = hidden;
            return new LogLevelChip(level.Verbosity, level.Name, !hidden, LevelChanged);
        }).ToArray();
    }

    /// <summary>A read's counts: the chips' and the categories', which a fresh log starts again.</summary>
    private void CountEntries(EditorLogRead read)
    {
        if (read.Fresh)
        {
            foreach (var chip in Levels)
            {
                chip.Count = 0;
            }
            _categories.Clear();
            Categories.Clear();
            CategoryColumn = 0;
        }
        HasTimeColumn = (!read.Fresh && HasTimeColumn) || read.Lines.Any(l => l.Columns?.Time is not null);
        foreach (var chip in Levels)
        {
            chip.Count += read.Counts[chip.Verbosity];
        }
        var added = false;
        foreach (var (name, count) in read.Counts.Categories)
        {
            if (!_categories.TryGetValue(name, out var choice))
            {
                choice = new LogCategoryChoice(name, !_hiddenCategories.Contains(name), CategoryChanged);
                _categories[name] = choice;
                var at = 0;
                while (at < Categories.Count && string.Compare(Categories[at].Name, name, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    at++;
                }
                Categories.Insert(at, choice);
                CategoryColumn = Math.Min(MaxCategoryColumn, Math.Max(CategoryColumn, name.Length));
                added = true;
            }
            choice.Count += count;
        }
        if (added || read.Fresh)
        {
            FindCategories();
            OnPropertyChanged(nameof(CategoriesLabel));
        }
        OnPropertyChanged(nameof(ShownNote));
    }

    /// <summary>The search box finds none of the categories there are.</summary>
    public bool NoCategoryFound => FoundCategories.Count == 0 && Categories.Count > 0;

    private void FindCategories()
    {
        var search = CategorySearch.Trim();
        FoundCategories.Clear();
        foreach (var choice in Categories)
        {
            if (search.Length == 0 || choice.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            {
                FoundCategories.Add(choice);
            }
        }
        OnPropertyChanged(nameof(NoCategoryFound));
    }

    /// <summary><b>Show all</b> in the Categories menu: those it lists.</summary>
    [RelayCommand]
    private void ShowAllCategories() => SetFound(shown: true);

    /// <summary><b>Hide all</b>: those it lists, so ticking one then shows only that one.</summary>
    [RelayCommand]
    private void HideAllCategories() => SetFound(shown: false);

    private void SetFound(bool shown)
    {
        _changingTogether = true;
        try
        {
            foreach (var choice in FoundCategories)
            {
                choice.IsShown = shown;
            }
        }
        finally
        {
            _changingTogether = false;
        }
        CategoryChanged(null);
    }

    /// <summary><b>Clear</b> by the text filter.</summary>
    [RelayCommand]
    private void ClearTextFilter() => TextFilter = "";

    private void LevelChanged(LogLevelChip chip)
    {
        _hiddenLevels[(int)chip.Verbosity] = !chip.IsShown;
        _services.ProjectTools.Remember(ProjectPath, HiddenLevelsKey,
            string.Join(",", Levels.Where(l => !l.IsShown).Select(l => l.Verbosity.ToString())) is { Length: > 0 } names ? names : null);
        Refilter();
    }

    private void CategoryChanged(LogCategoryChoice? choice)
    {
        if (choice is not null)
        {
            if (choice.IsShown)
            {
                _hiddenCategories.Remove(choice.Name);
            }
            else
            {
                _hiddenCategories.Add(choice.Name);
            }
        }
        if (_changingTogether)
        {
            return;
        }
        _services.ProjectTools.Remember(ProjectPath, HiddenCategoriesKey,
            _hiddenCategories.Count > 0 ? string.Join(",", _hiddenCategories.Order(StringComparer.OrdinalIgnoreCase)) : null);
        OnPropertyChanged(nameof(CategoriesLabel));
        Refilter();
    }

    private void Refilter()
    {
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(ShownNote));
        OnFilterChanged();
    }

    private static HashSet<string> Names(string? list) =>
        new((list ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
}

/// <summary>A level's chip on an editor log's filter: "Errors 2", shown or not.</summary>
public sealed partial class LogLevelChip(LogVerbosity verbosity, string name, bool shown, Action<LogLevelChip> changed) : ObservableObject
{
    public LogVerbosity Verbosity { get; } = verbosity;

    /// <summary>"Errors", "Display", "Messages".</summary>
    public string Name { get; } = name;

    /// <summary>Its entries in the log, those Claudette no longer keeps included.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(HasAny), nameof(AccessibleName))]
    public partial int Count { get; set; }

    public bool HasAny => Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tip), nameof(AccessibleName))]
    public partial bool IsShown { get; set; } = shown;

    partial void OnIsShownChanged(bool value) => changed(this);

    public string CountText => Count.ToString("N0", CultureInfo.CurrentCulture);

    public bool IsError => Verbosity == LogVerbosity.Error;

    public bool IsWarning => Verbosity == LogVerbosity.Warning;

    public string Tip => IsShown ? $"Showing {Name}: click to hide them" : $"{Name} hidden: click to show them";

    public string AccessibleName => $"{Name}, {CountText}, {(IsShown ? "shown" : "hidden")}";
}

/// <summary>One of Unreal's log categories in the Categories menu: "LogNet 1,203", shown or not.</summary>
public sealed partial class LogCategoryChoice(string name, bool shown, Action<LogCategoryChoice> changed) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    public partial int Count { get; set; }

    [ObservableProperty]
    public partial bool IsShown { get; set; } = shown;

    partial void OnIsShownChanged(bool value) => changed(this);

    public string CountText => Count.ToString("N0", CultureInfo.CurrentCulture);
}
