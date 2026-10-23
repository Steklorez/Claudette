using Claudette.Core.Installation;
using Claudette.Core.ProjectTools;
using Claudette.Core.Tests.Support;

namespace Claudette.Core.Tests.Installation;

/// <summary>
/// The Claude desktop app, which people take for Claude Code (DESIGN.md §12, "Installing Claude Code"): recognizing its
/// files, which are never run, and whether it's installed.
/// </summary>
public sealed class ClaudeDesktopTests : IDisposable
{
    private static readonly ProjectToolPaths Windows = new(@"C:\Users\me", @"C:\Users\me\AppData\Roaming", @"C:\Users\me\AppData\Local");

    private readonly TempFolder _temp = new("claudette-desktop");

    public void Dispose() => _temp.Dispose();

    [Theory]
    // The app itself, from its Windows package (the issue's screenshot), and its command.
    [InlineData(@"C:\Program Files\WindowsApps\Claude_3.6556.2.0_x64__pzs8sxrjxfjjc\app\claude.exe", ClaudeDesktopFile.App)]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WindowsApps\claude-desktop.exe", ClaudeDesktopFile.App)]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WindowsApps\Claude_pzs8sxrjxfjjc\claude-desktop.exe", ClaudeDesktopFile.App)]
    [InlineData(@"C:\Users\me\AppData\Local\AnthropicClaude\app-0.9.3\claude.exe", ClaudeDesktopFile.App)]
    // Its copy of Claude Code, as the package keeps it (seen with 3.6556.2), and as an unpackaged app would.
    [InlineData(@"C:\Users\me\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude-code\2.1.308\c17a819afb40\claude.exe", ClaudeDesktopFile.ClaudeCodeCopy)]
    [InlineData(@"c:\users\me\appdata\roaming\claude\claude-code\2.1.308\claude.exe", ClaudeDesktopFile.ClaudeCodeCopy)]
    // Claude Code's own installs: native, WinGet, npm.
    [InlineData(@"C:\Users\me\.local\bin\claude.exe", ClaudeDesktopFile.None)]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WinGet\Packages\Anthropic.ClaudeCode_Microsoft.Winget.Source_8wekyb3d8bbwe\claude.exe", ClaudeDesktopFile.None)]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WinGet\Links\claude.exe", ClaudeDesktopFile.None)]
    [InlineData(@"C:\Users\me\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe", ClaudeDesktopFile.None)]
    public void Recognizes_the_desktop_apps_files_on_Windows(string path, ClaudeDesktopFile expected)
    {
        Assert.Equal(expected, new ClaudeDesktop(Windows, ToolOS.Windows).Identify(path));
    }

    [Theory]
    [InlineData("/Applications/Claude.app/Contents/MacOS/Claude", ClaudeDesktopFile.App)]
    [InlineData("/Users/me/Applications/Claude.app/Contents/Resources/claude-ssh-proxy", ClaudeDesktopFile.App)]
    [InlineData("/Users/me/Library/Application Support/Claude/claude-code/2.1.308/claude", ClaudeDesktopFile.ClaudeCodeCopy)]
    [InlineData("/Users/me/.local/bin/claude", ClaudeDesktopFile.None)]
    [InlineData("/opt/homebrew/Caskroom/claude-code/2.1.308/claude", ClaudeDesktopFile.None)]
    public void Recognizes_the_desktop_apps_files_on_macOS(string path, ClaudeDesktopFile expected)
    {
        Assert.Equal(expected, new ClaudeDesktop(new ProjectToolPaths("/Users/me"), ToolOS.MacOS).Identify(path));
    }

    [Theory]
    [InlineData("/usr/bin/claude-desktop", ClaudeDesktopFile.App)]
    [InlineData("/home/me/.config/Claude/claude-code/2.1.308/claude", ClaudeDesktopFile.ClaudeCodeCopy)]
    // Linux paths are case-sensitive.
    [InlineData("/home/me/.config/claude/claude-code/2.1.308/claude", ClaudeDesktopFile.None)]
    [InlineData("/home/me/.local/bin/claude", ClaudeDesktopFile.None)]
    [InlineData("/usr/bin/claude", ClaudeDesktopFile.None)]
    public void Recognizes_the_desktop_apps_files_on_Linux(string path, ClaudeDesktopFile expected)
    {
        Assert.Equal(expected, new ClaudeDesktop(new ProjectToolPaths("/home/me"), ToolOS.Linux).Identify(path));
    }

    [Fact]
    public void Is_installed_on_Windows_when_its_command_or_package_is_there()
    {
        var paths = new ProjectToolPaths(_temp.Combine("home"), _temp.Combine("roaming"), _temp.Combine("local"));
        var desktop = new ClaudeDesktop(paths, ToolOS.Windows);
        Assert.False(desktop.IsInstalled(null));

        // The package's data folder, from the app's first run.
        Directory.CreateDirectory(_temp.Combine("local", "Packages", "Claude_pzs8sxrjxfjjc"));
        Assert.True(desktop.IsInstalled(null));

        Directory.Delete(_temp.Combine("local", "Packages"), recursive: true);
        // Its app execution alias, on PATH or not.
        Directory.CreateDirectory(_temp.Combine("local", "Microsoft", "WindowsApps"));
        File.WriteAllText(_temp.Combine("local", "Microsoft", "WindowsApps", "claude-desktop.exe"), "");
        Assert.True(desktop.IsInstalled(null));
    }

    [Fact]
    public void Is_installed_on_macOS_when_Claude_app_is_there()
    {
        var desktop = new ClaudeDesktop(new ProjectToolPaths(_temp.Combine("home"), Applications: _temp.Combine("Applications")), ToolOS.MacOS);
        Assert.False(desktop.IsInstalled(null));

        Directory.CreateDirectory(_temp.Combine("Applications", "Claude.app"));

        Assert.True(desktop.IsInstalled(null));
    }

    [Fact]
    public void Is_installed_on_Linux_when_claude_desktop_is_on_PATH()
    {
        Directory.CreateDirectory(_temp.Combine("bin"));
        File.WriteAllText(_temp.Combine("bin", "claude-desktop"), "");

        Assert.True(new ClaudeDesktop(new ProjectToolPaths(_temp.Combine("home")), ToolOS.Linux).IsInstalled(_temp.Combine("bin")));
    }

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Claude_3.6556.2.0_x64__pzs8sxrjxfjjc\app\claude.exe", "That's the Claude desktop app, not Claude Code.")]
    [InlineData(@"C:\Users\me\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude-code\2.1.284\3f4bed3e44ad\claude.exe", "That's the copy of Claude Code the Claude desktop app keeps")]
    public async Task A_path_to_the_desktop_apps_files_is_refused_without_running_it(string path, string detail)
    {
        // The path needn't exist: the desktop app deletes an old version's copy when it updates.
        var launcher = new FakeProcessLauncher();
        var locator = new ClaudeLocator(launcher, TimeProvider.System, desktop: new ClaudeDesktop(Windows, ToolOS.Windows));

        var result = await locator.LocateAsync(path, TestContext.Current.CancellationToken);

        Assert.False(result.IsUsable);
        Assert.Equal(ClaudeInstallProblem.DesktopApp, result.Problem);
        Assert.StartsWith(detail, result.Detail);
        Assert.Equal(path, result.FoundPath);
        Assert.True(result.DesktopInstalled);
        Assert.Empty(launcher.Started);
    }
}
