using CommunityToolkit.Mvvm.ComponentModel;
using Deventry.App.Mascot;
using Deventry.App.Services;

namespace Deventry.App.ViewModels;

/// <summary>
/// Joulene on the closing card: whether she stands still, with motion reduced, her hat for the day, and her size, the
/// screen pixels in a cell of her sprite at 100%, as on the composer.
/// </summary>
public sealed record ClosingJoulene(bool Still, string? Hat, double PixelsPerCell);

/// <summary>
/// What closing Deventry, or restarting it, is doing (DESIGN.md §13, "What closing does, on screen"): a card over the
/// dimmed window, once it has taken a moment, with its title, a bar that fills by step, the step it's on, and Joulene
/// waving goodbye. Every step is counted up front, so the bar never goes back.
/// </summary>
public sealed partial class ClosingViewModel : ViewModelBase, IDisposable
{
    /// <summary>How long closing goes on before the card shows: a quick close shows nothing.</summary>
    public static readonly TimeSpan ShowAfter = TimeSpan.FromMilliseconds(300);

    /// <summary>How long her arm stays up, and down, as she waves.</summary>
    public static readonly TimeSpan WaveStep = TimeSpan.FromMilliseconds(280);

    private const string Wave = "wave";
    private const string WaveHigh = "waveHigh";

    private readonly Lock _lock = new();
    private readonly UiTimeout _show;
    private readonly UiTicker _wave;
    private readonly ClosingJoulene? _joulene;
    private string? _tabs;
    private int _tabCount;
    private int _tabsDone;

    /// <param name="steps">Every step closing will take: the bar's length.</param>
    /// <param name="joulene">Joulene on the card, or null when she's turned off.</param>
    /// <param name="showAfter">How long before the card shows: <see cref="ShowAfter"/>, or none for a restart, which
    /// always takes a while and has already taken the tabs away.</param>
    public ClosingViewModel(TimeProvider time, IUiDispatcher dispatcher, string title, int steps, ClosingJoulene? joulene, TimeSpan showAfter)
    {
        Title = title;
        Total = Math.Max(1, steps);
        _joulene = joulene;
        Frame = joulene is null ? null : Pose(Wave);
        _wave = new UiTicker(time, dispatcher, WaveStep, () => Frame = Pose(Frame?.Pose == Wave ? WaveHigh : Wave));
        _show = new UiTimeout(time, dispatcher);
        if (showAfter <= TimeSpan.Zero)
        {
            IsShown = true;
        }
        else
        {
            _show.Restart(showAfter, () => IsShown = true);
        }
    }

    /// <summary><i>Closing Deventry…</i>, or a restart's own.</summary>
    public string Title { get; }

    /// <summary>The card shows: closing has taken longer than <see cref="ShowAfter"/>.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    /// <summary>What it's on now.</summary>
    [ObservableProperty]
    public partial string Step { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Bar), nameof(Progress))]
    public partial int Done { get; private set; }

    public int Total { get; }

    /// <summary>The bar, for a screen reader.</summary>
    public string Progress => $"{Done} of {Total} steps done";

    /// <summary>The bar: what's done in the meters' color, what's left on their track.</summary>
    public IReadOnlyList<ContextSegment> Bar =>
    [
        new ContextSegment("Done", Done, "MeterBrush", ""),
        new ContextSegment("To do", Total - Done, "MeterTrackBrush", ""),
    ];

    /// <summary>Joulene as she is now, waving; null when she's turned off.</summary>
    [ObservableProperty]
    public partial MascotFrame? Frame { get; private set; }

    /// <summary>Her size, as on the composer.</summary>
    public double PixelsPerCell => _joulene?.PixelsPerCell ?? 3;

    /// <summary>The remote clients connected now, by name, are telling their chats Deventry is closing.</summary>
    public void TellingBots(IReadOnlyList<string> names) =>
        Step = $"Telling {string.Join(" and ", names)} that Deventry is closing";

    /// <summary>The tabs, by name, are stopping, all at once.</summary>
    public void StoppingTabs(IReadOnlyList<string> names)
    {
        lock (_lock)
        {
            _tabCount = names.Count;
            _tabsDone = 0;
            _tabs = names.Count == 1 ? $"Stopping Claude in {names[0]}" : $"Stopping Claude in {names.Count} tabs";
            Step = _tabs;
        }
    }

    /// <summary>A tab has stopped: one more of the bar, and of the count after several.</summary>
    public void TabStopped()
    {
        lock (_lock)
        {
            _tabsDone++;
            if (_tabCount > 1)
            {
                Step = $"{_tabs} ({_tabsDone} of {_tabCount} done)";
            }
            Advance();
        }
    }

    /// <summary>A restart waits for its new copy: <i>Starting the new build</i>, or <i>Installing the update</i>.</summary>
    public void Waiting(string step) => Step = step;

    /// <summary>Saving what's left, or for a restart, whose files the new copy has, finishing up.</summary>
    public void Saving(bool restart) => Step = restart ? "Finishing up" : "Saving your tabs, drafts and scratch pads";

    /// <summary>A step is done. The bar never goes past its end.</summary>
    public void Advance()
    {
        lock (_lock)
        {
            Done = Math.Min(Total, Done + 1);
        }
    }

    partial void OnIsShownChanged(bool value) => _wave.Run(value && _joulene is { Still: false });

    private MascotFrame Pose(string pose) => new(pose, 0, 0, []) { Hat = _joulene?.Hat };

    public void Dispose()
    {
        _show.Dispose();
        _wave.Dispose();
    }
}
