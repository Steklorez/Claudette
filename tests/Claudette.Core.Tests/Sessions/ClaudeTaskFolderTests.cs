using System.Text.Json.Nodes;
using Claudette.Core.Sessions;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.Sessions;

/// <summary>The task list Claude Code keeps in its tasks folder (DESIGN.md §5, "Tasks").</summary>
public sealed class ClaudeTaskFolderTests : IDisposable
{
    private readonly TempFolder _config = new("claudette-task-folder");

    public void Dispose() => _config.Dispose();

    // A task as Claude Code 2.1.301 writes it.
    private const string Written = """
        {
          "id": "17",
          "subject": "Review and submit the change",
          "description": "The docs and the screenshot.",
          "activeForm": "Reviewing the change",
          "owner": "reviewer",
          "status": "in_progress",
          "blocks": [],
          "blockedBy": ["16"]
        }
        """;

    private string List => ClaudeTaskFolder.Folder(_config.Path, "c7de3b80-899d-4b05-b38e-1a25f7cabbdc");

    [Fact]
    public void A_sessions_list_is_named_for_it_unless_the_variable_names_another()
    {
        Assert.Equal("s1", ClaudeTaskFolder.ListId("s1", null));
        Assert.Equal("s1", ClaudeTaskFolder.ListId("s1", ""));
        Assert.Equal("my-project", ClaudeTaskFolder.ListId("s1", "my-project"));
    }

    [Fact]
    public void A_lists_folder_has_its_name_made_safe_as_Claude_Code_makes_it() =>
        Assert.Equal(Path.Combine(_config.Path, "tasks", "team-a-b_c-1"), ClaudeTaskFolder.Folder(_config.Path, "team a/b_c-1"));

    [Fact]
    public void A_file_gives_the_task_with_its_number_state_and_blockers()
    {
        _config.Write("tasks/c7de3b80-899d-4b05-b38e-1a25f7cabbdc/17.json", Written);

        var task = Assert.Single(ClaudeTaskFolder.Read(List)!);

        Assert.Equal("17", task.Id);
        Assert.Equal("Review and submit the change", task.Subject);
        Assert.Equal("The docs and the screenshot.", task.Description);
        Assert.Equal("Reviewing the change", task.ActiveForm);
        Assert.Equal("reviewer", task.Owner);
        Assert.Equal("in_progress", task.Status);
        Assert.Equal(["16"], task.BlockedBy);
    }

    [Fact]
    public void The_tasks_come_by_number_without_Claude_Codes_own_files_or_tasks()
    {
        _config.Write("tasks/c7de3b80-899d-4b05-b38e-1a25f7cabbdc/10.json", """{"id":"10","subject":"Ten","status":"pending","blocks":[],"blockedBy":[]}""");
        _config.Write("tasks/c7de3b80-899d-4b05-b38e-1a25f7cabbdc/9.json", """{"id":"9","subject":"Nine","status":"completed","blocks":[],"blockedBy":[]}""");
        _config.Write("tasks/c7de3b80-899d-4b05-b38e-1a25f7cabbdc/11.json", """{"id":"11","subject":"Its own","status":"pending","blocks":[],"blockedBy":[],"metadata":{"_internal":true}}""");
        _config.Write("tasks/c7de3b80-899d-4b05-b38e-1a25f7cabbdc/.highwatermark", "8");
        _config.Write("tasks/c7de3b80-899d-4b05-b38e-1a25f7cabbdc/.lock", "");

        Assert.Equal(["9", "10"], ClaudeTaskFolder.Read(List)!.Select(t => t.Id));
    }

    [Fact]
    public void A_folder_thats_not_there_or_has_a_file_half_written_gives_no_list()
    {
        Assert.Null(ClaudeTaskFolder.Read(List));

        _config.Write("tasks/c7de3b80-899d-4b05-b38e-1a25f7cabbdc/1.json", """{"id":"1","subject":"One","status":"pending"}""");
        _config.Write("tasks/c7de3b80-899d-4b05-b38e-1a25f7cabbdc/2.json", """{"id":"2","subj""");

        Assert.Null(ClaudeTaskFolder.Read(List));
    }

    [Fact]
    public void An_empty_folder_is_an_empty_list()
    {
        _config.CreateFolder("tasks/c7de3b80-899d-4b05-b38e-1a25f7cabbdc");

        Assert.Empty(ClaudeTaskFolder.Read(List)!);
    }

    [Fact]
    public void A_task_list_result_has_the_same_fields()
    {
        var listed = (JsonNode.Parse("""{"id":3,"subject":"Fix it","status":"pending","owner":"helper","blockedBy":[1,"2"]}""") as JsonObject)!;

        var task = ClaudeTask.From(listed)!;

        Assert.Equal(("3", "Fix it", "helper", "pending"), (task.Id, task.Subject, task.Owner, task.Status));
        Assert.Equal(["1", "2"], task.BlockedBy);
        Assert.Null(ClaudeTask.From((JsonNode.Parse("""{"subject":"No number"}""") as JsonObject)!));
    }

    [Fact]
    public void The_variable_comes_from_the_most_specific_settings_file_that_sets_it_then_the_environment()
    {
        var local = _config.Write("project/.claude/settings.local.json", """{"env":{}}""");
        var shared = _config.Write("project/.claude/settings.json", """{"env":{"CLAUDE_CODE_TASK_LIST_ID":"from-the-project"}}""");
        var user = _config.Write("settings.json", """{"env":{"CLAUDE_CODE_TASK_LIST_ID":"from-the-user"}}""");
        var environment = new Dictionary<string, string> { ["CLAUDE_CODE_TASK_LIST_ID"] = "from-the-shell" };

        Assert.Equal("from-the-project", ClaudeTaskFolder.ListVariableFor(environment, [local, shared, user]));
        Assert.Equal("from-the-shell", ClaudeTaskFolder.ListVariableFor(environment, [local]));
        Assert.Null(ClaudeTaskFolder.ListVariableFor(new Dictionary<string, string>(), [local, _config.Combine("missing.json")]));

        // An empty value unsets it.
        var unset = _config.Write("unset.json", """{"env":{"CLAUDE_CODE_TASK_LIST_ID":""}}""");
        Assert.Null(ClaudeTaskFolder.ListVariableFor(environment, [unset]));
    }
}
