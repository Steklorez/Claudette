using System.Text.Json.Nodes;
using Deventry.Core.Codex;
using Microsoft.Extensions.Time.Testing;

namespace Deventry.Core.Tests.Codex;

/// <summary>Deventry's hook for a Codex tab's patches (DESIGN.md §18, "Perforce"): what it reads, and its way to the tab.</summary>
public sealed class CodexHooksTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void A_patch_names_the_files_it_adds_changes_moves_and_deletes()
    {
        var files = CodexHooks.Files("""
            *** Begin Patch
            *** Add File: docs/new.md
            +hello
            *** Update File: src/Player.cpp
            *** Move to: src/Hero.cpp
            @@
            -old
            +new
            *** Delete File: old.txt
            *** End Patch
            """);

        Assert.Equal(
            [new CodexPatchFile("docs/new.md", CodexPatchChange.Add, null), new CodexPatchFile("src/Player.cpp", CodexPatchChange.Update, "src/Hero.cpp"), new CodexPatchFile("old.txt", CodexPatchChange.Delete, null)],
            files);
    }

    [Fact]
    public void A_hook_calls_files_are_full_paths_from_its_folder_and_another_tools_are_none()
    {
        var folder = Path.Combine(Path.GetTempPath(), "work");
        var input = new JsonObject
        {
            ["tool_name"] = CodexHooks.PatchTool,
            ["cwd"] = folder,
            ["tool_input"] = new JsonObject { ["command"] = "*** Begin Patch\n*** Update File: src/Player.cpp\n@@\n-a\n+b\n*** End Patch\n" },
        };

        Assert.Equal(Path.Combine(folder, "src", "Player.cpp"), Assert.Single(CodexHooks.FilesOf(input)).Path);
        Assert.Empty(CodexHooks.FilesOf(new JsonObject { ["tool_name"] = "Bash", ["tool_input"] = new JsonObject { ["command"] = "ls" } }));
    }

    [Fact]
    public void Its_command_and_configuration_are_the_same_for_every_tab()
    {
        var command = CodexHooks.Command(@"C:\Program Files\Deventry\Deventry.exe");

        Assert.Equal("\"C:\\Program Files\\Deventry\\Deventry.exe\" --codex-hook", command);
        Assert.Equal("hooks.PreToolUse=[{matcher=\"apply_patch\",hooks=[{type=\"command\",command=\"\\\"C:\\\\Program Files\\\\Deventry\\\\Deventry.exe\\\" --codex-hook\",timeout=30}]}]",
            CodexHooks.Config(command));
        Assert.True(CodexHooks.IsHookLaunch(["--codex-hook"]));
        Assert.Throws<ArgumentException>(() => CodexHooks.Command("C:\\a\"b\\Deventry.exe"));
    }

    [Fact]
    public async Task The_helper_passes_the_call_to_the_tab_and_gives_back_its_answer()
    {
        JsonObject? asked = null;
        await using var pipe = new CodexHookPipe((input, _) =>
        {
            asked = input;
            return Task.FromResult(new JsonObject { ["decision"] = "ok" });
        });

        // Over several lines, as Codex may write it.
        var answer = await CodexHooks.RelayAsync("{\n  \"tool_name\": \"apply_patch\"\n}", pipe.Name, TimeProvider.System, Token);

        Assert.Equal("""{"decision":"ok"}""", answer);
        Assert.Equal("apply_patch", asked!["tool_name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Its_pipe_fits_in_a_macos_socket_path()
    {
        await using var pipe = new CodexHookPipe((_, _) => Task.FromResult(new JsonObject()));

        // A pipe on macOS is a socket in its TMPDIR, whose path can be 104 characters at most.
        Assert.InRange($"/var/folders/q2/p1t6rgxj5fj32g1dc7dmd49r0000gn/T/CoreFxPipe_{pipe.Name}".Length, 1, 104);
    }

    [Fact]
    public async Task Without_a_tab_to_ask_the_helper_lets_the_patch_go_on()
    {
        Assert.Equal(CodexHooks.Continue, await CodexHooks.RelayAsync("{}", null, TimeProvider.System, Token));

        var time = new FakeTimeProvider();
        var relay = CodexHooks.RelayAsync("{}", $"deventry-codex-{Guid.NewGuid().ToString("N")[..16]}", time, Token);
        // Its deadline is set before it first waits, so the clock can move at once.
        time.Advance(CodexHooks.Timeout);

        Assert.Equal(CodexHooks.Continue, await relay.WaitAsync(Waiting.Timeout, TimeProvider.System, Token));
    }
}
