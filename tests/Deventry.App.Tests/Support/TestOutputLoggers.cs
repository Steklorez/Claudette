using Microsoft.Extensions.Logging;

namespace Deventry.App.Tests.Support;

/// <summary>
/// The app's log, from debug up, in the output of the test it's written during, which a test's result keeps: a test that
/// fails has beside it what went wrong underneath, such as what git said when it refused (<c>Core.Git.GitWorkingTree</c>).
/// </summary>
internal sealed class TestOutputLoggers : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new Logger(categoryName);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class Logger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            var line = $"[{logLevel}] {category}: {formatter(state, exception)}";
            try
            {
                TestContext.Current.TestOutputHelper?.WriteLine(exception is null ? line : $"{line}{Environment.NewLine}{exception}");
            }
            catch (InvalidOperationException)
            {
                // Written after its test ended, from work it left running: no test to give it to.
            }
        }
    }
}
