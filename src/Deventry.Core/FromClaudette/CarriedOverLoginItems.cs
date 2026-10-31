using Deventry.Core.LoginItems;

namespace Deventry.Core.FromClaudette;

/// <summary>
/// The login entry under Deventry's name, with the one Claudette wrote under its own (DESIGN.md §2, "Renaming to
/// Deventry"): Claudette's counts until Deventry writes its own, which replaces it. Starting at login has it written
/// again as Deventry starts (<see cref="StartAtLogin"/>), since the program it names has a new name too.
/// </summary>
public sealed class CarriedOverLoginItems(ILoginItems Deventrys, ILoginItems claudettes) : ILoginItems
{
    /// <summary>Only Claudette's entry is there, which starts at login as Deventry's would.</summary>
    public bool HasOnlyClaudettes => Deventrys.ReadEntry() == LoginEntryState.Missing && claudettes.ReadEntry() != LoginEntryState.Missing;

    public string? UnavailableReason => Deventrys.UnavailableReason;

    public string SystemSettingsName => Deventrys.SystemSettingsName;

    public IPackageStartupTask? PackageTask => Deventrys.PackageTask;

    public LoginEntryState ReadEntry() => Deventrys.ReadEntry() is var entry and not LoginEntryState.Missing ? entry : claudettes.ReadEntry();

    public void WriteEntry(ClaudetteCopy copy)
    {
        Deventrys.WriteEntry(copy);
        claudettes.DeleteEntry();
    }

    public void DeleteEntry()
    {
        Deventrys.DeleteEntry();
        claudettes.DeleteEntry();
    }

    public bool Exists(ClaudetteCopy copy) => Deventrys.Exists(copy);

    public void Start(ClaudetteCopy copy) => Deventrys.Start(copy);
}
