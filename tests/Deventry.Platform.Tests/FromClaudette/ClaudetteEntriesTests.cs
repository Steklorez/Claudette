using System.Runtime.Versioning;
using Deventry.Core.LoginItems;
using Deventry.Core.Updates;
using Deventry.Platform.Credentials.Windows;
using Deventry.Platform.FromClaudette;
using Deventry.Platform.LoginItems.Linux;
using Deventry.Platform.LoginItems.Windows;
using Deventry.Platform.Tests.Credentials;
using Deventry.Platform.Tests.Support;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Win32;

namespace Deventry.Platform.Tests.FromClaudette;

/// <summary>
/// What Claudette left in the OS under its own name, carried over to Deventry's (DESIGN.md §2, "Renaming to
/// Deventry"): a saved secret in Credential Manager, the Run key's value and the XDG autostart file. The registry
/// writes go to a key of the test's own, never the real Run key.
/// </summary>
[Collection(nameof(RealCredentialStore))]
public sealed class ClaudetteEntriesTests : IDisposable
{
    private readonly string _key = $@"Software\Deventry.Tests.FromClaudette.{Guid.NewGuid():N}";
    private readonly string _root = Directory.CreateTempSubdirectory("deventry-from-claudette-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Registry.CurrentUser.DeleteSubKeyTree(_key, throwOnMissingSubKey: false);
        }
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task A_secret_Claudette_saved_in_Credential_Manager_is_found_and_moves_under_Deventrys_name()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        var Deventrys = new WindowsCredentialStore();
        var claudettes = new WindowsCredentialStore(ClaudetteCredentials.WindowsPrefix);
        var key = $"test/{Guid.NewGuid():N}";
        try
        {
            await claudettes.WriteAsync(key, "Claudette test", "s3cret", Ct);

            Assert.Equal("s3cret", await ClaudetteCredentials.CarriedOver(Deventrys, claudettes).ReadAsync(key, Ct));

            Assert.Equal("s3cret", await Deventrys.ReadAsync(key, Ct));
            Assert.Null(await claudettes.ReadAsync(key, Ct));
            Assert.Equal($"Deventry/{key}", Deventrys.TargetName(key));
        }
        finally
        {
            await Deventrys.DeleteAsync(key, Ct);
            await claudettes.DeleteAsync(key, Ct);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows10.0.16299")]
    public void The_Run_value_Claudette_wrote_counts_until_Deventry_writes_its_own_in_its_place()
    {
        Assert.SkipUnless(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299), "Windows only.");
        var build = Directory.CreateDirectory(Path.Combine(_root, "Deventry")).FullName;
        File.WriteAllText(Path.Combine(build, "Deventry.exe"), "");
        var claudettes = new WindowsLoginItems(new FakeLauncher(), new FakeTimeProvider(), _key + @"\Run", _key + @"\Approved", ClaudetteLoginItems.WindowsValueName);
        var Deventrys = new WindowsLoginItems(new FakeLauncher(), new FakeTimeProvider(), _key + @"\Run", _key + @"\Approved");
        claudettes.WriteEntry(new ClaudetteCopy(AppInstallKind.Other, build, "0.7.0"));
        var items = ClaudetteLoginItems.CarriedOver(Deventrys, claudettes);

        Assert.Equal(LoginEntryState.Enabled, items.ReadEntry());
        items.WriteEntry(new ClaudetteCopy(AppInstallKind.Other, build, "1.0.0"));

        Assert.Equal($"\"{Path.Combine(build, "Deventry.exe")}\" --login", Deventrys.ReadCommand());
        Assert.Null(claudettes.ReadCommand());
        Assert.Equal("Deventry", Deventrys.ValueName);
    }

    [Fact]
    public void The_autostart_file_Claudette_wrote_counts_until_Deventry_writes_its_own_and_goes_when_its_turned_off()
    {
        var autostart = Path.Combine(_root, "autostart");
        Directory.CreateDirectory(autostart);
        File.WriteAllText(Path.Combine(autostart, ClaudetteLoginItems.LinuxFileName), "[Desktop Entry]\nType=Application\nExec=claudette --login\n");
        var Deventrys = new XdgAutostartLoginItems(new FakeLauncher(), autostart);
        var items = ClaudetteLoginItems.CarriedOver(Deventrys,
            new XdgAutostartLoginItems(new FakeLauncher(), autostart, fileName: ClaudetteLoginItems.LinuxFileName));

        Assert.Equal(LoginEntryState.Enabled, items.ReadEntry());
        items.DeleteEntry();

        Assert.Equal(LoginEntryState.Missing, items.ReadEntry());
        Assert.Empty(Directory.EnumerateFiles(autostart));
        Assert.Equal("deventry.desktop", Path.GetFileName(Deventrys.FilePath));
    }
}
