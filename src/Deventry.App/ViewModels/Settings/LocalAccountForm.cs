using System.Collections.ObjectModel;
using System.Globalization;
using Deventry.App.Services;
using Deventry.Core;
using Deventry.Core.Accounts;
using Deventry.Core.LocalModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Deventry.App.ViewModels.Settings;

/// <summary>A model a server offers, as the form lists it: its name, and what it is.</summary>
public sealed record LocalModelChoice(LocalModel Model, string Detail)
{
    public string Name => Model.Name;

    /// <summary>Claude Code can run on it: it can use tools, or the server didn't say.</summary>
    public bool CanUse => !Model.CapabilitiesKnown || Model.CanUseTools;

    public override string ToString() => Name;
}

/// <summary>
/// What an Ollama or llama.cpp account says (DESIGN.md §18, "Ollama and llama.cpp accounts"), in Settings → Agents →
/// Claude Code → Accounts and on the sign-in screen: what the kind is, its server's address and <b>Check</b>, its
/// model and the window it runs at, its longest reply, its key, keeping Claude Code off the internet, and starting the
/// server. The same form changes an account's settings.
/// </summary>
public sealed partial class LocalAccountForm : ObservableObject
{
    private readonly LocalServersService _servers;
    private readonly Func<string, Task<string?>> _pickFile;
    private readonly Func<string, Task> _openUrl;
    private readonly bool _hadKey;
    private readonly Func<Task<string?>> _readKey;

    /// <param name="from">An account's settings, to change them; null for a new account.</param>
    /// <param name="readKey">The account's key from the credential store, for asking its server before a new one is typed.</param>
    public LocalAccountForm(LocalServerKind kind, LocalServersService servers, string credentialStore, Func<string, Task<string?>> pickFile,
        Func<string, Task> openUrl, LocalServerSettings? from = null, Func<Task<string?>>? readKey = null)
    {
        _readKey = readKey ?? (() => Task.FromResult<string?>(null));
        Kind = kind;
        _servers = servers;
        CredentialStore = credentialStore;
        _pickFile = pickFile;
        _openUrl = openUrl;
        IsNew = from is null;
        _hadKey = from?.HasKey ?? false;
        Address = from?.Address ?? kind.DefaultAddress;
        Model = from?.Model ?? "";
        BackgroundModel = from?.BackgroundModel ?? "";
        Window = from?.Window is > 0 and var window ? window : LocalServers.DefaultWindow;
        LongestReply = from?.LongestReply ?? LocalServers.DefaultLongestReply;
        KeepOffline = from?.KeepOffline ?? false;
        TakesKey = from?.HasKey ?? false;
        StartServer = from?.StartServer ?? true;
        ServerPath = from?.ServerPath ?? "";
        ServerModel = from?.ServerModel ?? "";
        MakeCopy = new InlineConfirmation(MakeCopyAsync);
        _ = FindProgramAsync();
    }

    public LocalServerKind Kind { get; }

    public string CredentialStore { get; }

    /// <summary>A new account's form; an account's own changes its settings.</summary>
    public bool IsNew { get; }

    public bool IsOllama => Kind == LocalServers.Ollama;

    public bool IsLlamaCpp => Kind == LocalServers.LlamaCpp;

    // ---- What it is ------------------------------------------------------------------------------------------------------

    /// <summary>What an account of this kind's tabs will be, said before anything is filled in.</summary>
    public string Explanation =>
        $"Claude Code runs as it always does, but its model runs in {Kind.ServerName} on this computer, or at the address below. " +
        "Your prompts and files go to that server for the model's answers, and nowhere else. " +
        "Claude Code itself still talks to Anthropic for its updates and telemetry unless you keep it off the internet below. " +
        "Anthropic doesn't support Claude Code on models other than Claude: how well it works depends on the model you choose, " +
        "and what goes wrong is the model's or the server's to fix, not Claude Code's.";

    /// <summary>How the kind's server works, in a line.</summary>
    public string HowItWorks => IsOllama
        ? "Ollama runs a model at the window its own setting gives it (the app's slider, or OLLAMA_CONTEXT_LENGTH), which a request can't change: 4,096 tokens on a GPU under 24 GB unless changed. A server Deventry starts runs at the window below."
        : "llama-server serves the model it was started with, at the window it was started with (-c), or the whole window the model was trained for. It refuses a request that doesn't fit, rather than cutting it.";

    public string ProjectText => IsOllama ? "ollama.com" : "llama.cpp on GitHub";

    public string DocsText => IsOllama ? "Claude Code on Ollama" : "llama-server's options";

    [RelayCommand]
    private Task OpenProjectAsync() => _openUrl(Kind.ProjectUrl);

    [RelayCommand]
    private Task OpenDocsAsync() => _openUrl(Kind.DocsUrl);

    // ---- The server ------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartServer), nameof(StartNote))]
    public partial string Address { get; set; }

    partial void OnAddressChanged(string value)
    {
        CheckText = null;
        ModelChoices.Clear();
    }

    public string AddressPlaceholder => Kind.DefaultAddress;

    [ObservableProperty]
    public partial bool IsChecking { get; private set; }

    /// <summary>What <b>Check</b> found, or why it couldn't.</summary>
    [ObservableProperty]
    public partial string? CheckText { get; private set; }

    [ObservableProperty]
    public partial bool CheckFailed { get; private set; }

    public ObservableCollection<LocalModelChoice> ModelChoices { get; } = [];

    /// <summary>Asks the server for its models, and for llama.cpp, the window its model runs at.</summary>
    [RelayCommand]
    private async Task CheckAsync()
    {
        if (LocalServers.NormalizeAddress(Address) is not { } address)
        {
            CheckText = $"The address is where {Kind.ServerName} listens, such as {Kind.DefaultAddress}.";
            CheckFailed = true;
            return;
        }
        IsChecking = true;
        CheckText = $"Asking {Kind.ServerName}…";
        CheckFailed = false;
        try
        {
            var report = await (await ServerAsync(address)).CheckAsync();
            ModelChoices.Clear();
            foreach (var model in report.Models)
            {
                ModelChoices.Add(new LocalModelChoice(model, Describe(model)));
            }
            var usable = ModelChoices.Count(c => c.CanUse);
            CheckText = report.Models.Count == 0
                ? $"{Kind.ServerName} {report.Version} answers, but has no models yet.{(IsOllama ? " Pull one with \"ollama pull <model>\"." : "")}"
                : $"{Kind.ServerName} {report.Version}: {Count(report.Models.Count, "model")}{(usable < report.Models.Count ? $", {usable} of them can use tools" : "")}.";
            // The account's model, or the only one there is.
            SelectedModel = ModelChoices.FirstOrDefault(c => c.Name == Model.Trim()) ?? (ModelChoices.Count == 1 ? ModelChoices[0] : null);
        }
        catch (LocalServerException ex)
        {
            CheckText = ex.Message;
            CheckFailed = true;
        }
        finally
        {
            IsChecking = false;
        }
    }

    private static string Count(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";

    /// <summary>What a model is, beside its name: "5.2 GB · 40,960 tokens · tools, thinking".</summary>
    internal static string Describe(LocalModel model)
    {
        var parts = new List<string>();
        if (model.IsCloud)
        {
            parts.Add("runs on ollama.com, not this computer");
        }
        else if (model.Size is { } size)
        {
            parts.Add(Formats.Size(size));
        }
        if ((model.Window ?? model.TrainedWindow) is { } window)
        {
            parts.Add($"{window.ToString("N0", CultureInfo.CurrentCulture)} tokens");
        }
        if (model.CapabilitiesKnown)
        {
            var can = new[] { model.CanUseTools ? "tools" : null, model.CanThink ? "thinking" : null, model.CanSee ? "images" : null }.OfType<string>().ToList();
            parts.Add(model.CanUseTools ? string.Join(", ", can) : "can't use tools, so Claude Code can't run on it");
        }
        return string.Join(" · ", parts);
    }

    // ---- The model -------------------------------------------------------------------------------------------------------

    /// <summary>The model chosen from what <b>Check</b> found; choosing one fills in its name and window.</summary>
    [ObservableProperty]
    public partial LocalModelChoice? SelectedModel { get; set; }

    partial void OnSelectedModelChanged(LocalModelChoice? value)
    {
        if (value is null)
        {
            return;
        }
        Model = value.Name;
        // The window it runs at when the server says, else the most it was trained for, up to Deventry's default.
        if (value.Model.Window is { } runs)
        {
            Window = runs;
        }
        else if (value.Model.TrainedWindow is { } trained)
        {
            Window = Math.Min(trained, LocalServers.DefaultWindow);
        }
        ServerWindow = value.Model.Window;
        OnPropertyChanged(nameof(ModelNote));
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelNote), nameof(CanMakeCopy), nameof(CopyText))]
    public partial string Model { get; set; }

    /// <summary>Why the chosen model won't do, or what to know about it.</summary>
    public string? ModelNote => SelectedModel is { CanUse: false }
        ? $"{SelectedModel.Name} can't use tools, and Claude Code does everything through its tools: choose another model."
        : SelectedModel?.Model.IsCloud == true ? $"{SelectedModel.Name} runs on ollama.com, not this computer, and counts toward your Ollama plan." : null;

    [ObservableProperty]
    public partial string BackgroundModel { get; set; }

    // ---- The window ------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowNote), nameof(WindowIsSmall), nameof(CanMakeCopy), nameof(CopyText), nameof(WindowValue))]
    public partial int Window { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowNote), nameof(WindowIsSmall), nameof(LongestReplyValue))]
    public partial int LongestReply { get; set; }

    /// <summary><see cref="Window"/> for its box, which counts in decimals.</summary>
    public decimal? WindowValue
    {
        get => Window;
        set => Window = value is > 0 ? (int)Math.Min(value.Value, int.MaxValue) : 0;
    }

    /// <summary><see cref="LongestReply"/> for its box.</summary>
    public decimal? LongestReplyValue
    {
        get => LongestReply;
        set => LongestReply = value is > 0 ? (int)Math.Min(value.Value, LocalServers.MaximumLongestReply) : LocalServers.DefaultLongestReply;
    }

    /// <summary>The window the server ran the model at when Deventry last looked; null when it hasn't.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowNote), nameof(WindowIsSmall), nameof(CanMakeCopy), nameof(CopyText))]
    public partial int? ServerWindow { get; private set; }

    /// <summary>The window is under what Claude Code needs to work well, or under what the account says.</summary>
    public bool WindowIsSmall => Window < LocalServers.RecommendedWindow || ServerWindow is { } runs && runs < Window;

    /// <summary>What the window means for Claude Code, and when it's too small, why that matters.</summary>
    public string WindowNote
    {
        get
        {
            var point = LocalServers.CompactionPoint(Window, LongestReply);
            var compacts = point > 0 ? $"Claude Code compacts the conversation at about {point.ToString("N0", CultureInfo.CurrentCulture)} tokens, keeping the longest reply and a margin free." : "";
            if (ServerWindow is { } runs && runs < Window)
            {
                return $"{Kind.ServerName} runs {Model.Trim()} with a {runs.ToString("N0", CultureInfo.CurrentCulture)}-token window here, not {Window.ToString("N0", CultureInfo.CurrentCulture)}. " +
                    (IsOllama
                        ? "Ollama would cut the start off each request without saying so, and Claude would forget what it was doing. Make a copy of the model with the window below, change Ollama's own setting, or let Deventry start Ollama, which runs it at the window below."
                        : "It refuses a request that doesn't fit: start llama-server with -c and the window below, or let Deventry start it.");
            }
            return Window < LocalServers.RecommendedWindow
                ? $"Claude Code needs 64,000 tokens or more to work well: its own instructions take about 15,000. {compacts}"
                : compacts;
        }
    }

    /// <summary>Has the server load the model, and says the window it loaded with.</summary>
    [RelayCommand]
    private async Task CheckWindowAsync()
    {
        if (Model.Trim() is not { Length: > 0 } model || LocalServers.NormalizeAddress(Address) is not { } address)
        {
            CheckText = "Choose the model first.";
            CheckFailed = true;
            return;
        }
        IsChecking = true;
        CheckText = $"Loading {model}…";
        CheckFailed = false;
        try
        {
            var server = await ServerAsync(address);
            ServerWindow = (await server.DescribeAsync(model))?.Window ?? await server.LoadAsync(model);
            CheckText = ServerWindow is { } runs
                ? $"{Kind.ServerName} runs {model} at {runs.ToString("N0", CultureInfo.CurrentCulture)} tokens."
                : $"{Kind.ServerName} didn't say the window it runs {model} at.";
        }
        catch (LocalServerException ex)
        {
            CheckText = ex.Message;
            CheckFailed = true;
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>Ollama on this computer can make the model a copy that runs at the account's window.</summary>
    public bool CanMakeCopy => IsOllama && Model.Trim().Length > 0 && LocalServers.IsOnThisComputer(Address)
        && SelectedModel?.Model.IsCloud != true && ServerWindow is { } runs && runs < Window;

    public string CopyText => $"Make a copy with a {Window.ToString("N0", CultureInfo.CurrentCulture)}-token window…";

    public string CopyQuestion => $"Ollama makes {OllamaServer.CopyName(Model.Trim(), Window)} from {Model.Trim()}, which runs at {Window.ToString("N0", CultureInfo.CurrentCulture)} tokens whatever Ollama's setting is. It shares the model's files, so nothing is downloaded or copied; ollama rm removes it.";

    /// <summary><b>Make a copy with a window…</b>, which asks first, in place.</summary>
    public InlineConfirmation MakeCopy { get; }

    private async Task MakeCopyAsync()
    {
        if (LocalServers.NormalizeAddress(Address) is not { } address)
        {
            return;
        }
        IsChecking = true;
        CheckFailed = false;
        try
        {
            if (await ServerAsync(address) is not OllamaServer ollama)
            {
                return;
            }
            var copy = await ollama.CopyWithWindowAsync(Model.Trim(), Window);
            var described = await ollama.DescribeAsync(copy) ?? new LocalModel(copy) { Window = Window };
            var choice = new LocalModelChoice(described, Describe(described));
            ModelChoices.Add(choice);
            SelectedModel = choice;
            CheckText = $"Made {copy}, which runs at {Window.ToString("N0", CultureInfo.CurrentCulture)} tokens.";
        }
        catch (LocalServerException ex)
        {
            CheckText = ex.Message;
            CheckFailed = true;
        }
        finally
        {
            IsChecking = false;
        }
    }

    // ---- The key ---------------------------------------------------------------------------------------------------------

    /// <summary>The server takes an API key: llama-server's <c>--api-key</c>, or ollama.com's.</summary>
    [ObservableProperty]
    public partial bool TakesKey { get; set; }

    public string TakesKeyText => IsOllama ? "It takes an API key (for Ollama's cloud at https://ollama.com)" : "It takes an API key (llama-server's --api-key)";

    [ObservableProperty]
    public partial string ApiKey { get; set; } = "";

    public string KeyPlaceholder => _hadKey ? $"Kept in {CredentialStore}: paste a new one to replace it" : "Paste the key";

    public string KeyNote => $"Kept in {CredentialStore}. Claude Code asks Deventry for it when a tab starts, so it's never in a command line or the environment.";

    /// <summary>
    /// The client for the server at <paramref name="address"/>, with the key typed in, else the account's own, for
    /// Deventry's own requests to it.
    /// </summary>
    private async Task<ILocalModelServer> ServerAsync(string address) =>
        _servers.ServerAt(Kind, address, !TakesKey ? null : Secret ?? (_hadKey ? await _readKey() : null));

    /// <summary>The key to keep for the account; null when it takes none, or none was typed.</summary>
    public string? Secret => TakesKey && ApiKey.Trim() is { Length: > 0 } key ? key : null;

    /// <summary>What's missing from the key; null when it's there, kept already or not needed.</summary>
    public string? SecretProblem => TakesKey && Secret is null && !_hadKey ? "Paste the server's API key, or untick that it takes one." : null;

    // ---- Claude Code -----------------------------------------------------------------------------------------------------

    [ObservableProperty]
    public partial bool KeepOffline { get; set; }

    // ---- Starting the server ---------------------------------------------------------------------------------------------

    /// <summary>The address is on this computer, where Deventry can start the server.</summary>
    public bool CanStartServer => LocalServers.IsOnThisComputer(Address);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartNote))]
    public partial bool StartServer { get; set; }

    public string StartText => $"Start {Kind.ServerName} when it isn't running";

    public string StartNote => !CanStartServer
        ? $"Deventry can start {Kind.ServerName} only on this computer."
        : IsOllama
            ? "Deventry runs \"ollama serve\" on the address's port, with the window below as the default for the models it loads, and stops it when Deventry closes. An Ollama that's already running, such as Ollama's app, is used as it is."
            : "Deventry runs llama-server with the model below, its name, the window and the address's port, on this computer alone and without a key, and stops it when Deventry closes.";

    [ObservableProperty]
    public partial string ServerPath { get; set; }

    /// <summary>Where Deventry found the program, for the box while it's empty.</summary>
    [ObservableProperty]
    public partial string ServerPathPlaceholder { get; private set; } = "";

    private async Task FindProgramAsync()
    {
        try
        {
            var found = await _servers.FindProgramAsync(Kind, null);
            ServerPathPlaceholder = found is null ? $"Not found: install {Kind.Name}, or choose {Kind.Executable}" : found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ServerPathPlaceholder = $"Choose {Kind.Executable}";
        }
    }

    public string ServerPathTitle => $"Path to {Kind.Executable}";

    [RelayCommand]
    private async Task BrowseServerAsync()
    {
        if (await _pickFile($"Choose {Kind.Executable}") is { } path)
        {
            ServerPath = path;
        }
    }

    /// <summary>For llama.cpp, the model llama-server loads when Deventry starts it: a .gguf file, or a model on Hugging Face.</summary>
    [ObservableProperty]
    public partial string ServerModel { get; set; }

    partial void OnServerModelChanged(string value)
    {
        // A model to load names the account's model, until it's given a name of its own.
        if (IsLlamaCpp && value.Trim().Length > 0 && (Model.Trim().Length == 0 || _namedFromServerModel))
        {
            Model = LocalServerLaunch.ModelName(value);
            _namedFromServerModel = true;
        }
    }

    private bool _namedFromServerModel;

    [RelayCommand]
    private async Task BrowseServerModelAsync()
    {
        if (await _pickFile("Choose the model file (.gguf)") is { } path)
        {
            ServerModel = path;
        }
    }

    // ---- What it says ----------------------------------------------------------------------------------------------------

    public LocalServerSettings ToSettings() => new()
    {
        Address = LocalServers.NormalizeAddress(Address) ?? Address.Trim(),
        Model = Model.Trim(),
        BackgroundModel = Trimmed(BackgroundModel),
        Window = Window,
        LongestReply = LongestReply,
        KeepOffline = KeepOffline,
        HasKey = TakesKey,
        StartServer = StartServer && CanStartServer,
        ServerPath = Trimmed(ServerPath),
        ServerModel = IsLlamaCpp ? Trimmed(ServerModel) : null,
    };

    /// <summary>What's wrong with the settings, as Settings says it; null when they're fine.</summary>
    public string? Problem => SelectedModel is { CanUse: false } choice
        ? $"{choice.Name} can't use tools: choose another model."
        : LocalServers.Problem(Kind, ToSettings()) ?? SecretProblem;

    /// <summary>Forgets the key typed in, once it's saved or the form is left.</summary>
    public void ClearSecrets() => ApiKey = "";

    private static string? Trimmed(string? value) => value?.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}
