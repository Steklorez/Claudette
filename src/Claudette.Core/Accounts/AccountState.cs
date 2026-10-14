using Claudette.Core.Settings;

namespace Claudette.Core.Accounts;

/// <summary>
/// The accounts in the app state and which one each folder uses (DESIGN.md §4, "Accounts"). A null account id means the
/// Default account: Claude Code's own sign-in, as it is in a terminal. Nothing here saves the state.
/// </summary>
public static class AccountState
{
    /// <summary>The account with <paramref name="id"/>, or null for the Default account, or one that's gone or can't be used.</summary>
    public static ClaudeAccount? Find(AppState state, string? id) =>
        id is null ? null : state.Accounts.FirstOrDefault(a => a.Id == id && ApiKeyHelper.IsSafeId(a.Id) && a.IsApiKey);

    /// <summary>
    /// The account a new tab in <paramref name="folder"/> uses: the one its group was set to, else the one new folders
    /// use (Settings → Accounts), else Default. An account removed since counts as Default.
    /// </summary>
    public static string? ForFolder(AppState state, string folder)
    {
        foreach (var (path, id) in state.FolderAccounts)
        {
            if (FolderHistory.SamePath(path, folder))
            {
                return Find(state, id)?.Id;
            }
        }
        return Find(state, state.NewFolderAccount)?.Id;
    }

    /// <summary>
    /// Remembers <paramref name="accountId"/> for <paramref name="folder"/>, in place of any it had under another spelling.
    /// Default is remembered too, so a folder set back to it doesn't take the one new folders use.
    /// </summary>
    public static void RememberForFolder(AppState state, string folder, string? accountId)
    {
        foreach (var path in state.FolderAccounts.Keys.Where(p => FolderHistory.SamePath(p, folder)).ToList())
        {
            state.FolderAccounts.Remove(path);
        }
        state.FolderAccounts[folder] = accountId ?? DefaultId;
    }

    /// <summary>How <see cref="AppState.FolderAccounts"/> writes the Default account.</summary>
    public const string DefaultId = "default";

    /// <summary>Forgets an account that was removed: the folders that used it use Default again.</summary>
    public static void Forget(AppState state, string accountId)
    {
        state.Accounts.RemoveAll(a => a.Id == accountId);
        foreach (var path in state.FolderAccounts.Where(p => p.Value == accountId).Select(p => p.Key).ToList())
        {
            state.FolderAccounts[path] = DefaultId;
        }
        if (state.NewFolderAccount == accountId)
        {
            state.NewFolderAccount = null;
        }
    }
}
