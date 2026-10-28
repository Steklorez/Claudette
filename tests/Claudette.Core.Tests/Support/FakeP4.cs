using System.Globalization;
using System.Threading.Channels;
using Claudette.Core.Processes;

namespace Claudette.Testing;

/// <summary>
/// A pretend <c>p4</c> for Perforce ticket handling tests (DESIGN.md §18): answers <c>-ztag info</c>, <c>set -q</c>,
/// <c>-ztag login -s</c> and <c>login</c> (reading the password from standard input) from state the test sets, and
/// records every command and what was written to it. Anything else started through it exits 1 straight away, unless
/// <see cref="Other"/> answers it. Shared by the Core and App tests.
/// </summary>
internal sealed class FakeP4(TimeProvider? time = null) : IProcessLauncher
{
    private readonly List<FakeP4Run> _runs = [];

    /// <summary>The clock tickets expire by; settable, for a harness that makes its clock after its launcher.</summary>
    public TimeProvider Time { get; set; } = time ?? TimeProvider.System;

    public string User { get; set; } = "matt";

    public string Client { get; set; } = "matt-ws";

    /// <summary>What <c>p4 set -q P4PORT</c> says; null when P4PORT isn't set anywhere.</summary>
    public string? Port { get; set; } = "ssl:perforce:1666";

    public string ServerAddress { get; set; } = "perforce:1666";

    /// <summary>The workspace root; null leaves it out of <c>p4 info</c>.</summary>
    public string? Root { get; set; }

    /// <summary>False reports the client as <c>*unknown*</c>: not a workspace.</summary>
    public bool IsWorkspace { get; set; } = true;

    public string Password { get; set; } = "s3cret";

    /// <summary>When the ticket expires; null means not logged in at all.</summary>
    public DateTimeOffset? TicketExpires { get; set; }

    public TimeSpan TicketLifetime { get; set; } = TimeSpan.FromHours(12);

    /// <summary>The server can't be reached.</summary>
    public bool Unreachable { get; set; }

    /// <summary>P4LOGINSSO is set.</summary>
    public bool SingleSignOn { get; set; }

    /// <summary>What <c>p4 set -q P4PASSWD</c> says.</summary>
    public string? ConfiguredPassword { get; set; }

    /// <summary>When set, a login waits for it before answering, to test logins that overlap.</summary>
    public TaskCompletionSource? HoldLogins { get; set; }

    /// <summary>Answers anything that isn't <c>p4</c>: exit code, standard output. Null exits 1.</summary>
    public Func<ProcessStartSpec, (int ExitCode, string Output)?>? Other { get; set; }

    public string Executable { get; set; } = "p4";

    public IReadOnlyList<FakeP4Run> Runs
    {
        get
        {
            lock (_runs)
            {
                return _runs.ToArray();
            }
        }
    }

    public IReadOnlyList<FakeP4Run> Logins => Runs.Where(r => r.Command == "login" && !r.Arguments.Contains("-s")).ToArray();

    public int StatusChecks => Runs.Count(r => r.Command == "login" && r.Arguments.Contains("-s"));

    /// <summary>Logs in the way the user would in a terminal.</summary>
    public void LogInYourself() => TicketExpires = Time.GetUtcNow() + TicketLifetime;

    /// <summary>Everything started, <c>p4</c> or not.</summary>
    public IReadOnlyList<ProcessStartSpec> Started
    {
        get
        {
            lock (_runs)
            {
                return _started.ToArray();
            }
        }
    }

    private readonly List<ProcessStartSpec> _started = [];

    public IRunningProcess Start(ProcessStartSpec spec)
    {
        var run = new FakeP4Run(spec);
        lock (_runs)
        {
            _started.Add(spec);
            if (spec.FileName == Executable)
            {
                _runs.Add(run);
            }
        }
        return new FakeP4Process(this, run);
    }

    /// <summary>Plays <c>p4</c>: exit code, standard output, standard error.</summary>
    internal async Task<(int, string, string)> AnswerAsync(FakeP4Run run)
    {
        if (run.Spec.FileName != Executable)
        {
            return Other?.Invoke(run.Spec) is { } other ? (other.ExitCode, other.Output, "") : (1, "", "not p4");
        }
        const string connectFailed = "Perforce client error:\n\tConnect to server failed; check $P4PORT.\n\tTCP connect to perforce:1666 failed.";
        var now = Time.GetUtcNow();
        switch (run.Command)
        {
            case "info" when Unreachable:
                return (1, "", connectFailed);
            case "info":
                var root = Root is null ? "" : $"... clientRoot {Root}\n";
                return (0, $"... userName {run.User ?? User}\n... clientName {(IsWorkspace ? Client : "*unknown*")}\n{root}... serverAddress {ServerAddress}\n... serverVersion P4D/LINUX26X86_64/2025.1/1234567 (2025/06/01)\n", "");
            case "set" when run.Arguments is ["-q"]:
                // Every setting for the folder, as Perforce in the Files tab asks (DESIGN.md §18, "Finding the workspace").
                var all = new List<string>();
                if (Port is not null)
                {
                    all.Add($"P4PORT={Port}");
                }
                if (IsConfigured)
                {
                    all.Add($"P4CLIENT={Client}");
                    all.Add($"P4USER={User}");
                }
                if (SingleSignOn)
                {
                    all.Add("P4LOGINSSO=/usr/local/bin/sso-login %user%");
                }
                return (0, string.Join('\n', all) + "\n", "");
            case "set":
                var name = run.Arguments.LastOrDefault() ?? "";
                var value = name switch
                {
                    "P4PORT" => Port,
                    "P4LOGINSSO" => SingleSignOn ? "/usr/local/bin/sso-login %user%" : null,
                    "P4PASSWD" => ConfiguredPassword,
                    _ => null,
                };
                return (0, value is null ? "" : $"{name}={value}\n", "");
            case "login" when Unreachable:
                return (1, "", connectFailed);
            case "login" when run.Arguments.Contains("-s"):
                if (TicketExpires is not { } expires)
                {
                    return (1, "", "Perforce password (P4PASSWD) invalid or unset.");
                }
                if (expires <= now)
                {
                    return (1, "", "Your session has expired, please login again.");
                }
                var seconds = ((long)(expires - now).TotalSeconds).ToString(CultureInfo.InvariantCulture);
                return (0, $"... User {User}\n... Expiration {seconds}\n... TicketExpiration {seconds}\n", "");
            case "login":
                if (HoldLogins is { } hold)
                {
                    await hold.Task;
                }
                if (run.Input.FirstOrDefault() != Password)
                {
                    return (1, "Enter password: ", "Password invalid.");
                }
                TicketExpires = Time.GetUtcNow() + TicketLifetime;
                return (0, $"Enter password: \nUser {run.User ?? User} logged in.\n", "");
            case "fstat" or "edit" or "add" or "delete" or "revert" or "move" or "diff" or "print" or "reconcile" when Unreachable:
                return (1, "", connectFailed);
            case "fstat" or "edit" or "add" or "delete" or "revert" or "move" or "diff" or "print" or "reconcile":
                if (HoldFileCommands is { } held)
                {
                    await held.Task;
                }
                lock (Depot)
                {
                    return new FakeDepotCommand(this, run).Answer();
                }
            default:
                return (0, "", "");
        }
    }

    // ---- Files (DESIGN.md §18, "Perforce in the Files tab") -----------------------------------------------------------

    /// <summary>
    /// The files Perforce has, or that are opened here, by their local paths: what <c>fstat</c>, <c>edit</c>, <c>add</c>,
    /// <c>delete</c>, <c>revert</c>, <c>move</c>, <c>diff -sa</c>, <c>print</c> and <c>reconcile -a</c> answer from and
    /// change. Lock it to read it while commands may run.
    /// </summary>
    public Dictionary<string, FakeDepotFile> Depot { get; } = new(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

    /// <summary>A file in the depot at revision 1, synced and not opened, with <paramref name="text"/> as its depot copy.</summary>
    public FakeDepotFile Has(string path, string text = "")
    {
        lock (Depot)
        {
            return Depot[path] = new FakeDepotFile { DepotText = text };
        }
    }

    /// <summary>The depot's record of a file, read under its lock; null when it has none.</summary>
    public FakeDepotFile? Find(string path)
    {
        lock (Depot)
        {
            return Depot.TryGetValue(path, out var file) ? file : null;
        }
    }

    /// <summary>P4CLIENT is set somewhere for the folder; false leaves <c>p4 set -q</c> with nothing but P4PORT.</summary>
    public bool IsConfigured { get; set; } = true;

    /// <summary>When set, the file commands wait for it before answering, as a slow or hung server does.</summary>
    public TaskCompletionSource? HoldFileCommands { get; set; }

    /// <summary>The file commands run, in order: <c>edit</c>, <c>add</c> and the rest, without <c>fstat</c>.</summary>
    public IReadOnlyList<FakeP4Run> FileCommands => Runs.Where(r => r.Command is "edit" or "add" or "delete" or "revert" or "move" or "reconcile").ToArray();
}

/// <summary>A file the fake depot has, or that's opened here (<see cref="FakeP4.Depot"/>).</summary>
internal sealed class FakeDepotFile
{
    /// <summary>The revision synced here; null for a file opened for add, which the depot doesn't have yet.</summary>
    public int? Have { get; set; } = 1;

    /// <summary>What it's opened for here (<c>edit</c>, <c>add</c>, <c>delete</c>, <c>move/add</c>…); null when it isn't.</summary>
    public string? Action { get; set; }

    /// <summary>The changelist it's opened in: a number, or <c>default</c>.</summary>
    public string Change { get; set; } = "default";

    /// <summary>Who else has it open, <c>user@workspace</c>.</summary>
    public List<string> OtherOpens { get; } = [];

    /// <summary>The depot's copy, which <c>print</c> writes.</summary>
    public string DepotText { get; set; } = "";

    /// <summary>It differs from the depot's copy: <c>diff -sa</c> lists it.</summary>
    public bool Differs { get; set; }

    /// <summary>An error Perforce gives for any command on it, such as another user's lock.</summary>
    public string? Refuse { get; set; }

    /// <summary>For a moved file's new name, the depot path of its old one.</summary>
    public string? MovedFrom { get; set; }
}

/// <summary>One of the file commands, answered from <see cref="FakeP4.Depot"/>, under its lock.</summary>
internal sealed class FakeDepotCommand(FakeP4 p4, FakeP4Run run)
{
    private readonly List<string> _out = [];
    private readonly List<string> _err = [];
    private string? _change;
    private bool _keepLocal;
    private string? _outputFile;
    private readonly List<string> _specs = [];

    public (int, string, string) Answer()
    {
        ReadOptions();
        switch (run.Command)
        {
            case "fstat":
                Fstat();
                break;
            case "edit":
                Each(file =>
                {
                    if (file.Value.Action is { } action)
                    {
                        Out($"{Depot(file.Key)}#1 - currently opened for {action}");
                    }
                    else
                    {
                        Open(file, "edit");
                    }
                }, "file(s) not on client.");
                break;
            case "add":
                Add();
                break;
            case "delete":
                Each(file =>
                {
                    Open(file, "delete");
                    if (!_keepLocal)
                    {
                        File.Delete(file.Key);
                    }
                }, "file(s) not on client.");
                break;
            case "revert":
                Each(Revert, "file(s) not opened on this client.");
                break;
            case "move":
                Move();
                break;
            case "diff":
                foreach (var file in _specs.SelectMany(Match).Where(f => f.Value.Action is not null && f.Value.Differs))
                {
                    Out(file.Key);
                }
                break;
            case "print":
                if (_specs.SelectMany(s => Match(s.Replace("#have", "", StringComparison.Ordinal))).FirstOrDefault() is { Key: not null } printed && _outputFile is { } output)
                {
                    File.WriteAllText(output, printed.Value.DepotText);
                }
                else
                {
                    _err.Add($"{_specs.FirstOrDefault()} - no such file(s).");
                }
                break;
            case "reconcile":
                foreach (var folder in _specs.Select(s => s.EndsWith("...", StringComparison.Ordinal) ? s[..^4] : s))
                {
                    foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(p => !p4.Depot.ContainsKey(p)))
                    {
                        p4.Depot[path] = new FakeDepotFile { Have = null, Action = "add", Change = _change ?? "default" };
                        Out($"{Depot(path)}#1 - opened for add");
                    }
                }
                break;
        }
        var exit = _err.Count > 0 && _out.Count == 0 ? 1 : 0;
        return (exit, _out.Count == 0 ? "" : string.Join('\n', _out) + "\n", _err.Count == 0 ? "" : string.Join('\n', _err) + "\n");
    }

    private void ReadOptions()
    {
        var args = run.Arguments;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "-c":
                    _change = args[++i];
                    break;
                case "-k":
                    _keepLocal = true;
                    break;
                case "-o":
                    _outputFile = args[++i];
                    break;
                case "-T":
                    i++;
                    break;
                case "-q" or "-f" or "-a" or "-sa" or "-n":
                    break;
                default:
                    _specs.Add(args[i]);
                    break;
            }
        }
    }

    private void Fstat()
    {
        foreach (var spec in _specs)
        {
            var found = Match(spec).ToArray();
            if (found.Length == 0)
            {
                _err.Add($"{spec} - no such file(s).");
            }
            foreach (var (path, file) in found)
            {
                var record = new List<string> { $"... depotFile {Depot(path)}", $"... clientFile {Escape(path)}" };
                if (file.Have is { } have)
                {
                    record.Add("... headAction edit");
                    record.Add($"... haveRev {have}");
                }
                if (file.Action is { } action)
                {
                    record.Add($"... action {action}");
                    record.Add($"... change {file.Change}");
                }
                if (file.MovedFrom is { } from)
                {
                    record.Add($"... movedFile {from}");
                }
                record.AddRange(file.OtherOpens.Select((o, n) => $"... otherOpen{n} {o}"));
                _out.Add(string.Join('\n', record) + "\n");
            }
        }
    }

    private void Add()
    {
        foreach (var spec in _specs)
        {
            var path = FakeP4Paths.Unescape(spec);
            if (p4.Depot.TryGetValue(path, out var known) && known.Have is not null)
            {
                Out($"{path} - can't add existing file");
            }
            else if (known?.Refuse is { } refused)
            {
                _err.Add(refused);
            }
            else
            {
                p4.Depot[path] = new FakeDepotFile { Have = null, Action = "add", Change = _change ?? "default" };
                Out($"{Depot(path)}#1 - opened for add");
            }
        }
    }

    private void Revert(KeyValuePair<string, FakeDepotFile> file)
    {
        if (file.Value.Action is not { } action)
        {
            _err.Add($"{file.Key} - file(s) not opened on this client.");
            return;
        }
        if (action is "add" or "move/add")
        {
            p4.Depot.Remove(file.Key);
        }
        else
        {
            file.Value.Action = null;
            file.Value.Change = "default";
        }
        Out($"{Depot(file.Key)}#1 - was {action}, reverted");
    }

    private void Move()
    {
        if (_specs.Count != 2)
        {
            _err.Add("Usage: move [-c changelist#] [-f -n -k] [-t filetype] fromFile toFile");
            return;
        }
        var from = FakeP4Paths.Unescape(_specs[0]);
        var to = FakeP4Paths.Unescape(_specs[1]);
        if (!p4.Depot.TryGetValue(from, out var moved) || moved.Action is not ("edit" or "add"))
        {
            _err.Add($"{from} - file(s) not opened for edit.");
            return;
        }
        moved.Action = "move/delete";
        p4.Depot[to] = new FakeDepotFile { Have = null, Action = "move/add", Change = _change ?? moved.Change, MovedFrom = Depot(from), DepotText = moved.DepotText };
        Out($"{Depot(to)}#1 - moved from {Depot(from)}#1");
    }

    /// <summary>Opens a file the depot has, in the command's changelist.</summary>
    private void Open(KeyValuePair<string, FakeDepotFile> file, string action)
    {
        if (file.Value.Have is null)
        {
            _err.Add($"{file.Key} - file(s) not on client.");
            return;
        }
        file.Value.Action = action;
        file.Value.Change = _change ?? "default";
        if (action == "edit" && File.Exists(file.Key))
        {
            // Perforce makes a file it checks out writable.
            File.SetAttributes(file.Key, File.GetAttributes(file.Key) & ~FileAttributes.ReadOnly);
        }
        Out($"{Depot(file.Key)}#1 - opened for {action}");
    }

    /// <summary>Runs <paramref name="act"/> on each file the specs name, saying <paramref name="none"/> for a spec naming none.</summary>
    private void Each(Action<KeyValuePair<string, FakeDepotFile>> act, string none)
    {
        foreach (var spec in _specs)
        {
            var found = Match(spec).ToArray();
            if (found.Length == 0)
            {
                _err.Add($"{FakeP4Paths.Unescape(spec)} - {none}");
            }
            foreach (var file in found)
            {
                if (file.Value.Refuse is { } refused)
                {
                    _err.Add(refused);
                    continue;
                }
                act(file);
            }
        }
    }

    /// <summary>The files a spec names: one, a folder's (<c>folder/*</c>), or everything under it (<c>folder/...</c>).</summary>
    private IEnumerable<KeyValuePair<string, FakeDepotFile>> Match(string spec)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (spec.EndsWith("...", StringComparison.Ordinal))
        {
            var under = FakeP4Paths.Unescape(spec[..^3]);
            return p4.Depot.Where(f => f.Key.StartsWith(under, comparison)).ToArray();
        }
        if (spec.EndsWith('*'))
        {
            var folder = Path.TrimEndingDirectorySeparator(FakeP4Paths.Unescape(spec[..^1]));
            return p4.Depot.Where(f => string.Equals(Path.GetDirectoryName(f.Key), folder, comparison)).ToArray();
        }
        var path = FakeP4Paths.Unescape(spec);
        return p4.Depot.TryGetValue(path, out var file) ? [new(path, file)] : [];
    }

    private void Out(string line) => _out.Add(line);

    /// <summary>A depot path for a local one: <c>//depot/</c> and its name.</summary>
    private static string Depot(string path) => $"//depot/{Path.GetFileName(path)}";

    private static string Escape(string path) =>
        path.Replace("%", "%25", StringComparison.Ordinal).Replace("@", "%40", StringComparison.Ordinal).Replace("#", "%23", StringComparison.Ordinal);
}

/// <summary>Perforce's escapes for its wildcard characters, undone as <c>p4</c> reads a spec.</summary>
internal static class FakeP4Paths
{
    public static string Unescape(string spec) =>
        spec.Replace("%40", "@", StringComparison.Ordinal).Replace("%23", "#", StringComparison.Ordinal)
            .Replace("%2A", "*", StringComparison.OrdinalIgnoreCase).Replace("%25", "%", StringComparison.Ordinal);
}

/// <summary>One command started through <see cref="FakeP4"/>.</summary>
internal sealed class FakeP4Run
{
    private readonly List<string> _input = [];

    public FakeP4Run(ProcessStartSpec spec)
    {
        Spec = spec;
        var args = spec.Arguments.ToList();
        // Global options come first.
        var i = 0;
        while (i < args.Count && args[i].StartsWith('-') && args[i] != "-s")
        {
            switch (args[i])
            {
                case "-p":
                    Port = args[i + 1];
                    i += 2;
                    break;
                case "-u":
                    User = args[i + 1];
                    i += 2;
                    break;
                default:
                    i++;
                    break;
            }
        }
        Command = i < args.Count ? args[i] : "";
        Arguments = args.Skip(i + 1).ToArray();
    }

    public ProcessStartSpec Spec { get; }

    public string Command { get; }

    public IReadOnlyList<string> Arguments { get; }

    /// <summary>The <c>-p</c> global option, if given.</summary>
    public string? Port { get; }

    /// <summary>The <c>-u</c> global option, if given.</summary>
    public string? User { get; }

    /// <summary>Lines written to standard input.</summary>
    public IReadOnlyList<string> Input
    {
        get
        {
            lock (_input)
            {
                return _input.ToArray();
            }
        }
    }

    public void AddInput(string line)
    {
        lock (_input)
        {
            _input.Add(line);
        }
    }
}

/// <summary>Answers when standard input is closed, which <c>ProcessRunner</c> does right after writing any input.</summary>
internal sealed class FakeP4Process(FakeP4 p4, FakeP4Run run) : IRunningProcess
{
    private readonly Channel<string> _stdout = Channel.CreateUnbounded<string>();
    private readonly Channel<string> _stderr = Channel.CreateUnbounded<string>();
    private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _answered;

    public int Id => 7000;

    public ChannelReader<string> StandardOutput => _stdout.Reader;

    public ChannelReader<string> StandardError => _stderr.Reader;

    public Task<int> Exited => _exited.Task;

    public ValueTask WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        run.AddInput(line);
        return ValueTask.CompletedTask;
    }

    public void CloseStandardInput()
    {
        if (Interlocked.Exchange(ref _answered, 1) == 0)
        {
            _ = AnswerAsync();
        }
    }

    private async Task AnswerAsync()
    {
        var (code, output, error) = await p4.AnswerAsync(run);
        Write(_stdout, output);
        Write(_stderr, error);
        _stdout.Writer.TryComplete();
        _stderr.Writer.TryComplete();
        _exited.TrySetResult(code);
    }

    /// <summary>
    /// Each line, as the real launcher reads them: blank lines too, which separate <c>-ztag</c>'s records, but not the
    /// nothing after the last line's end.
    /// </summary>
    private static void Write(Channel<string> channel, string text)
    {
        if (text.Length == 0)
        {
            return;
        }
        var lines = text.Split('\n');
        var count = text.EndsWith('\n') ? lines.Length - 1 : lines.Length;
        for (var i = 0; i < count; i++)
        {
            channel.Writer.TryWrite(lines[i]);
        }
    }

    public void Kill()
    {
        _stdout.Writer.TryComplete();
        _stderr.Writer.TryComplete();
        _exited.TrySetResult(-1);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
