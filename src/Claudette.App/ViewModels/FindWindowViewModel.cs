using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>
/// The Find tool window (DESIGN.md §18, "Finding in files", "The Find window"): the searches kept by Find in files'
/// <b>Open in Find window</b>, each a tab in the window's header, as Rider's Find window has them. Its window is on the
/// strip while it has a search; closing a tab stops its search for good.
/// </summary>
public sealed partial class FindWindowViewModel : ObservableObject, IDisposable
{
    /// <summary>The searches kept, in the order they were kept.</summary>
    public ObservableCollection<FindResultsViewModel> Searches { get; } = [];

    /// <summary>The search whose tab shows.</summary>
    [ObservableProperty]
    public partial FindResultsViewModel? Selected { get; set; }

    /// <summary>It has a search: its window is on the strip.</summary>
    [ObservableProperty]
    public partial bool HasSearches { get; private set; }

    /// <summary>A search kept: a tab of its own, after the others, which shows.</summary>
    public void Add(FindResultsViewModel results)
    {
        Searches.Add(results);
        Selected = results;
        HasSearches = true;
    }

    /// <summary>A search's tab clicked: it shows.</summary>
    [RelayCommand]
    private void Select(FindResultsViewModel? results)
    {
        if (results is not null && Searches.Contains(results))
        {
            Selected = results;
        }
    }

    /// <summary>A search's tab's ×, or a middle click on it: the tab goes, the one beside it shows, and its search stops.</summary>
    [RelayCommand]
    private void Close(FindResultsViewModel? results)
    {
        if (results is null || Searches.IndexOf(results) is not (>= 0 and var at))
        {
            return;
        }
        Searches.RemoveAt(at);
        if (ReferenceEquals(Selected, results))
        {
            Selected = Searches.ElementAtOrDefault(at) ?? Searches.LastOrDefault();
        }
        HasSearches = Searches.Count > 0;
        results.Dispose();
    }

    public void Dispose()
    {
        foreach (var results in Searches)
        {
            results.Dispose();
        }
        Searches.Clear();
        Selected = null;
        HasSearches = false;
    }
}
