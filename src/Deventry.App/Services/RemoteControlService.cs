using Deventry.Core.Auth;
using Deventry.Core.RemoteControl;
using Deventry.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Deventry.App.Services;

/// <summary>
/// What the tabs share about Remote Control, the Claude app's connection to them (DESIGN.md §18, "Remote Control"):
/// whether the account can use it, the presence file that keeps pushes off the phone while Deventry is in front, and
/// which tabs are connected, which <see cref="KeepAwakeService"/> keeps the computer awake for. Use on the UI thread.
/// </summary>
public sealed class RemoteControlService : IDisposable
{
    private readonly AppServices _services;
    private readonly PresenceFile _presence;
    private readonly HashSet<string> _connectedTabs = new(StringComparer.Ordinal);

    public RemoteControlService(AppServices services)
    {
        _services = services;
        _presence = new PresenceFile(services.Paths.PresenceFile, services.Loggers.CreateLogger("Presence"));
        EnvironmentVariable = name => _services.UserEnvironment.Current is { } environment
            ? environment.GetValueOrDefault(name)
            : Environment.GetEnvironmentVariable(name);
    }

    /// <summary>
    /// A variable of the environment <c>claude</c> starts with: the login shell's when it's used (DESIGN.md §13). Tests
    /// replace it, so the machine running them doesn't decide.
    /// </summary>
    internal Func<string, string?> EnvironmentVariable { get; set; }

    /// <summary>The latest <c>claude auth status</c>, from the account menu. Null until the first.</summary>
    public AuthStatus? Account { get; private set; }

    /// <summary>
    /// Why this account can't use Remote Control, or null when it may. A tab's switch is off and disabled then. A rule
    /// for the account (<see cref="RuleInForce"/>) says it from the first launch on.
    /// </summary>
    public string? UnavailableReason => RemoteControlEligibility.Check(Account, EnvironmentVariable) ?? RuleInForce?.Describe() ?? PolicyReason;

    /// <summary>
    /// What Claude Code said when a policy turned Remote Control off, in any tab, for an account that isn't known (one
    /// that is has a rule instead, which outlives this). Every tab then stops trying to connect until Deventry restarts or
    /// another account signs in.
    /// </summary>
    public string? PolicyReason { get; private set; }

    /// <summary>
    /// What the organizations have turned off, as Settings → Accounts lists it (DESIGN.md §18, "Remote Control"). Kept in
    /// the machine's state, so it holds across restarts.
    /// </summary>
    public IReadOnlyList<OrganizationRule> Rules => _services.State.OrganizationRules;

    /// <summary>The rule that keeps the signed-in account from Remote Control, or null when there's none.</summary>
    public OrganizationRule? RuleInForce => Account is { } account && RemoteControlEligibility.Check(account, EnvironmentVariable) is null
        ? _services.State.OrganizationRules.FirstOrDefault(r => r.Matches(OrganizationRule.RemoteControl, account.Email, account.OrganizationName))
        : null;

    /// <summary>Raised when a rule is added or removed, for Settings.</summary>
    public event Action? RulesChanged;

    /// <summary>
    /// A tab heard that a policy turned Remote Control off. With the account known that becomes a rule, once, so no tab
    /// tries on a later launch; without it, it holds until Deventry restarts or another account signs in.
    /// </summary>
    public void OnTurnedOffByPolicy(string reason)
    {
        var before = UnavailableReason;
        PolicyReason ??= reason;
        if (Account is { Email: { Length: > 0 } email } account
            && !_services.State.OrganizationRules.Any(r => r.Matches(OrganizationRule.RemoteControl, email, account.OrganizationName)))
        {
            _services.State.OrganizationRules.Add(new OrganizationRule
            {
                Feature = OrganizationRule.RemoteControl,
                Email = email,
                Organization = account.OrganizationName ?? "",
                Reason = reason,
                AddedAt = _services.Time.GetUtcNow(),
            });
            _services.Loggers.CreateLogger("RemoteControl").LogInformation("Remote Control is turned off for {Email} ({Organization}) by its organization; not trying again: {Reason}", email, account.OrganizationName, reason);
            _services.SaveState();
            RulesChanged?.Invoke();
        }
        if (UnavailableReason != before)
        {
            AvailabilityChanged?.Invoke();
        }
    }

    /// <summary>The user removed a rule: tabs may try again, with the account's organization having changed its mind.</summary>
    public void RemoveRule(OrganizationRule rule)
    {
        var before = UnavailableReason;
        if (!_services.State.OrganizationRules.Remove(rule))
        {
            return;
        }
        if (Account is { } account && rule.Matches(OrganizationRule.RemoteControl, account.Email, account.OrganizationName))
        {
            PolicyReason = null;
        }
        _services.SaveState();
        RulesChanged?.Invoke();
        if (UnavailableReason != before)
        {
            AvailabilityChanged?.Invoke();
        }
    }

    public bool IsAvailable => UnavailableReason is null;

    /// <summary>
    /// Raised on the UI thread when the account changes, which can make Remote Control available or not. Tabs that
    /// want to connect then try again.
    /// </summary>
    public event Action? AvailabilityChanged;

    public void UseAccount(AuthStatus? account)
    {
        var before = UnavailableReason;
        if (account?.Email != Account?.Email || account?.OrganizationName != Account?.OrganizationName)
        {
            // Another account's organization may allow it.
            PolicyReason = null;
        }
        Account = account;
        if (UnavailableReason != before)
        {
            AvailabilityChanged?.Invoke();
        }
    }

    /// <summary>The variables every <c>claude</c> Deventry starts gets on top of its environment.</summary>
    public IReadOnlyDictionary<string, string?> ClaudeVariables => new Dictionary<string, string?> { [PresenceFile.Variable] = _presence.Path };

    // ---- Presence (DESIGN.md §10) --------------------------------------------------------------------------------

    /// <summary>The presence file is there while Deventry's window is in front, so Claude Code doesn't push to the phone.</summary>
    public void SetAppActive(bool active) => _presence.SetPresent(active);

    public bool IsPresent => _presence.IsPresent;

    // ---- Keeping the computer awake -------------------------------------------------------------------------------

    /// <summary>How many tabs are connected to the Claude app now.</summary>
    public int ConnectedTabs => _connectedTabs.Count;

    /// <summary>
    /// A tab connected or stopped being connected: sleep is held off while any tab is, if Settings → Claude Code allows
    /// (<see cref="KeepAwakeService"/>).
    /// </summary>
    public void SetTabConnected(string tabId, bool connected)
    {
        if (connected ? _connectedTabs.Add(tabId) : _connectedTabs.Remove(tabId))
        {
            _services.KeepAwake.SetConnectedTabs(_connectedTabs.Count);
        }
    }

    /// <summary>At exit: nobody is at Deventry. <see cref="KeepAwakeService"/> lets the computer sleep again itself.</summary>
    public void Dispose()
    {
        _connectedTabs.Clear();
        _presence.Dispose();
    }
}
