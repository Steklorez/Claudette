namespace Claudette.Core.ProjectTools;

/// <summary>
/// Runs every project type that's on on a tab's folder and picks the project to use (DESIGN.md §18, "Project tools"):
/// the one the user picked for this folder, when there are several, else the nearest.
/// </summary>
public sealed class ProjectToolDetector(IReadOnlyList<IProjectType> types)
{
    public IReadOnlyList<IProjectType> Types { get; } = types;

    /// <summary>The folders the search never looks into: the shared ones and every type's, whether it's on or not.</summary>
    public IReadOnlySet<string> SkippedFolders { get; } =
        new HashSet<string>(ProjectSearch.SkippedFolders.Concat(types.SelectMany(t => t.SkippedFolders)), StringComparer.OrdinalIgnoreCase);

    /// <param name="chosenPath">The project the user picked for this folder, if any.</param>
    public ProjectDetection Detect(string folder, ProjectToolContext context, string? chosenPath = null)
    {
        var candidates = new List<ProjectCandidate>();
        foreach (var type in Types.Where(t => context.Settings.IsEnabled(t.Kind)))
        {
            try
            {
                candidates.AddRange(type.Find(folder, context));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A type that can't look is as good as one that found nothing.
            }
        }
        if (candidates.Count == 0)
        {
            return ProjectDetection.None;
        }
        var comparison = context.OS.PathComparison();
        var chosen = candidates.FirstOrDefault(c => chosenPath is not null && string.Equals(c.Path, chosenPath, comparison)) ?? candidates[0];
        return new ProjectDetection(candidates, Describe(chosen, context));
    }

    /// <summary>
    /// Reads one project. A type that fails still gives a project with the problem, and no actions. Its note to Claude
    /// is left out while Settings says not to tell Claude about its kind.
    /// </summary>
    public ProjectInfo Describe(ProjectCandidate candidate, ProjectToolContext context)
    {
        var type = Types.First(p => p.Kind == candidate.Kind);
        try
        {
            var project = type.Describe(candidate, context);
            return context.Settings.TellsClaude(type.Kind) ? project : project with { SystemPromptNote = null };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or FormatException)
        {
            return new ProjectInfo
            {
                Kind = candidate.Kind,
                KindName = type.Name,
                Name = candidate.Name,
                Root = Path.GetDirectoryName(candidate.Path) ?? candidate.Path,
                ProjectPath = candidate.Path,
                Problem = $"Couldn't read the project: {ex.Message}",
            };
        }
    }
}
