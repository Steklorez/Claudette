namespace Claudette.App.ViewModels.ToolWindows;

/// <summary>
/// An auto-hidden window sliding out over the conversation and back (DESIGN.md §3, "Tool windows", "Auto-hide"), as in
/// Visual Studio. Pointing at its tab slides it out after <see cref="SlideOutDelay"/>, and it slides back
/// <see cref="SlideBackDelay"/> after the pointer leaves, unless the keyboard focus is in it. A click on its tab, or a
/// button or menu showing it, slides it out at once and keeps it out until the pointer is pressed elsewhere, the focus
/// leaves it, or Esc. One window is out at a time. Timed by the injected clock.
/// </summary>
public sealed partial class ToolDockViewModel
{
    /// <summary>How long the pointer rests on a tab before its window slides out.</summary>
    public static readonly TimeSpan SlideOutDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>How long after the pointer leaves a window slid out by pointing that it slides back.</summary>
    public static readonly TimeSpan SlideBackDelay = TimeSpan.FromMilliseconds(400);

    private readonly UiTimeout _slideOutTimer;
    private readonly UiTimeout _slideBackTimer;

    /// <summary>The tab under the pointer, along an auto-hidden edge.</summary>
    private ToolWindowViewModel? _pointedAt;

    /// <summary>The window out stays out until the pointer is pressed elsewhere: it was clicked, or shown by a button.</summary>
    private bool _held;

    private bool _pointerInSlide;
    private bool _focusInSlide;

    /// <summary>The window slid out, at either edge; null while none is.</summary>
    public ToolWindowViewModel? SlidOut => Right.SlidOut ?? Bottom.SlidOut;

    /// <summary>A window slid out by a click on its tab: the view puts the keyboard focus in it.</summary>
    public event Action<ToolWindowViewModel>? FocusRequested;

    /// <summary>The pointer came onto a tab along an auto-hidden edge: its window slides out once it rests there.</summary>
    public void PointAtTab(ToolWindowViewModel window)
    {
        _pointedAt = window;
        _slideBackTimer.Cancel();
        if (ReferenceEquals(SlidOut, window))
        {
            _slideOutTimer.Cancel();
            return;
        }
        _slideOutTimer.Restart(SlideOutDelay, () => SlideOut(window, held: false));
    }

    /// <summary>The pointer left a tab: its window doesn't slide out, and one out by pointing slides back unless the pointer goes onto it.</summary>
    public void LeaveTab(ToolWindowViewModel window)
    {
        if (ReferenceEquals(_pointedAt, window))
        {
            _pointedAt = null;
        }
        _slideOutTimer.Cancel();
        SlideBackLater();
    }

    /// <summary>
    /// A click on a tab along an auto-hidden edge, or Enter on it: its window slides out at once and takes the keyboard
    /// focus; clicked again while it's out, it slides back.
    /// </summary>
    public void ClickTab(ToolWindowViewModel window)
    {
        CancelSlideTimers();
        if (ReferenceEquals(SlidOut, window) && _held)
        {
            SlideBack();
            return;
        }
        SlideOut(window, held: true);
        if (ReferenceEquals(SlidOut, window))
        {
            FocusRequested?.Invoke(window);
        }
    }

    /// <summary>The pointer came onto the window slid out, or left it.</summary>
    public void PointInSlide(bool inside)
    {
        _pointerInSlide = inside;
        if (inside)
        {
            _slideBackTimer.Cancel();
        }
        else
        {
            SlideBackLater();
        }
    }

    /// <summary>A press in the window slid out: it stays out until the pointer is pressed elsewhere, as a click on its tab keeps it.</summary>
    public void PressInSlide()
    {
        _held = true;
        _slideBackTimer.Cancel();
    }

    /// <summary>The keyboard focus came into the window slid out, or left it: leaving it, away from the pointer, slides it back.</summary>
    public void FocusInSlide(bool within)
    {
        var was = _focusInSlide;
        _focusInSlide = within;
        if (was && !within && !_pointerInSlide && SlidOut is not null)
        {
            SlideBack();
        }
    }

    /// <summary>The pointer pressed anywhere but the window slid out and the edges' tabs: it slides back.</summary>
    public void PressOutside()
    {
        if (SlidOut is not null)
        {
            SlideBack();
        }
    }

    /// <summary>Esc: the window slid out slides back. False when none was out.</summary>
    public bool Escape()
    {
        if (SlidOut is null)
        {
            return false;
        }
        SlideBack();
        return true;
    }

    /// <summary>The window slid out slides back at once.</summary>
    public void SlideBack()
    {
        CancelSlideTimers();
        if (SlidOut is null)
        {
            return;
        }
        EndSlide();
        Refresh();
    }

    /// <summary>A window slid out by pointing slides back once the pointer has been away for <see cref="SlideBackDelay"/>.</summary>
    private void SlideBackLater()
    {
        if (SlidOut is not { } slid || _held || _pointerInSlide || _focusInSlide || ReferenceEquals(_pointedAt, slid))
        {
            return;
        }
        _slideBackTimer.Restart(SlideBackDelay, () =>
        {
            if (!_held && !_pointerInSlide && !_focusInSlide && !ReferenceEquals(_pointedAt, SlidOut))
            {
                SlideBack();
            }
        });
    }

    /// <summary>
    /// <paramref name="window"/>, along an auto-hidden edge, slides out, in place of any other window out;
    /// <paramref name="held"/> keeps it out until the pointer is pressed elsewhere.
    /// </summary>
    private void SlideOut(ToolWindowViewModel window, bool held)
    {
        var edge = EdgeOf(window.Edge);
        if (!edge.IsAutoHide || !window.HasTab)
        {
            return;
        }
        _slideBackTimer.Cancel();
        if (!ReferenceEquals(SlidOut, window))
        {
            EndSlide();
        }
        _held |= held;
        edge.SlidOut = window;
        Refresh();
    }

    private void EndSlide()
    {
        Right.SlidOut = null;
        Bottom.SlidOut = null;
        _held = false;
        _pointerInSlide = false;
        _focusInSlide = false;
    }

    private void CancelSlideTimers()
    {
        _slideOutTimer.Cancel();
        _slideBackTimer.Cancel();
    }
}
