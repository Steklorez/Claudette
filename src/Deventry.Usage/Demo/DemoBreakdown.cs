using Deventry.Core.Sessions;
using Deventry.Core.Settings;

namespace Deventry.Usage.Demo;

/// <summary>
/// The demo's token breakdown (DESIGN.md §18, "Token breakdown"): the grappling hook's tab's turns with their calls, its
/// tools' results, an Explore subagent on Haiku and a costlier one on Opus, a turn sent after the cache expired and one
/// that compacted first, scaled to the tokens the tab has in the window, so the busiest tabs stay as they were.
/// </summary>
internal static class DemoBreakdown
{
    public const string Tab = "tab-grapple";

    private const string Opus = "claude-opus-5-5";
    private const string Haiku = "claude-haiku-5-5";

    /// <summary>Fresh input's list price, US dollars per token, by model.</summary>
    private static readonly Dictionary<string, double> InputPrice = new(StringComparer.Ordinal) { [Opus] = 5e-6, [Haiku] = 1e-6 };

    /// <summary>The tab's turns, from <paramref name="start"/>, as about <paramref name="tokens"/> tokens in all.</summary>
    public static void Write(UsageStore store, TabState tab, DateTimeOffset start, long tokens)
    {
        var demo = new Builder(tab);
        demo.Turn("demo-grapple-1", start.AddMinutes(20), null, t => t
            .Main(400, "Grep", 900)
            .Main(300, "Read", 7_400)
            .Main(350, "Agent", 410, subagent: ("Explore", Haiku, 4, 2_600))
            .Main(500, "Read", 5_100)
            .Main(900));
        demo.Turn("demo-grapple-2", start.AddMinutes(65), TimeSpan.FromMinutes(42), t => t
            .Main(600, "Write", 700)
            .Main(500, "Write", 600)
            .Main(450, "Edit", 500)
            .Main(400, "Bash", 9_200)
            .Main(300, "Bash", 3_100)
            .Main(1_100));
        demo.Turn("demo-grapple-3", start.AddMinutes(150), TimeSpan.FromMinutes(83), t => t
            .Expired(300, "Agent", 312, subagent: ("Explore", Opus, 9, 4_600))
            .Main(400, "Read", 6_200)
            .Main(350, "Edit", 600)
            .Main(1_000));
        demo.Turn("demo-grapple-4", start.AddMinutes(185), TimeSpan.FromMinutes(31), t => t
            .Compact()
            .Main(500, "Bash", 2_400)
            .Main(800));
        demo.Save(store, tokens);
    }

    private sealed class Builder(TabState tab)
    {
        private readonly List<(string Turn, DateTimeOffset At, TimeSpan? Idle, List<Call> Calls, List<(int Seq, string Tool, long Tokens, string? Use, DateTimeOffset At)> Steps, DateTimeOffset? Compacted)> _turns = [];
        private long _cached;
        private long _pending = 14_000;

        public void Turn(string id, DateTimeOffset at, TimeSpan? idle, Func<TurnBuilder, TurnBuilder> calls)
        {
            var turn = new TurnBuilder(this, at);
            calls(turn);
            _turns.Add((id, at, idle, turn.Calls, turn.Steps, turn.CompactedAt));
        }

        /// <summary>Writes the turns, every count scaled so they come to about <paramref name="tokens"/>.</summary>
        public void Save(UsageStore store, long tokens)
        {
            var total = _turns.Sum(t => t.Calls.Sum(c => c.Total));
            var scale = total > 0 ? (double)tokens / total : 1;
            foreach (var (turn, at, idle, calls, steps, compacted) in _turns)
            {
                var records = calls.Select((c, i) => Record(turn, i + 1, c, scale)).ToList();
                var end = records.Max(c => c.Timestamp).AddSeconds(5);
                var rows = records.GroupBy(c => c.Model).Select(g => new TurnRecord(end, tab.Id, tab.SessionId, g.Key, g.Sum(c => c.Input), g.Sum(c => c.Output),
                    g.Sum(c => c.CacheWrite), g.Sum(c => c.CacheRead), g.Sum(c => c.CostUsd), tab.Folder) { Turn = turn, IdleBefore = idle }).ToList();
                var stepRecords = steps.Select(s => new StepRecord(turn, s.Seq, null, s.Tool, Scale(s.Tokens, scale), false, s.At) { ToolUseId = s.Use }).ToList();
                UsageEvent[] events = compacted is { } when ? [new UsageEvent(tab.Id, tab.SessionId, turn, when, UsageEventKind.Compacted, Scale(_compactedFrom, scale), "auto")] : [];
                store.AddTurns(rows, new TurnDetail(records, stepRecords, events));
            }
        }

        private long _compactedFrom;

        private CallRecord Record(string turn, int seq, Call c, double scale)
        {
            var input = Scale(c.Input, scale);
            var output = Scale(c.Output, scale);
            var read = Scale(c.Read, scale);
            var write = Scale(c.Write, scale);
            var hour = c.Agent is null;
            var cost = InputPrice[c.Model] * (input + (read * 0.1) + (write * (hour ? 2 : 1.25)) + (output * 5));
            return new CallRecord(turn, tab.Id, tab.SessionId, c.At, seq, c.Kind == CallKind.Compaction ? null : $"msg_{turn}_{seq}", c.Agent, c.AgentType, c.Model,
                input, output, read, hour ? 0 : write, hour ? write : 0, 0, 0, c.Kind, cost);
        }

        private static long Scale(long tokens, double scale) => Math.Max(tokens > 0 ? 1 : 0, (long)Math.Round(tokens * scale));

        public sealed class TurnBuilder(Builder demo, DateTimeOffset at)
        {
            private DateTimeOffset _at = at;

            public List<Call> Calls { get; } = [];

            public List<(int Seq, string Tool, long Tokens, string? Use, DateTimeOffset At)> Steps { get; } = [];

            public DateTimeOffset? CompactedAt { get; private set; }

            /// <summary>A call of the main agent's, which reads what's cached and writes what's new, and the tool it asks for.</summary>
            public TurnBuilder Main(long output, string? tool = null, long result = 0, (string Type, string Model, int Calls, long Grow)? subagent = null) =>
                Add(output, tool, result, subagent, expired: false);

            /// <summary>The turn's first call, after the cache expired: it writes the whole conversation again.</summary>
            public TurnBuilder Expired(long output, string? tool = null, long result = 0, (string Type, string Model, int Calls, long Grow)? subagent = null) =>
                Add(output, tool, result, subagent, expired: true);

            /// <summary>Claude Code compacting the conversation first: one call that reads it all and writes its summary.</summary>
            public TurnBuilder Compact()
            {
                CompactedAt = _at;
                demo._compactedFrom = demo._cached + demo._pending;
                Calls.Add(new Call(null, null, Opus, 8, 4_200, demo._cached, demo._pending, CallKind.Compaction, _at));
                demo._cached = 0;
                demo._pending = 14_000 + 4_200;
                _at = _at.AddSeconds(20);
                return this;
            }

            private TurnBuilder Add(long output, string? tool, long result, (string Type, string Model, int Calls, long Grow)? subagent, bool expired)
            {
                var read = expired ? 0 : demo._cached;
                var write = expired ? demo._cached + demo._pending : demo._pending;
                Calls.Add(new Call(null, null, Opus, 4, output, read, write, expired ? CallKind.CacheExpired : CallKind.Call, _at));
                demo._cached += demo._pending;
                demo._pending = output;
                var seq = Calls.Count;
                _at = _at.AddSeconds(6);
                if (subagent is { } agent)
                {
                    var use = $"toolu_demo_{seq}_{Calls.Count}";
                    long cached = 0, pending = 12_000;
                    for (var i = 0; i < agent.Calls; i++)
                    {
                        var said = i == agent.Calls - 1 ? result : 260;
                        Calls.Add(new Call(use, agent.Type, agent.Model, 5, said, cached, pending, CallKind.Call, _at));
                        cached += pending;
                        pending = said + agent.Grow;
                        _at = _at.AddSeconds(5);
                    }
                    Steps.Add((seq, tool ?? "Agent", result, use, _at));
                }
                else if (tool is not null)
                {
                    Steps.Add((seq, tool, result, $"toolu_demo_{seq}", _at.AddSeconds(-3)));
                }
                demo._pending += result;
                return this;
            }
        }
    }

    private sealed record Call(string? Agent, string? AgentType, string Model, long Input, long Output, long Read, long Write, CallKind Kind, DateTimeOffset At)
    {
        public long Total => Input + Output + Read + Write;
    }
}
