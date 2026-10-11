using System.Text.Json.Nodes;
using Claudette.Core.Docs;

namespace Claudette.Core.Tests.Docs;

/// <summary>
/// What Claudette reads from calls to claude.ai's Docs connector (DESIGN.md §5, "Claude Docs"). The inputs and replies
/// are shaped as the connector sent them to Claude Code 2.1.284, cut down, with made-up ids and addresses.
/// </summary>
public class ClaudeDocCallTests
{
    private const string Batch = "mcp__claude_ai_Claude_Docs__batch";
    private const string Update = "mcp__claude_ai_Claude_Docs__update";
    private const string Create = "mcp__claude_ai_Claude_Docs__create";
    private const string Url = "https://claude.ai/code/artifact/0b1c2d3e-0000-4000-8000-000000000001";

    private static JsonObject Input(string json) => JsonNode.Parse(json)!.AsObject();

    private const string MakeInput = """
        {"container":{"kind":"project","create":{"name":"Enemies: your task list","doc":{"blocks":{"s1":{"type":"pending","intent":"Before you start"}},"markdown":"# Enemies: your task list\n\n<?claude block s1?>"}}},"batch":[]}
        """;

    // The reply's first line is its JSON; what the model is to do next follows it.
    private const string MadeReply = $$$"""
        {"verdict":"allow","acks":[{"verdict":"allow","minted":"a38e"}],"created":{"verdict":"allow","minted":"0b1c2d3e-0000-4000-8000-000000000001","bound":true},"frame":{"slug":"0b1c2d3e-0000-4000-8000-000000000001","url":"{{{Url}}}","artifactUrl":"{{{Url}}}"}}
        The doc's viewer is at {{{Url}}} — NOT open for the user yet. Open it NOW, before you fill any section.
        """;

    [Fact]
    public void A_batch_that_makes_a_doc_gives_its_id_address_and_title()
    {
        var call = ClaudeDocCall.From(Batch, Input(MakeInput), MadeReply);

        Assert.Equal(new ClaudeDocCall("0b1c2d3e-0000-4000-8000-000000000001", Url, "Enemies: your task list", ClaudeDocChange.Created), call);
    }

    [Fact]
    public void An_edit_names_its_doc_by_the_container_and_has_no_address()
    {
        var call = ClaudeDocCall.From(Update, Input("""
            {"ref":{"object":"node","id":"57f7"},"engine":"prose","container":{"kind":"project","id":"0b1c2d3e-0000-4000-8000-000000000001"},"payload":{"ops":[]}}
            """), """{"verdict":"allow","rev":2,"session":"my76","xml":"<doc/>"}""");

        Assert.Equal(new ClaudeDocCall("0b1c2d3e-0000-4000-8000-000000000001", null, null, ClaudeDocChange.Edited), call);
    }

    [Fact]
    public void A_comment_is_a_created_utterance_and_its_reply_says_where_the_doc_is()
    {
        var call = ClaudeDocCall.From(Create, Input("""
            {"object":"utterance","container":{"kind":"project","id":"0b1c2d3e-0000-4000-8000-000000000001"},"payload":{"value":{"body":"Should I add the cheat now?"}}}
            """), $$$"""{"verdict":"allow","minted":"4653","frame":{"slug":"0b1c2d3e-0000-4000-8000-000000000001","url":"{{{Url}}}","artifactUrl":"{{{Url}}}"}}""");

        Assert.Equal(new ClaudeDocCall("0b1c2d3e-0000-4000-8000-000000000001", Url, null, ClaudeDocChange.Commented), call);
    }

    [Fact]
    public void Calls_that_name_no_doc_or_only_read_one_are_not_doc_changes()
    {
        Assert.Null(ClaudeDocCall.From("mcp__claude_ai_Claude_Docs__guide", Input("""{"items":["topic.index"]}"""), "# topic.index"));
        Assert.Null(ClaudeDocCall.From("mcp__claude_ai_Claude_Docs__read", Input("""{"container":{"kind":"project","id":"d1"}}"""), """{"verdict":"allow"}"""));
        // Another server's tool, however its input looks.
        Assert.Null(ClaudeDocCall.From("mcp__notes__batch", Input(MakeInput), MadeReply));
    }

    [Fact]
    public void A_refused_call_changes_nothing()
    {
        Assert.Null(ClaudeDocCall.From(Batch, Input(MakeInput), """{"verdict":"refuse","code":"quota"}"""));
        Assert.Null(ClaudeDocCall.From(Batch, Input(MakeInput), """{"verdict":"allow","created":{"verdict":"refuse"}}"""));
    }

    [Fact]
    public void Only_a_web_address_is_where_a_doc_opens()
    {
        var call = ClaudeDocCall.From(Batch, Input(MakeInput), """
            {"verdict":"allow","created":{"verdict":"allow","minted":"d1"},"frame":{"slug":"d1","url":"file:///etc/passwd","artifactUrl":"javascript:alert(1)"}}
            """);

        Assert.Equal(new ClaudeDocCall("d1", null, "Enemies: your task list", ClaudeDocChange.Created), call);
    }

    [Fact]
    public void A_reply_that_isnt_json_still_names_an_edits_doc_but_not_a_new_one()
    {
        Assert.Equal(ClaudeDocChange.Edited, ClaudeDocCall.From(Update, Input("""{"container":{"kind":"project","id":"d1"}}"""), "Done.")?.Change);
        Assert.Null(ClaudeDocCall.From(Batch, Input(MakeInput), "Done."));
    }

    [Fact]
    public void A_reply_written_over_several_lines_is_read_whole()
    {
        var call = ClaudeDocCall.From(Batch, Input(MakeInput), $$"""
            {
              "verdict": "allow",
              "frame": { "slug": "d1", "artifactUrl": "{{Url}}" }
            }
            """);

        Assert.Equal(("d1", Url), (call?.DocId, call?.Url));
    }
}
