namespace Claudette.App.Games;

/// <summary>
/// Claudette's tic-tac-toe (DESIGN.md §18, "Games with Claudette"). On every level she takes a win in front of her.
/// Easy otherwise plays anywhere; Normal blocks the user's wins and plays her best move half the time; Hard, called
/// Unbeatable, always plays her best move, which never loses.
/// </summary>
public static class TicTacToeOpponent
{
    public static int Choose(TicTacToe board, GameLevel level, Random random)
    {
        var free = board.Free.ToArray();
        if (free.Length == 0 || board.IsOver)
        {
            throw new InvalidOperationException("There's no move to make.");
        }
        if (board.WinningMove(Player.Claudette) is { } win)
        {
            return win;
        }
        switch (level)
        {
            case GameLevel.Easy:
                return free[random.Next(free.Length)];
            case GameLevel.Normal:
                if (board.WinningMove(Player.You) is { } block)
                {
                    return block;
                }
                if (random.Next(2) == 0)
                {
                    return free[random.Next(free.Length)];
                }
                break;
        }
        return Best(board, random);
    }

    /// <summary>
    /// Her best square by minimax, one of the equally good ones at random so no two games go the same way. A win sooner,
    /// or a loss later, counts for more.
    /// </summary>
    public static int Best(TicTacToe board, Random random)
    {
        var choices = BestMoves(board);
        return choices[random.Next(choices.Count)];
    }

    /// <summary>Every square as good as her best by minimax.</summary>
    public static IReadOnlyList<int> BestMoves(TicTacToe board)
    {
        var free = board.Free.ToArray();
        if (free.Length == TicTacToe.Size)
        {
            // Every first move draws with best play.
            return free;
        }
        var best = int.MinValue;
        var choices = new List<int>();
        foreach (var square in free)
        {
            board.Set(square, Player.Claudette);
            var score = -Score(board, Player.You, 1);
            board.Set(square, Player.None);
            if (score > best)
            {
                best = score;
                choices.Clear();
            }
            if (score == best)
            {
                choices.Add(square);
            }
        }
        return choices;
    }

    /// <summary>How the board stands for <paramref name="toMove"/>, with best play from here.</summary>
    private static int Score(TicTacToe board, Player toMove, int depth)
    {
        var winner = board.Winner;
        if (winner != Player.None)
        {
            return winner == toMove ? 10 - depth : depth - 10;
        }
        if (board.IsFull)
        {
            return 0;
        }
        var best = int.MinValue;
        for (var square = 0; square < TicTacToe.Size; square++)
        {
            if (board[square] != Player.None)
            {
                continue;
            }
            board.Set(square, toMove);
            best = Math.Max(best, -Score(board, toMove.Other(), depth + 1));
            board.Set(square, Player.None);
        }
        return best;
    }
}
