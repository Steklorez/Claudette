using System.Globalization;
using Deventry.Core.Accounts;
using Deventry.Core.Processes;

namespace Deventry.Core.LocalModels;

/// <summary>
/// Starting an account's server when it isn't running (DESIGN.md §18, "Ollama and llama.cpp accounts"): where its
/// program is, and the command and environment that serve the account's model at its address and window. Only for an
/// address on this computer; a server Deventry starts listens on nothing else.
/// </summary>
public static class LocalServerLaunch
{
    /// <summary>
    /// The server's program: the one the account names, else one on <paramref name="searchPath"/>, else where its
    /// installer puts it. Null when there's none.
    /// </summary>
    /// <param name="exists">Whether a file exists; tests replace it.</param>
    public static string? Find(LocalServerKind kind, string? configuredPath, string? searchPath, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (configuredPath?.Trim() is { Length: > 0 } configured)
        {
            return exists(configured) ? configured : null;
        }
        var name = OperatingSystem.IsWindows() ? kind.Executable + ".exe" : kind.Executable;
        return OnPath(searchPath, name).Concat(KnownPaths(kind)).FirstOrDefault(exists);
    }

    /// <summary>Where each server's installer puts its program on this system.</summary>
    public static IReadOnlyList<string> KnownPaths(LocalServerKind kind)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (kind == LocalServers.Ollama)
        {
            return OperatingSystem.IsWindows() ? [Path.Combine(local, "Programs", "Ollama", "ollama.exe")]
                : OperatingSystem.IsMacOS() ? ["/Applications/Ollama.app/Contents/Resources/ollama", "/opt/homebrew/bin/ollama", "/usr/local/bin/ollama"]
                : ["/usr/local/bin/ollama", "/usr/bin/ollama"];
        }
        // winget's llama.cpp links its programs into its Links folder; Homebrew and the Linux packages use their bin.
        return OperatingSystem.IsWindows() ? [Path.Combine(local, "Microsoft", "WinGet", "Links", "llama-server.exe")]
            : OperatingSystem.IsMacOS() ? ["/opt/homebrew/bin/llama-server", "/usr/local/bin/llama-server"]
            : [Path.Combine(home, ".local", "bin", "llama-server"), "/usr/local/bin/llama-server", "/usr/bin/llama-server"];
    }

    private static IEnumerable<string> OnPath(string? searchPath, string name)
    {
        foreach (var directory in (searchPath ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(directory.Trim().Trim('"'), name);
            }
            catch (ArgumentException)
            {
                continue;
            }
            yield return candidate;
        }
    }

    /// <summary>
    /// What starts <paramref name="kind"/>'s server for <paramref name="local"/>: Ollama as <c>ollama serve</c> on the
    /// address's port, with the account's window as the default for the models it loads (<c>OLLAMA_CONTEXT_LENGTH</c>);
    /// <c>llama-server</c> with the account's model, its name, its window, on 127.0.0.1 and the address's port, without a
    /// key.
    /// </summary>
    /// <param name="environment">The user's environment (<see cref="UserEnvironment"/>), which the server's variables go over.</param>
    /// <exception cref="InvalidOperationException">The address isn't on this computer, or llama.cpp's account has no model to load.</exception>
    public static ProcessStartSpec Command(LocalServerKind kind, string executable, LocalServerSettings local, IReadOnlyDictionary<string, string> environment)
    {
        if (!LocalServers.IsOnThisComputer(local.Address))
        {
            throw new InvalidOperationException($"Deventry starts {kind.ServerName} only on this computer, and {local.Address} isn't.");
        }
        var port = LocalServers.PortOf(local.Address).ToString(CultureInfo.InvariantCulture);
        var variables = new Dictionary<string, string>(environment);
        if (kind == LocalServers.Ollama)
        {
            variables["OLLAMA_HOST"] = $"127.0.0.1:{port}";
            if (local.Window > 0)
            {
                variables["OLLAMA_CONTEXT_LENGTH"] = local.Window.ToString(CultureInfo.InvariantCulture);
            }
            return new ProcessStartSpec(executable, ["serve"]) { Environment = variables, TrackProcessTree = true };
        }
        if (local.ServerModel?.Trim() is not { Length: > 0 } model)
        {
            throw new InvalidOperationException("llama-server needs a model to load: a .gguf file, or a model on Hugging Face.");
        }
        List<string> args = IsModelFile(model) ? ["-m", model] : ["-hf", model];
        if (local.Model.Trim() is { Length: > 0 } alias)
        {
            args.AddRange(["--alias", alias]);
        }
        if (local.Window > 0)
        {
            args.AddRange(["-c", local.Window.ToString(CultureInfo.InvariantCulture)]);
        }
        args.AddRange(["--host", "127.0.0.1", "--port", port]);
        // A key in the user's environment would make it ask for one that Claude Code isn't given.
        variables.Remove("LLAMA_API_KEY");
        return new ProcessStartSpec(executable, args) { Environment = variables, TrackProcessTree = true };
    }

    /// <summary>A model file, rather than a model on Hugging Face (<c>user/repo:quant</c>).</summary>
    public static bool IsModelFile(string model) =>
        model.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) || Path.IsPathFullyQualified(model) || model.StartsWith('.');

    /// <summary>
    /// The name an account gives a model file it starts <c>llama-server</c> with: the file's name, without
    /// <c>.gguf</c>; or for a model on Hugging Face, its repository's.
    /// </summary>
    public static string ModelName(string serverModel)
    {
        var model = serverModel.Trim();
        if (IsModelFile(model))
        {
            return Path.GetFileNameWithoutExtension(model.Replace('\\', '/').Split('/')[^1]);
        }
        var repository = model.Split('/')[^1];
        return repository.Replace("-GGUF", "", StringComparison.OrdinalIgnoreCase).Replace(':', '-').ToLowerInvariant();
    }

    /// <summary>How long a server Deventry started has to answer before Deventry gives up on it.</summary>
    public static TimeSpan StartTimeout(LocalServerKind kind) => kind == LocalServers.LlamaCpp ? TimeSpan.FromMinutes(1) : TimeSpan.FromSeconds(30);
}
