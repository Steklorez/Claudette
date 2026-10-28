using Claudette.Core.Diffs;
using Claudette.Core.ProjectFiles;

namespace Claudette.App.Services;

/// <summary>
/// What Replace in files does before writing files no editor has open (DESIGN.md §18, "Finding in files"): in a Perforce
/// workspace, the depot files among them are checked out first, as Settings → Perforce → <b>Check out files before
/// they're changed</b> says, in one <c>p4 edit</c>. A file Perforce couldn't check out, and that's still read-only, is
/// left as it is, with why.
/// </summary>
internal sealed class PerforceBeforeFilesWrite(PerforceFilesService perforce, PerforceContext context) : IBeforeFilesWrite
{
    public async Task<IReadOnlyDictionary<string, string>> PrepareAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var outcome = await perforce.CheckOutBeforeWritingAsync(context, paths, cancellationToken).ConfigureAwait(false);
        var refused = new Dictionary<string, string>(ChangedFiles.PlatformPathComparer);
        if (!outcome.Succeeded)
        {
            foreach (var path in paths.Where(p => !TextFile.IsWritable(p)))
            {
                refused[path] = $"Perforce couldn't check it out: {outcome.Problem}";
            }
        }
        return refused;
    }
}
