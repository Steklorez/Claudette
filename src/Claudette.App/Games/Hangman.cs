namespace Claudette.App.Games;

/// <summary>Where a hangman word comes from, which Claudette says.</summary>
public enum HangmanCategory
{
    Programming,
    Everyday,
}

/// <summary>A word for hangman, in capitals A to Z.</summary>
public sealed record HangmanWord(string Word, HangmanCategory Category);

/// <summary>
/// A round of hangman (DESIGN.md §18, "Games with Claudette"): Claudette's word, and the letters the user has guessed.
/// Six misses lose.
/// </summary>
public sealed class Hangman(HangmanWord word)
{
    public const int MaxMisses = 6;

    private readonly HashSet<char> _guessed = [];

    public string Word { get; } = word.Word.ToUpperInvariant();

    public HangmanCategory Category { get; } = word.Category;

    public IReadOnlyCollection<char> Guessed => _guessed;

    public int Misses => _guessed.Count(letter => !Word.Contains(letter));

    public int MissesLeft => MaxMisses - Misses;

    public bool IsSolved => Word.All(_guessed.Contains);

    public bool IsLost => Misses >= MaxMisses;

    public bool IsOver => IsSolved || IsLost;

    public GameOutcome Outcome => IsSolved ? GameOutcome.Won : IsLost ? GameOutcome.Lost : GameOutcome.Playing;

    public bool HasGuessed(char letter) => _guessed.Contains(char.ToUpperInvariant(letter));

    /// <summary>
    /// Guesses <paramref name="letter"/>: how many times it's in the word, or null when it can't be guessed (it isn't a
    /// letter from A to Z, it's been guessed, or the round is over).
    /// </summary>
    public int? Guess(char letter)
    {
        letter = char.ToUpperInvariant(letter);
        if (!char.IsAsciiLetterUpper(letter) || IsOver || !_guessed.Add(letter))
        {
            return null;
        }
        return Word.Count(c => c == letter);
    }

    /// <summary>The word with the letters not yet guessed as blanks, such as <c>C_MP_L_R</c>.</summary>
    public string Pattern => new([.. Word.Select(c => _guessed.Contains(c) ? c : '_')]);
}
