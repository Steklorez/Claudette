using Claudette.App.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>Tic-tac-toe against Claudette (DESIGN.md §18, "Games with Claudette"): the user is ✕, she's ○.</summary>
public sealed class TicTacToeViewModel : GameViewModel
{
    private static readonly string[] SquareNames =
        ["top left", "top middle", "top right", "middle left", "middle", "middle right", "bottom left", "bottom middle", "bottom right"];

    private TicTacToe _board = new();

    public TicTacToeViewModel(IGameHost host) : base(host, GameKind.TicTacToe)
    {
        Squares = [.. Enumerable.Range(0, TicTacToe.Size).Select(i => new TicTacToeSquare(this, i))];
        NewRound();
    }

    public override string Title => "Tic-tac-toe";

    public override IReadOnlyList<GameLevelChoice> Levels { get; } =
        [new(GameLevel.Easy, "Easy"), new(GameLevel.Normal, "Normal"), new(GameLevel.Hard, "Unbeatable")];

    /// <summary>The nine squares, row by row from the top left.</summary>
    public IReadOnlyList<TicTacToeSquare> Squares { get; }

    /// <summary>"the top left", for her moves and the squares' names.</summary>
    public static string SquareName(int square) => SquareNames[square];

    protected override void StartRound(bool youStart)
    {
        _board = new TicTacToe();
        Refresh();
        if (youStart)
        {
            Status = "Your move. You're ✕.";
        }
        else
        {
            HerTurn(HerMove);
        }
    }

    /// <summary>The user takes <paramref name="square"/>, unless it's taken, it's her turn or the round is over.</summary>
    public void Play(int square)
    {
        if (IsHerTurn || !_board.CanPlay(square))
        {
            return;
        }
        _board.Play(square, Player.You);
        Host.Moved();
        Refresh();
        if (!Ended())
        {
            HerTurn(HerMove);
        }
    }

    public override bool Press(char key)
    {
        if (key is < '1' or > '9')
        {
            return false;
        }
        Play(key - '1');
        return true;
    }

    private void HerMove()
    {
        var threat = _board.WinningMove(Player.You);
        var square = TicTacToeOpponent.Choose(_board, CurrentLevel, Host.Random);
        _board.Play(square, Player.Claudette);
        Refresh();
        Host.Announce($"Claudette took the {SquareName(square)}.");
        if (Ended())
        {
            return;
        }
        if (threat == square)
        {
            Host.CloseCall();
        }
        Status = "Your move.";
    }

    /// <summary>Ends the round if someone won or the board is full.</summary>
    private bool Ended()
    {
        switch (_board.Outcome)
        {
            case GameOutcome.Won:
                Finish(GameOutcome.Won, "You win!");
                return true;
            case GameOutcome.Lost:
                Finish(GameOutcome.Lost, "Claudette wins!");
                return true;
            case GameOutcome.Draw:
                Finish(GameOutcome.Draw, "A draw.");
                return true;
            default:
                return false;
        }
    }

    private void Refresh()
    {
        var line = _board.WinningLine ?? [];
        foreach (var square in Squares)
        {
            square.Owner = _board[square.Index];
            square.IsWinning = line.Contains(square.Index);
        }
    }
}

/// <summary>A square of the tic-tac-toe board.</summary>
public sealed partial class TicTacToeSquare(TicTacToeViewModel game, int index) : ObservableObject
{
    public int Index => index;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYours), nameof(IsHers), nameof(Name))]
    public partial Player Owner { get; internal set; }

    /// <summary>One of the three in a row that won.</summary>
    [ObservableProperty]
    public partial bool IsWinning { get; internal set; }

    public bool IsYours => Owner == Player.You;

    public bool IsHers => Owner == Player.Claudette;

    /// <summary>"Top left, empty", for screen readers.</summary>
    public string Name
    {
        get
        {
            var where = TicTacToeViewModel.SquareName(index);
            var whose = Owner switch
            {
                Player.You => "yours",
                Player.Claudette => "Claudette's",
                _ => "empty",
            };
            return $"{char.ToUpperInvariant(where[0])}{where[1..]}, {whose}";
        }
    }

    [RelayCommand]
    private void Play() => game.Play(index);
}
