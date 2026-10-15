using Claudette.App.Games;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Claudette.App.ViewModels;

/// <summary>Connect Four against Claudette (DESIGN.md §18, "Games with Claudette"): the user's pieces are blue, hers berry.</summary>
public sealed class ConnectFourViewModel : GameViewModel
{
    private ConnectFour _board = new();

    public ConnectFourViewModel(IGameHost host) : base(host, GameKind.ConnectFour)
    {
        Columns = [.. Enumerable.Range(0, ConnectFour.Columns).Select(c => new ConnectFourColumn(this, c))];
        NewRound();
    }

    public override string Title => "Connect Four";

    public override IReadOnlyList<GameLevelChoice> Levels { get; } =
        [new(GameLevel.Easy, "Easy"), new(GameLevel.Normal, "Normal"), new(GameLevel.Hard, "Hard")];

    /// <summary>The seven columns, left to right.</summary>
    public IReadOnlyList<ConnectFourColumn> Columns { get; }

    protected override void StartRound(bool youStart)
    {
        _board = new ConnectFour();
        Refresh();
        if (youStart)
        {
            Status = "Your move: pick a column.";
        }
        else
        {
            HerTurn(HerMove);
        }
    }

    /// <summary>The user drops a piece in <paramref name="column"/>, unless it's full, it's her turn or the round is over.</summary>
    public void Drop(int column)
    {
        if (IsHerTurn || !_board.CanDrop(column))
        {
            return;
        }
        _board.Drop(column, Player.You);
        Host.Moved();
        Refresh();
        if (!Ended())
        {
            HerTurn(HerMove);
        }
    }

    public override bool Press(char key)
    {
        if (key is < '1' or > '7')
        {
            return false;
        }
        Drop(key - '1');
        return true;
    }

    private void HerMove()
    {
        var threat = _board.WinningMove(Player.You);
        var column = ConnectFourOpponent.Choose(_board, CurrentLevel, Host.Random);
        _board.Drop(column, Player.Claudette);
        Refresh();
        Host.Announce($"Claudette dropped a piece in column {column + 1}.");
        if (Ended())
        {
            return;
        }
        if (threat == column)
        {
            Host.CloseCall();
        }
        Status = "Your move.";
    }

    private bool Ended()
    {
        switch (_board.Outcome)
        {
            case GameOutcome.Won:
                Finish(GameOutcome.Won, "Four in a row: you win!");
                return true;
            case GameOutcome.Lost:
                Finish(GameOutcome.Lost, "Four in a row: Claudette wins!");
                return true;
            case GameOutcome.Draw:
                Finish(GameOutcome.Draw, "The board's full: a draw.");
                return true;
            default:
                return false;
        }
    }

    private void Refresh()
    {
        var winning = _board.WinningCells;
        foreach (var column in Columns)
        {
            column.Refresh(_board, winning);
        }
    }
}

/// <summary>A column of the Connect Four board: a click drops a piece in it.</summary>
public sealed partial class ConnectFourColumn : ObservableObject
{
    private readonly ConnectFourViewModel _game;

    internal ConnectFourColumn(ConnectFourViewModel game, int index)
    {
        _game = game;
        Index = index;
        Cells = [.. Enumerable.Range(0, ConnectFour.Rows).Select(_ => new ConnectFourCell())];
    }

    public int Index { get; }

    /// <summary>Its cells from the top down, as they show.</summary>
    public IReadOnlyList<ConnectFourCell> Cells { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    public partial int Filled { get; private set; }

    /// <summary>"Column 3, 2 of 6 filled", for screen readers.</summary>
    public string Name => Filled == ConnectFour.Rows ? $"Column {Index + 1}, full" : $"Column {Index + 1}, {Filled} of {ConnectFour.Rows} filled";

    [RelayCommand]
    private void Drop() => _game.Drop(Index);

    internal void Refresh(ConnectFour board, IReadOnlyList<(int Column, int Row)> winning)
    {
        Filled = board.Height(Index);
        for (var row = 0; row < ConnectFour.Rows; row++)
        {
            var cell = Cells[ConnectFour.Rows - 1 - row];
            cell.Owner = board.At(Index, row);
            cell.IsWinning = winning.Contains((Index, row));
            cell.IsLast = board.LastMove == (Index, row);
        }
    }
}

/// <summary>A place for a piece on the Connect Four board.</summary>
public sealed partial class ConnectFourCell : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsYours), nameof(IsHers))]
    public partial Player Owner { get; internal set; }

    /// <summary>One of the pieces in a row that won: it's ringed.</summary>
    [ObservableProperty]
    public partial bool IsWinning { get; internal set; }

    /// <summary>The piece dropped last.</summary>
    [ObservableProperty]
    public partial bool IsLast { get; internal set; }

    public bool IsYours => Owner == Player.You;

    public bool IsHers => Owner == Player.Claudette;
}
