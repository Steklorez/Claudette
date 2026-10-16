using System.Text.Json;
using Claudette.Core.Json;
using Claudette.Core.Protocol;
using Claudette.Core.Sessions;

namespace Claudette.Core.Transcripts;

/// <summary>
/// The running totals Claude Code saves in a session's transcript when its process exits normally (a <c>cost-state</c>
/// entry, undocumented), and starts a later process from when it resumes or forks the session. Claudette reads them so a
/// resumed tab's first turn counts only what that turn spent (DESIGN.md §6, "Usage history").
/// </summary>
public static class CostState
{
    private const string EntryType = "cost-state";

    /// <summary>Every <c>cost-state</c> entry's totals, oldest first; none when there are none or the file can't be read.</summary>
    /// <remarks>Off the caller's thread, line by line: a long session's transcript can be tens of MB.</remarks>
    public static Task<IReadOnlyList<ModelUsageTotals>> ReadAllAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<ModelUsageTotals>>(() =>
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
                return Read(Files.Utf8Lines.Read(stream, 0, cancellationToken).Select(line => line.Text));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }, cancellationToken);

    public static IReadOnlyList<ModelUsageTotals> Read(IEnumerable<string> lines)
    {
        var totals = new List<ModelUsageTotals>();
        foreach (var line in lines)
        {
            // Most lines are conversation: only parse the ones that could be an entry of this type.
            if (!line.Contains(EntryType, StringComparison.Ordinal))
            {
                continue;
            }
            try
            {
                if (JsonTree.ParseObject(line) is { } entry && entry.GetString("type") == EntryType)
                {
                    totals.Add(ModelUsageTotals.From(entry.GetObject("modelUsage")));
                }
            }
            catch (JsonException)
            {
                // A line cut short or garbled: skip it.
            }
        }
        return totals;
    }
}
