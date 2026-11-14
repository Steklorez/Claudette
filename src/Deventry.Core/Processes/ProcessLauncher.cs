using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;

namespace Deventry.Core.Processes;

/// <summary>Starts real processes with redirected, UTF-8, line-based standard streams.</summary>
/// <param name="timeProvider">The clock for <paramref name="outputDrainTime"/>. Null: the system clock.</param>
/// <param name="outputDrainTime">
/// How long a process's output is still read after it exits. What the process itself wrote is already in the pipes, so
/// this is quick; a process it started that inherited them (a build server, a command left in the background) can
/// keep them open much longer, and isn't waited for. Null: two seconds.
/// </param>
public sealed class ProcessLauncher(TimeProvider? timeProvider = null, TimeSpan? outputDrainTime = null) : IProcessLauncher
{
    public static readonly TimeSpan DefaultOutputDrainTime = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly TimeSpan _drain = outputDrainTime ?? DefaultOutputDrainTime;

    public IRunningProcess Start(ProcessStartSpec spec)
    {
        var redirect = !spec.Detached;
        var startInfo = new ProcessStartInfo(spec.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = redirect,
            RedirectStandardInput = redirect,
            RedirectStandardOutput = redirect,
            RedirectStandardError = redirect,
        };
        if (redirect)
        {
            startInfo.StandardInputEncoding = new UTF8Encoding(false);
            startInfo.StandardOutputEncoding = Encoding.UTF8;
            startInfo.StandardErrorEncoding = Encoding.UTF8;
        }
        if (spec.CommandLine is { } commandLine)
        {
            startInfo.Arguments = commandLine;
        }
        else
        {
            foreach (var argument in spec.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }
        if (spec.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = spec.WorkingDirectory;
        }
        if (spec.Environment is not null)
        {
            startInfo.Environment.Clear();
            foreach (var (key, value) in spec.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

#pragma warning disable RS0030 // This is IProcessLauncher: every process starts here.
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{spec.FileName}'.");
#pragma warning restore RS0030
        return new RunningProcess(process, redirect, _time, _drain);
    }

    private sealed class RunningProcess : IRunningProcess
    {
        private readonly Process _process;
        private readonly Channel<string> _stdout = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true });
        private readonly Channel<string> _stderr = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleWriter = true });
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly Task<int> _exited;

        /// <summary>The input is to be closed, once no line is being written.</summary>
        private bool _closeAsked;

        private readonly bool _redirected;
        private readonly TimeProvider _time;
        private readonly TimeSpan _drain;

        public RunningProcess(Process process, bool redirected, TimeProvider time, TimeSpan drain)
        {
            _process = process;
            _redirected = redirected;
            _time = time;
            _drain = drain;
            if (!redirected)
            {
                _stdout.Writer.TryComplete();
                _stderr.Writer.TryComplete();
                _exited = WaitForExitAsync(Task.CompletedTask, Task.CompletedTask);
                return;
            }
            _process.StandardInput.NewLine = "\n";
            _process.StandardInput.AutoFlush = false;
            var stdoutPump = PumpAsync(process.StandardOutput, _stdout.Writer);
            var stderrPump = PumpAsync(process.StandardError, _stderr.Writer);
            _exited = WaitForExitAsync(stdoutPump, stderrPump);
        }

        public int Id => _process.Id;

        public ChannelReader<string> StandardOutput => _stdout.Reader;

        public ChannelReader<string> StandardError => _stderr.Reader;

        public Task<int> Exited => _exited;

        public async ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
                // Asked to close while this line was being written: closed now it's done.
                CloseIfAsked();
            }
        }

        /// <summary>
        /// Closes the input, ending it for the process. A line being written goes whole first: closing under it throws
        /// (<c>StreamWriter</c> refuses while it's writing), and would cut the line off. Its write closes the input once
        /// it's done, or fails as the process ends.
        /// </summary>
        public void CloseStandardInput()
        {
            if (!_redirected)
            {
                return;
            }
            Volatile.Write(ref _closeAsked, true);
            CloseIfAsked();
        }

        /// <summary>
        /// Closes the input if it's been asked for and no line is being written: by whoever gets the lock, the closer or the
        /// write that held it, which looks again after letting it go, so neither misses the other.
        /// </summary>
        private void CloseIfAsked()
        {
            try
            {
                if (!Volatile.Read(ref _closeAsked) || !_writeLock.Wait(0))
                {
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                // Disposed: the input went with it.
                return;
            }
            try
            {
                _process.StandardInput.Close();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The process already exited and closed the pipe, or was disposed.
            }
            finally
            {
                _writeLock.Release();
            }
        }

        public void Kill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or AggregateException or Win32Exception or NotSupportedException)
            {
                // Already exited; or it, or a process it started, isn't ours to end (the rest are ended regardless). Its
                // callers are closing a tab or the app, which mustn't stop halfway over it.
            }
        }

        public async ValueTask DisposeAsync()
        {
            CloseStandardInput();
            Kill();
            try
            {
                await _exited.ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Nothing left to wait for.
            }
            _process.Dispose();
            _writeLock.Dispose();
        }

        private static async Task PumpAsync(StreamReader reader, ChannelWriter<string> writer)
        {
            try
            {
                while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    writer.TryWrite(line);
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                // The pipe broke because the process ended, or the process was disposed while a descendant kept it open.
            }
            finally
            {
                writer.TryComplete();
            }
        }

        /// <summary>
        /// Completes when the process exits, once its output has been read. A descendant that inherited the pipes can keep
        /// them open long after; their end isn't waited for beyond the drain time, and both streams end then.
        /// </summary>
        private async Task<int> WaitForExitAsync(Task stdoutPump, Task stderrPump)
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            try
            {
                await Task.WhenAll(stdoutPump, stderrPump).WaitAsync(_drain, _time).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _stdout.Writer.TryComplete();
                _stderr.Writer.TryComplete();
            }
            return _process.ExitCode;
        }
    }
}
