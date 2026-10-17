using System.Threading.Channels;
using Claudette.Core.Processes;

namespace Claudette.App.Tests.Support;

/// <summary>Records what would be started, and hands back processes the test controls.</summary>
internal sealed class FakeLauncher : IProcessLauncher
{
    // Processes start off the test's thread too: both lists change as one step, and are read as copies.
    private readonly Lock _lock = new();
    private readonly List<ProcessStartSpec> _started = [];
    private readonly List<FakeProcess> _processes = [];

    public IReadOnlyList<ProcessStartSpec> Started
    {
        get
        {
            lock (_lock)
            {
                return [.. _started];
            }
        }
    }

    public IReadOnlyList<FakeProcess> Processes
    {
        get
        {
            lock (_lock)
            {
                return [.. _processes];
            }
        }
    }

    /// <summary>The process started with <paramref name="spec"/>.</summary>
    public FakeProcess ProcessFor(ProcessStartSpec spec)
    {
        lock (_lock)
        {
            return _processes[_started.IndexOf(spec)];
        }
    }

    /// <summary>Called as each process starts, for example to play a new build saying it's up.</summary>
    public Action<ProcessStartSpec, FakeProcess>? OnStart { get; set; }

    public IRunningProcess Start(ProcessStartSpec spec)
    {
        var process = new FakeProcess();
        lock (_lock)
        {
            _started.Add(spec);
            _processes.Add(process);
        }
        OnStart?.Invoke(spec, process);
        return process;
    }
}

internal sealed class FakeProcess : IRunningProcess
{
    private readonly Channel<string> _stdout = Channel.CreateUnbounded<string>();
    private readonly Channel<string> _stderr = Channel.CreateUnbounded<string>();
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int Id => 4321;

    public bool Killed { get; private set; }

    public bool Disposed { get; private set; }

    public ChannelReader<string> StandardOutput => _stdout.Reader;

    public ChannelReader<string> StandardError => _stderr.Reader;

    public Task<int> Exited => _exited.Task;

    public void WriteError(string line) => _stderr.Writer.TryWrite(line);

    public void WriteOutput(string line) => _stdout.Writer.TryWrite(line);

    /// <summary>Lines written to standard input.</summary>
    public List<string> Input { get; } = [];

    public void Exit(int code)
    {
        _stdout.Writer.TryComplete();
        _stderr.Writer.TryComplete();
        _exited.TrySetResult(code);
    }

    public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        lock (Input)
        {
            Input.Add(line);
        }
        return ValueTask.CompletedTask;
    }

    public void CloseStandardInput()
    {
    }

    public void Kill()
    {
        Killed = true;
        Exit(-1);
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}
