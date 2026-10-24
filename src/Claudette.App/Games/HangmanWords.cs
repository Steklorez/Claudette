namespace Claudette.App.Games;

/// <summary>
/// The words Claudette thinks of for hangman (DESIGN.md §18, "Games with Claudette"): three times in four one from
/// programming, otherwise an everyday one. Letters A to Z only, spelled the same in British and American English, and
/// no product names.
/// </summary>
public static class HangmanWords
{
    /// <summary>A word doesn't come up again until this many others have.</summary>
    public const int Recent = 20;

    public static readonly IReadOnlyList<string> Programming =
    [
        "ALGORITHM", "ALLOCATOR", "ARGUMENT", "ARRAY", "ASSEMBLY", "ASSERTION", "ASYNC", "ATTRIBUTE", "BACKEND", "BACKTRACE",
        "BANDWIDTH", "BENCHMARK", "BINARY", "BITMASK", "BOOLEAN", "BOOTSTRAP", "BRANCH", "BREAKPOINT", "BROWSER", "BUFFER",
        "BYTECODE", "CACHE", "CALLBACK", "CHECKOUT", "CHECKSUM", "CLASS", "CLOSURE", "CODEBASE", "COMMIT", "COMPILER",
        "CONCURRENCY", "CONSTANT", "CONSTRUCTOR", "CONTAINER", "CONTEXT", "COROUTINE", "CURSOR", "DAEMON", "DATABASE", "DEADLOCK",
        "DEBUGGER", "DECORATOR", "DELEGATE", "DEPENDENCY", "DEPLOYMENT", "DESTRUCTOR", "DICTIONARY", "DIRECTORY", "ENCODING", "ENCRYPTION",
        "ENDPOINT", "EXCEPTION", "EXECUTABLE", "EXPRESSION", "FIRMWARE", "FRAMEWORK", "FRONTEND", "FUNCTION", "GENERATOR", "GENERIC",
        "HANDLER", "HASHING", "HEADER", "HOTFIX", "IDENTIFIER", "IMMUTABLE", "INDEXING", "INHERITANCE", "INSTANCE", "INTEGER",
        "INTERFACE", "INTERPRETER", "ITERATOR", "KERNEL", "KEYWORD", "LAMBDA", "LATENCY", "LIBRARY", "LINKER", "LINTER",
        "LOCALHOST", "LOGGER", "MAKEFILE", "MEMORY", "MERGE", "METHOD", "MIDDLEWARE", "MIGRATION", "MODULE", "MONOREPO",
        "MUTEX", "NAMESPACE", "NULLABLE", "OBJECT", "OPERATOR", "OVERFLOW", "OVERLOAD", "PACKAGE", "PARAMETER", "PARSER",
        "PAYLOAD", "PIPELINE", "PLUGIN", "POINTER", "POLYMORPHISM", "PROCESSOR", "PROFILER", "PROMISE", "PROMPT", "PROPERTY",
        "PROTOCOL", "PROTOTYPE", "QUERY", "QUEUE", "RECURSION", "REFACTOR", "REFERENCE", "REGEX", "REGISTER", "REGRESSION",
        "RELEASE", "REPOSITORY", "RESOLVER", "ROLLBACK", "ROUTER", "RUNTIME", "SANDBOX", "SCHEDULER", "SCHEMA", "SCRIPT",
        "SEMAPHORE", "SERVER", "SINGLETON", "SNAPSHOT", "SOCKET", "STRING", "STRUCT", "SUBAGENT", "SUBCLASS", "SUBMODULE",
        "SUBROUTINE", "SYNTAX", "TEMPLATE", "TERMINAL", "THREAD", "THROUGHPUT", "TIMEOUT", "TIMESTAMP", "TOKEN", "TOOLCHAIN",
        "TRANSACTION", "TRANSPILER", "TUPLE", "VARIABLE", "VECTOR", "VERSION", "VIRTUAL", "WEBHOOK", "WEBSOCKET", "WIDGET",
        "WILDCARD", "WORKFLOW", "WORKTREE", "YIELD",
    ];

    public static readonly IReadOnlyList<string> Everyday =
    [
        "BALLOON", "BANANA", "BICYCLE", "BLANKET", "BUTTERFLY", "CACTUS", "CANDLE", "CARNIVAL", "CASTLE", "CHOCOLATE",
        "COMPASS", "COOKIE", "CRAYON", "CROCODILE", "CUPCAKE", "DIAMOND", "DINOSAUR", "DOLPHIN", "DRAGON", "ELEPHANT",
        "FEATHER", "FIREWORK", "FLAMINGO", "GARDEN", "GIRAFFE", "GUITAR", "HAMMOCK", "HEDGEHOG", "HONEYCOMB", "ISLAND",
        "JELLYFISH", "JIGSAW", "KANGAROO", "KETTLE", "LANTERN", "LEMONADE", "LIGHTHOUSE", "MAGNET", "MARSHMALLOW", "MOUNTAIN",
        "MUFFIN", "NOTEBOOK", "OCTOPUS", "ORCHESTRA", "PANCAKE", "PENGUIN", "PICNIC", "PILLOW", "PINEAPPLE", "PUMPKIN",
        "PUZZLE", "RAINBOW", "ROCKET", "SANDCASTLE", "SANDWICH", "SCARECROW", "SNOWFLAKE", "SNOWMAN", "SPAGHETTI", "SQUIRREL",
        "SUNFLOWER", "TELESCOPE", "THUNDER", "TORNADO", "TREASURE", "TRUMPET", "UMBRELLA", "VOLCANO", "WAFFLE", "WATERFALL",
        "WHISTLE", "WINDMILL", "ZEBRA",
    ];

    /// <summary>A word for the next round, none of <paramref name="recent"/>.</summary>
    public static HangmanWord Pick(Random random, IReadOnlyCollection<string> recent)
    {
        var category = random.Next(4) < 3 ? HangmanCategory.Programming : HangmanCategory.Everyday;
        var words = category == HangmanCategory.Programming ? Programming : Everyday;
        var fresh = words.Where(w => !recent.Contains(w)).ToArray();
        var from = fresh.Length > 0 ? fresh : [.. words];
        return new HangmanWord(from[random.Next(from.Length)], category);
    }
}
