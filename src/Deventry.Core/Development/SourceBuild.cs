namespace Deventry.Core.Development;

/// <summary>
/// Deventry running from its own build output in a source checkout, for working on Deventry from Deventry
/// (DESIGN.md §9, "Working on Deventry").
/// </summary>
/// <param name="OutputDirectory">The app's build output, such as <c>src/Deventry.App/bin/Debug/net10.0</c>.</param>
/// <param name="RepositoryRoot">The checkout: the folder with <c>Deventry.slnx</c>.</param>
public sealed record SourceBuild(string OutputDirectory, string RepositoryRoot)
{
    public const string SolutionFileName = "Deventry.slnx";

    private const int MaxDepth = 8;

    /// <summary>
    /// The source build <paramref name="appDirectory"/> is part of: a folder inside a <c>bin</c> folder, below a folder
    /// with <c>Deventry.slnx</c>. Null for an installed Deventry.
    /// </summary>
    public static SourceBuild? Detect(string appDirectory)
    {
        var output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));
        var insideBin = false;
        var folder = output;
        for (var depth = 0; depth < MaxDepth && !string.IsNullOrEmpty(folder); depth++)
        {
            if (string.Equals(Path.GetFileName(folder), "bin", StringComparison.OrdinalIgnoreCase))
            {
                insideBin = true;
            }
            else if (insideBin && File.Exists(Path.Combine(folder, SolutionFileName)))
            {
                return new SourceBuild(output, folder);
            }
            folder = Path.GetDirectoryName(folder);
        }
        return null;
    }

    /// <summary>The app's project, the folder the <c>bin</c> folder is in, such as <c>src/Deventry.App</c>; null if there's no <c>bin</c>.</summary>
    public string? ProjectDirectory => BinFolder() is { } bin ? Path.GetDirectoryName(bin) : null;

    /// <summary>The configuration it was built in: the folder below <c>bin</c>, such as <c>Debug</c>.</summary>
    public string? Configuration => BinFolder() is { } bin && Path.GetRelativePath(bin, OutputDirectory).Split(Path.DirectorySeparatorChar)[0] is { Length: > 0 } name and not "."
        ? name
        : null;

    private string? BinFolder()
    {
        for (var folder = OutputDirectory; !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
        {
            if (string.Equals(Path.GetFileName(folder), "bin", StringComparison.OrdinalIgnoreCase))
            {
                return folder;
            }
        }
        return null;
    }
}

/// <summary>A source build of Deventry that's running now, and where its new builds appear.</summary>
/// <param name="SourceOutput">The build output that's watched for new builds.</param>
/// <param name="RunningDirectory">The folder this Deventry runs from: a copy of a build, or the build output itself.</param>
/// <param name="RunningStamp">When the running build was written: only a newer build counts as new.</param>
public sealed record DevelopmentBuild(string SourceOutput, string RunningDirectory, DateTime RunningStamp);
