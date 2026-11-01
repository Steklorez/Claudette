using System.Text.Json;
using Deventry.App.Conversation;
using Deventry.App.Services;
using Deventry.App.Tests.Support;
using Deventry.App.ViewModels.Settings;
using Deventry.Core.Auth;
using Deventry.Core.RemoteControl;
using Deventry.Core.Settings;

namespace Deventry.App.Tests;

/// <summary>
/// What an account's organization has turned off, kept as a rule so Deventry stops trying (DESIGN.md §18, "Remote
/// Control"), and listed in Settings → Accounts with a way to remove it.
/// </summary>
public class OrganizationRulesTests
{
    private const string Reason = "Remote Control is disabled by your organization's policy. Contact your organization admin for access.";

    private static AuthStatus Account(string email, string organization) => new(true, "claude.ai", "firstParty", email, organization, "max", null, null);

    [Fact]
    public async Task A_policy_adds_a_rule_for_the_account_once()
    {
        await using var h = new TabTestHarness();
        var service = h.Services.RemoteControl;
        service.UseAccount(Account("Me@Example.com", "Example"));
        var changes = 0;
        service.AvailabilityChanged += () => changes++;

        service.OnTurnedOffByPolicy(Reason);
        service.OnTurnedOffByPolicy("another words");
        service.UseAccount(Account("me@example.com", "EXAMPLE"));
        service.OnTurnedOffByPolicy(Reason);

        var rule = Assert.Single(h.Services.State.OrganizationRules);
        Assert.Equal((OrganizationRule.RemoteControl, "Me@Example.com", "Example", Reason, h.Time.GetUtcNow()),
            (rule.Feature, rule.Email, rule.Organization, rule.Reason, rule.AddedAt));
        Assert.Equal(1, changes);
        Assert.Same(rule, service.RuleInForce);
        Assert.Contains("your organization's rule for Me@Example.com", service.UnavailableReason);
        Assert.Contains($"since {rule.Since}", service.UnavailableReason);
        Assert.StartsWith(Reason, service.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_rule_is_kept_and_a_new_launch_doesnt_try_again()
    {
        await using var h = new TabTestHarness();
        h.Services.RemoteControl.UseAccount(Account("me@example.com", "Example"));
        h.Services.RemoteControl.OnTurnedOffByPolicy(Reason);

        // A restart: the state is read back, and the service starts with no memory of its own.
        var state = JsonSerializer.Deserialize<AppState>(JsonFileStore<AppState>.Serialize(h.Services.State), JsonFileStore<AppState>.Options)!;
        Assert.Single(state.OrganizationRules);
        using var fresh = new RemoteControlService(h.Services);
        fresh.EnvironmentVariable = _ => null;
        Assert.Null(fresh.PolicyReason);
        Assert.Null(fresh.UnavailableReason);

        fresh.UseAccount(Account("ME@example.com", "example"));

        Assert.NotNull(fresh.UnavailableReason);
        Assert.False(fresh.IsAvailable);
    }

    [Fact]
    public async Task A_rule_covers_its_account_only()
    {
        await using var h = new TabTestHarness();
        var service = h.Services.RemoteControl;
        service.UseAccount(Account("me@example.com", "Example"));
        service.OnTurnedOffByPolicy(Reason);

        service.UseAccount(Account("me@example.com", "Other"));
        Assert.Null(service.UnavailableReason);
        service.UseAccount(Account("you@example.com", "Example"));
        Assert.Null(service.UnavailableReason);

        var changes = 0;
        service.AvailabilityChanged += () => changes++;
        service.UseAccount(Account("me@example.com", "Example"));
        Assert.NotNull(service.UnavailableReason);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task Without_a_known_account_the_policy_stays_in_memory()
    {
        await using var h = new TabTestHarness();
        var service = h.Services.RemoteControl;

        service.OnTurnedOffByPolicy(Reason);

        Assert.Empty(h.Services.State.OrganizationRules);
        Assert.Equal(Reason, service.UnavailableReason);
    }

    [Fact]
    public async Task Removing_the_rule_lets_tabs_try_again()
    {
        await using var h = new TabTestHarness();
        var service = h.Services.RemoteControl;
        service.UseAccount(Account("me@example.com", "Example"));
        service.OnTurnedOffByPolicy(Reason);
        var rule = Assert.Single(service.Rules);
        var availability = 0;
        var rules = 0;
        service.AvailabilityChanged += () => availability++;
        service.RulesChanged += () => rules++;

        service.RemoveRule(rule);

        Assert.Empty(service.Rules);
        Assert.Null(service.UnavailableReason);
        Assert.Equal((1, 1), (availability, rules));
    }

    [Fact]
    public async Task A_tab_on_a_ruled_account_doesnt_connect_or_warn_and_its_switch_says_why()
    {
        await using var h = new TabTestHarness(s => s.ClaudeCode.ConnectNewTabsToClaudeApp = true);
        h.Services.State.OrganizationRules.Add(new OrganizationRule
        {
            Feature = OrganizationRule.RemoteControl, Email = "me@example.com", Organization = "Example", Reason = Reason, AddedAt = h.Time.GetUtcNow(),
        });
        h.Services.RemoteControl.UseAccount(Account("me@example.com", "Example"));

        var tab = await h.OpenTabAsync();
        await TabTestHarness.Eventually(() => tab.RemoteControl.Status.State == RemoteControlState.Unavailable, "the rule");

        Assert.DoesNotContain(h.Transport.Sent, m => m["request"]?["subtype"]?.GetValue<string>() == "remote_control");
        // The tab keeps the user's choice, which can only be turned off; once it is, the switch is disabled with the rule as its tip.
        Assert.True(tab.RemoteControl.IsOn);
        await tab.RemoteControl.SetAsync(false);
        Assert.False(tab.RemoteControl.CanToggle);
        Assert.Contains("your organization's rule for me@example.com", tab.RemoteControl.ToggleTip);
        Assert.DoesNotContain(InlineDispatcher.Read(() => tab.Items.OfType<NoteItem>().Select(n => n.Text).ToList()), t => t.StartsWith("Not connecting", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Settings_lists_the_rules_and_removes_one()
    {
        await using var h = new TabTestHarness();
        using var page = new AccountsPage(new SettingsContext(h.Services));
        Assert.False(page.HasOrganizationRules);

        h.Services.RemoteControl.UseAccount(Account("me@example.com", "Example"));
        h.Services.RemoteControl.OnTurnedOffByPolicy(Reason);

        var row = Assert.Single(page.OrganizationRules);
        Assert.Equal(("Remote Control (the Claude app)", "me@example.com (Example)", Reason, $"Since {row.Rule.Since}"), (row.Feature, row.Account, row.Reason, row.Since));
        Assert.True(page.HasOrganizationRules);

        row.RemoveCommand.Execute(null);

        Assert.Empty(page.OrganizationRules);
        Assert.False(page.HasOrganizationRules);
        Assert.Empty(h.Services.State.OrganizationRules);
    }
}
