using Claudette.App.Tests.Support;
using Claudette.App.ViewModels;
using Claudette.Core.Development;

namespace Claudette.App.Tests;

/// <summary>
/// A source build's new commits upstream (DESIGN.md §9, "New commits upstream"): the badge and its dialog, pulling and
/// building them, and the restart into the build. Git and <c>dotnet</c> are played by the fake launcher.
/// </summary>
public class SourceUpdateTests
{
    [Fact]
    public async Task New_commits_upstream_are_offered_with_their_subjects()
    {
        var launcher = new FakeLauncher();
        await using var h = new TabTestHarness(launcher: launcher);
        using var source = await FakeCheckout.StartAsync(h, launcher);
        var badge = source.Badge;

        Assert.True(badge.HasBadge);
        Assert.Equal("3 new commits on origin/main", badge.BadgeText);
        Assert.Equal("3 new commits on origin/main", badge.Title);
        Assert.Equal(["Commit 3", "Commit 2", "Commit 1"], badge.Commits.Select(c => c.Subject));
        Assert.StartsWith("Ada · ", badge.Commits[0].Detail, StringComparison.Ordinal);
        Assert.Equal("3333333", badge.Commits[0].Hash);
        Assert.Null(badge.MoreText);
        Assert.True(badge.CanUpdate);
        Assert.False(badge.CanUpdateWhenIdle);
        Assert.True(badge.CanSkip);
        Assert.Null(badge.WhyNotText);
        Assert.Equal("3 new commits on origin/main.", badge.CheckStatusText);
        Assert.Contains("fetch --quiet", source.Checkout.Commands);
    }

    [Fact]
    public async Task More_commits_than_are_listed_say_how_many_more()
    {
        var launcher = new FakeLauncher();
        await using var h = new TabTestHarness(launcher: launcher);
        using var source = await FakeCheckout.StartAsync(h, launcher, configure: c => c.Behind = 32);

        Assert.Equal(SourceCheckout.MaxCommits, source.Badge.Commits.Count);
        Assert.Equal("and 12 more", source.Badge.MoreText);
    }

    [Fact]
    public async Task Pull_build_and_restart_fast_forwards_builds_and_hands_every_tab_to_the_new_build()
    {
        var launcher = new FakeLauncher();
        await using var h = new TabTestHarness(launcher: launcher);
        using var source = await FakeCheckout.StartAsync(h, launcher);

        await source.Badge.UpdateCommand.ExecuteAsync(null);

        await TabTestHarness.Eventually(() => source.Host.Exited, "the restart into the new build");
        Assert.Contains("merge --ff-only --quiet @{upstream}", source.Checkout.Commands);
        var build = Assert.Single(launcher.Started, s => s.Arguments is ["build", ..]);
        Assert.Equal(["build", Path.Combine(h.Root, "repo", "src", "Claudette.App"), "-c", "Debug"], build.Arguments.Take(4));
        Assert.Equal(Path.Combine(h.Root, "repo"), build.WorkingDirectory);
        Assert.Null(source.Updates.LastError);
    }

    [Fact]
    public async Task Pull_and_build_leaves_the_build_to_be_offered()
    {
        var launcher = new FakeLauncher();
        await using var h = new TabTestHarness(launcher: launcher);
        using var source = await FakeCheckout.StartAsync(h, launcher);
        using var newBuild = new NewBuildViewModel(source.Restarts);

        await source.Badge.PullAndBuildCommand.ExecuteAsync(null);
        source.Restarts.CheckNow();
        source.Restarts.CheckNow();

        Assert.False(source.Host.Exited);
        Assert.Equal(FakeCheckout.Rebuilt, source.Restarts.ReadyStamp);
        Assert.Equal("New build ready", newBuild.BadgeText);
        await source.Updates.LastCheck!;
        Assert.False(source.Badge.HasBadge);
        Assert.StartsWith("Up to date with origin/main.", source.Badge.CheckStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_when_idle_builds_now_and_restarts_once_the_tab_is_done()
    {
        var launcher = new FakeLauncher();
        await using var h = new TabTestHarness(launcher: launcher);
        var tab = await h.OpenTabAsync();
        tab.ComposerText = "long job";
        await tab.SendCommand.ExecuteAsync(null);
        h.Transport.Emit("""{"type":"system","subtype":"init","session_id":"s1","model":"claude-opus-5-5"}""");
        // What waiting for idle reads: the tab's status, which follows the session's a moment after it's in the turn.
        await TabTestHarness.Eventually(() => h.Shell.AnyTabWorking, "the tab working");
        using var source = await FakeCheckout.StartAsync(h, launcher);
        Assert.True(source.Badge.CanUpdateWhenIdle);
        Assert.NotNull(source.Badge.WorkingText);

        await source.Badge.UpdateWhenIdleCommand.ExecuteAsync(null);

        Assert.True(source.Restarts.IsWaitingForIdle);
        Assert.False(source.Host.Exited);
        h.Transport.EmitTurn();
        await TabTestHarness.Eventually(() => source.Host.Exited, "the restart once the turn ended");
    }

    [Fact]
    public async Task The_checkouts_own_work_keeps_it_from_pulling_and_says_why()
    {
        var launcher = new FakeLauncher();
        await using var h = new TabTestHarness(launcher: launcher);
        using var source = await FakeCheckout.StartAsync(h, launcher, configure: c => c.HasChanges = true);
        var badge = source.Badge;

        Assert.True(badge.HasBadge);
        Assert.Equal(3, badge.Commits.Count);
        Assert.Equal("The checkout has uncommitted changes. Commit or stash them to pull.", badge.WhyNotText);
        Assert.False(badge.CanUpdate);
        Assert.False(badge.CanPullOnly);
        Assert.True(badge.CanSkip);

        source.Checkout.HasChanges = false;
        source.Checkout.Ahead = 2;
        await source.Updates.CheckNowAsync();
        Assert.Equal("The checkout has 2 commits that origin/main doesn't. Pull it yourself.", badge.WhyNotText);
        Assert.DoesNotContain(source.Checkout.Commands, c => c.StartsWith("merge", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_build_shows_its_last_lines_and_can_be_built_again()
    {
        var launcher = new FakeLauncher();
        await using var h = new TabTestHarness(launcher: launcher);
        using var source = await FakeCheckout.StartAsync(h, launcher, configure: c => c.BuildSucceeds = false);
        var badge = source.Badge;

        await badge.UpdateCommand.ExecuteAsync(null);

        Assert.False(source.Host.Exited);
        Assert.True(badge.HasBadge);
        Assert.Equal("Build failed", badge.BadgeText);
        Assert.Equal("The build failed (exit code 1).", badge.ErrorText);
        Assert.Contains("error CS1002: ; expected", badge.BuildOutput);
        Assert.True(badge.CanBuildAgain);
        Assert.False(badge.CanUpdate);

        source.Checkout.BuildSucceeds = true;
        await badge.BuildAgainCommand.ExecuteAsync(null);

        await TabTestHarness.Eventually(() => source.Host.Exited, "the restart after building again");
        Assert.Single(source.Checkout.Commands, c => c.StartsWith("merge", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Skipped_commits_stay_hidden_until_more_are_pushed()
    {
        var launcher = new FakeLauncher();
        await using var h = new TabTestHarness(launcher: launcher);
        using var source = await FakeCheckout.StartAsync(h, launcher);
        var badge = source.Badge;

        badge.SkipCommand.Execute(null);

        Assert.False(badge.HasBadge);
        Assert.Equal(source.Checkout.UpstreamHead, h.Services.State.SkippedSourceUpdate);
        Assert.Equal("3 new commits on origin/main; you skipped them.", badge.CheckStatusText);
        await source.Updates.CheckNowAsync();
        Assert.False(badge.HasBadge);

        source.Checkout.Behind = 4;
        source.Checkout.UpstreamHead = "d4d4d4d4d4d4d4d4";
        await source.Updates.CheckNowAsync();
        Assert.True(badge.HasBadge);
        Assert.Equal("4 new commits on origin/main", badge.BadgeText);
    }

    [Fact]
    public async Task A_build_running_in_place_is_only_pulled()
    {
        var launcher = new FakeLauncher();
        await using var h = new TabTestHarness(launcher: launcher);
        using var source = await FakeCheckout.StartAsync(h, launcher, inPlace: true);
        var badge = source.Badge;

        Assert.False(badge.CanUpdate);
        Assert.True(badge.CanPullOnly);
        Assert.NotNull(badge.InPlaceText);

        await badge.PullCommand.ExecuteAsync(null);

        Assert.Contains("merge --ff-only --quiet @{upstream}", source.Checkout.Commands);
        Assert.DoesNotContain(launcher.Started, s => s.Arguments is ["build", ..]);
        Assert.False(source.Host.Exited);
    }
}
