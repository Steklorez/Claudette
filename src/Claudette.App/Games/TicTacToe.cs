namespace Claudette.App.Games;

/// <summary>Tic-tac-toe's board (DESIGN.md §18, "Games with Claudette"): nine squares, row by row from the top left.</summary>
public sealed class TicTacToe
{
    public const int Size = 9;

    /// <summary>The eight lines of three: the rows, the columns, then the diagonals.</summary>
    public static readonly IReadOnlyList<int[]> Lines =
        [[0, 1, 2], [3, 4, 5], [6, 7, 8], [0, 3, 6], [1, 4, 7], [2, 5, 8], [0, 4, 8], [2, 4, 6]];

    private readonly Player[] _squares = new Player[Size];

    public Player this[int square] => _squares[square];

    /// <summary>The line of three that won, or null.</summary>
    public int[]? WinningLine
    {
        get
        {
            foreach (var line in Lines)
            {
                var first = _squares[line[0]];
                if (first != Player.None && _squares[line[1]] == first && _squares[line[2]] == first)
                {
                    return line;
                }
            }
            return null;
        }
    }

    public Player Winner => WinningLine is { } line ? _squares[line[0]] : Player.None;

    public bool IsFull => Array.IndexOf(_squares, Player.None) < 0;

    public bool IsOver => Winner != Player.None || IsFull;

    public GameOutcome Outcome => Winner switch
    {
        Player.You => GameOutcome.Won,
        Player.Claudette => GameOutcome.Lost,
        _ => IsFull ? GameOutcome.Draw : GameOutcome.Playing,
    };

    /// <summary>The squares nobody has taken.</summary>
    public IEnumerable<int> Free
    {
        get
        {
            for (var square = 0; square < Size; square++)
            {
                if (_squares[square] == Player.None)
                {
                    yield return square;
                }
            }
        }
    }

    public bool CanPlay(int square) => square is >= 0 and < Size && _squares[square] == Player.None && !IsOver;

    public void Play(int square, Player player)
    {
        if (!CanPlay(square) || player == Player.None)
        {
            throw new InvalidOperationException($"Square {square} can't be played.");
        }
        _squares[square] = player;
    }

    /// <summary>A square that wins at once for <paramref name="player"/>, or null.</summary>
    public int? WinningMove(Player player)
    {
        if (IsOver)
        {
            return null;
        }
        foreach (var square in Free.ToArray())
        {
            _squares[square] = player;
            var wins = Winner == player;
            _squares[square] = Player.None;
            if (wins)
            {
                return square;
            }
        }
        return null;
    }

    /// <summary>Takes or clears a square without the rules' checks, for looking ahead.</summary>
    internal void Set(int square, Player player) => _squares[square] = player;
}
