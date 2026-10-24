using System.Collections.ObjectModel;
using Claudette.App.Conversation;
using Claudette.Core.Protocol;
using Claudette.Core.Sessions;

namespace Claudette.App.Tests;

/// <summary>Saving a conversation as Markdown or HTML (DESIGN.md §5, "Export").</summary>
public class ConversationExportTests
{
    private readonly ObservableCollection<ConversationItem> _items = [];
    private readonly ConversationBuilder _builder;

    public ConversationExportTests() => _builder = new ConversationBuilder(_items);

    private void Apply(string line)
    {
        Assert.True(MessageParser.TryParse(line, out var message, out var error), error);
        _builder.Apply(message switch
        {
            AssistantMessage a => new AssistantMessageReceived(a),
            UserMessage u => new ToolResultsReceived(u),
            _ => throw new InvalidOperationException(),
        });
    }

    private void Conversation()
    {
        _builder.AddUserMessage("Run the ```tests``` please");
        Apply("""{"type":"assistant","message":{"content":[{"type":"thinking","thinking":"secret plan"},{"type":"text","text":"Running **them** now. <script>alert(1)</script>"}]}}""");
        Apply("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t1","name":"Bash","input":{"command":"dotnet test","description":"Run tests"}}]}}""");
        Apply("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t1","content":"Passed! 12 tests","is_error":false}]}}""");
        Apply("""{"type":"assistant","message":{"content":[{"type":"tool_use","id":"t2","name":"Read","input":{"file_path":"src/app.cs"}}]}}""");
        Apply("""{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"t2","content":"x","is_error":true}]}}""");
    }

    [Fact]
    public void A_call_that_made_a_Claude_Doc_keeps_the_docs_link()
    {
        // As claude.ai's Docs connector answered through Claude Code 2.1.284, cut down (DESIGN.md §5, "Claude Docs").
        var items = new ObservableCollection<ConversationItem>();
        var builder = new ConversationBuilder(items, new TodoList());
        foreach (var line in new[]
        {
            """{"type":"assistant","message":{"content":[{"type":"tool_use","id":"c1","name":"mcp__claude_ai_Claude_Docs__batch","input":{"container":{"kind":"project","create":{"name":"Enemies"}}}}]}}""",
            """{"type":"user","message":{"role":"user","content":[{"type":"tool_result","tool_use_id":"c1","content":"{\"verdict\":\"allow\",\"frame\":{\"slug\":\"d1\",\"artifactUrl\":\"https://claude.ai/code/artifact/d1\"}}\nOpen it NOW."}]}}""",
        })
        {
            Assert.True(MessageParser.TryParse(line, out var message, out var error), error);
            builder.Apply(message is AssistantMessage a ? new AssistantMessageReceived(a) : new ToolResultsReceived((UserMessage)message));
        }

        var md = ConversationExport.ToMarkdown("Docs", items);
        var html = ConversationExport.ToHtml("Docs", items);

        Assert.Contains(" → Created “Enemies”: https://claude.ai/code/artifact/d1", md, StringComparison.Ordinal);
        Assert.DoesNotContain("``", md, StringComparison.Ordinal);
        Assert.DoesNotContain("verdict", md, StringComparison.Ordinal);
        Assert.Contains("https://claude.ai/code/artifact/d1", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_forms_use_one_kind_of_line_ending_on_every_OS()
    {
        _builder.AddUserMessage("first line\r\nsecond line");
        Apply("""{"type":"assistant","message":{"content":[{"type":"text","text":"one\r\ntwo"}]}}""");

        var md = ConversationExport.ToMarkdown("Endings", _items);
        var html = ConversationExport.ToHtml("Endings", _items);

        Assert.DoesNotContain('\r', md);
        Assert.DoesNotContain('\r', html);
        Assert.Contains("one\ntwo", md, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_has_the_prompts_replies_and_one_line_per_tool()
    {
        Conversation();

        var md = ConversationExport.ToMarkdown("Fix the build", _items, "/work/api");

        Assert.StartsWith("# Fix the build\n\n`/work/api`\n", md, StringComparison.Ordinal);
        // A fence the prompt's own backticks can't close.
        Assert.Contains("````text\nRun the ```tests``` please\n````", md, StringComparison.Ordinal);
        Assert.Contains("**Claude**\n\nRunning **them** now.", md, StringComparison.Ordinal);
        Assert.Contains("- **Bash** `dotnet test` → Passed! 12 tests", md, StringComparison.Ordinal);
        Assert.Contains("```text\nPassed! 12 tests\n```", md, StringComparison.Ordinal);
        Assert.Contains("- **Read** `src/app.cs` (failed)", md, StringComparison.Ordinal);
        Assert.DoesNotContain("secret plan", md, StringComparison.Ordinal);

        Assert.Contains("> secret plan", ConversationExport.ToMarkdown("t", _items, options: new(IncludeThinking: true)), StringComparison.Ordinal);
    }

    [Fact]
    public void Html_is_one_file_where_nothing_from_the_conversation_runs()
    {
        Conversation();

        var html = ConversationExport.ToHtml("Fix <the> build", _items, "/work/api");

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("<title>Fix &lt;the&gt; build</title>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("<b>Bash</b> <code>dotnet test</code>", html, StringComparison.Ordinal);
        Assert.Contains("<pre>Passed! 12 tests</pre>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http", html.Replace("http-equiv", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
