using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Claudette.App.Services;

namespace Claudette.App.Views;

/// <summary>
/// The Games page (DESIGN.md §18, "Games with Claudette"): what it needs beyond bindings, which is the keyboard. While
/// the page has focus, the number keys play tic-tac-toe's squares and Connect Four's columns and the letters guess in
/// hangman. It takes focus when a game is started, so the keys reach it rather than the composer.
/// </summary>
public partial class GamesView : UserControl
{
    private GameService? _games;

    public GamesView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_games is not null)
        {
            _games.PropertyChanged -= OnGamesChanged;
        }
        _games = DataContext as GameService;
        if (_games is not null && this.IsAttachedToVisualTree())
        {
            _games.PropertyChanged += OnGamesChanged;
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_games is not null)
        {
            _games.PropertyChanged -= OnGamesChanged;
            _games.PropertyChanged += OnGamesChanged;
        }
        FocusIfStarted();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_games is not null)
        {
            _games.PropertyChanged -= OnGamesChanged;
        }
    }

    private void OnGamesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GameService.Game))
        {
            FocusIfStarted();
        }
    }

    /// <summary>A game was just started: the page takes the keyboard, once whatever started it (a menu) has let go.</summary>
    private void FocusIfStarted()
    {
        if (_games is { IsOpen: true } games && IsEffectivelyVisible && games.TakeFocusRequest())
        {
            Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Background);
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || (e.KeyModifiers & ~KeyModifiers.Shift) != KeyModifiers.None || _games?.Game is not { } game
            // The level's list has its own keys.
            || (e.Source as Visual)?.FindAncestorOfType<ComboBox>(includeSelf: true) is not null)
        {
            return;
        }
        var key = e.Key switch
        {
            >= Key.A and <= Key.Z => (char)('A' + (e.Key - Key.A)),
            >= Key.D1 and <= Key.D9 => (char)('1' + (e.Key - Key.D1)),
            >= Key.NumPad1 and <= Key.NumPad9 => (char)('1' + (e.Key - Key.NumPad1)),
            _ => '\0',
        };
        if (key != '\0' && game.Press(key))
        {
            e.Handled = true;
        }
    }
}
