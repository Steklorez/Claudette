<#
.SYNOPSIS
What screenshots.ps1 and guide-screenshots.ps1 share (Windows): building Claudette and the demo, starting Claudette on the
demo and closing it, finding and clicking its controls through UI Automation, and capturing its windows with PrintWindow.

.DESCRIPTION
Claudette runs with CLAUDETTE_UNFOCUSED=1 and its windows behind the user's, so a run leaves the focus, the keyboard and
the pointer to the user. Clicks and text go through UI Automation and keys without modifiers are posted to the window,
none of which needs the focus. Only a key chord or a hover needs the real keyboard or pointer: Enter-Input borrows them
and Exit-Input gives them back. The focus watch warns if Claudette came to the front otherwise.
#>
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
if (-not ('WindowShot' -as [type])) {
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

public static class WindowShot
{
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

    /// <summary>
    /// The window as it draws itself, without the resize borders and shadow around its frame; down to
    /// <paramref name="bottom"/> on the screen when it isn't zero.
    /// </summary>
    public static void Save(IntPtr hwnd, string path, int bottom)
    {
        DrawActive(hwnd);
        RECT outer, frame;
        GetWindowRect(hwnd, out outer);
        DwmGetWindowAttribute(hwnd, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out frame, Marshal.SizeOf(typeof(RECT)));
        using (var full = new Bitmap(outer.Right - outer.Left, outer.Bottom - outer.Top, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(full))
            {
                var hdc = g.GetHdc();
                PrintWindow(hwnd, hdc, 2 /* PW_RENDERFULLCONTENT */);
                g.ReleaseHdc(hdc);
            }
            var to = bottom == 0 ? frame.Bottom : Math.Min(bottom, frame.Bottom);
            var crop = new Rectangle(frame.Left - outer.Left, frame.Top - outer.Top, frame.Right - frame.Left, to - frame.Top);
            using (var shot = full.Clone(crop, PixelFormat.Format32bppArgb))
            {
                shot.Save(path, ImageFormat.Png);
            }
        }
    }

    /// <summary>The part of the window from (<paramref name="left"/>, <paramref name="top"/>) to (<paramref name="right"/>, <paramref name="bottom"/>) on the screen.</summary>
    public static void SaveRegion(IntPtr hwnd, string path, int left, int top, int right, int bottom)
    {
        DrawActive(hwnd);
        RECT outer;
        GetWindowRect(hwnd, out outer);
        using (var full = new Bitmap(outer.Right - outer.Left, outer.Bottom - outer.Top, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(full))
            {
                var hdc = g.GetHdc();
                PrintWindow(hwnd, hdc, 2 /* PW_RENDERFULLCONTENT */);
                g.ReleaseHdc(hdc);
            }
            var crop = Rectangle.Intersect(new Rectangle(left - outer.Left, top - outer.Top, right - left, bottom - top), new Rectangle(0, 0, full.Width, full.Height));
            using (var shot = full.Clone(crop, PixelFormat.Format32bppArgb))
            {
                shot.Save(path, ImageFormat.Png);
            }
        }
    }

    /// <summary>Screen pixels per device-independent pixel in the window.</summary>
    public static double Scale(IntPtr hwnd)
    {
        return GetDpiForWindow(hwnd) / 96.0;
    }

    /// <summary>Sets a window's size in device-independent pixels, keeping its place.</summary>
    public static void Resize(IntPtr hwnd, int width, int height)
    {
        var scale = Scale(hwnd);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, (int)(width * scale), (int)(height * scale), 0x0002 /* NOMOVE */ | 0x0004 /* NOZORDER */);
    }

    /// <summary>A visible top-level window of the process other than <paramref name="main"/>, or zero.</summary>
    public static IntPtr OtherWindow(int processId, IntPtr main)
    {
        var found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
        {
            uint pid;
            GetWindowThreadProcessId(hwnd, out pid);
            RECT rect;
            GetWindowRect(hwnd, out rect);
            if (pid == processId && hwnd != main && IsWindowVisible(hwnd) && rect.Right - rect.Left > 400)
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>
    /// Has the window draw its frame as active, as it does with the focus: without it, Windows draws the title bar
    /// plainer. Claudette never has the focus to lose again, so it stays drawn that way.
    /// </summary>
    static void DrawActive(IntPtr hwnd)
    {
        SendMessage(hwnd, 0x0086 /* WM_NCACTIVATE */, new IntPtr(1), IntPtr.Zero);
        Thread.Sleep(200);
    }

    /// <summary>Puts the window behind every other, without activating it: PrintWindow captures it there all the same.</summary>
    public static void ToBack(IntPtr hwnd)
    {
        SetWindowPos(hwnd, new IntPtr(1) /* HWND_BOTTOM */, 0, 0, 0, 0, 0x0001 /* NOSIZE */ | 0x0002 /* NOMOVE */ | 0x0010 /* NOACTIVATE */);
    }

    /// <summary>Puts the pointer at (<paramref name="x"/>, <paramref name="y"/>) on the screen, which a tip waits for before it shows.</summary>
    public static void MovePointer(int x, int y)
    {
        SetCursorPos(x, y);
    }

    /// <summary>Where the pointer is on the screen: x, then y.</summary>
    public static int[] Pointer()
    {
        POINT point;
        GetCursorPos(out point);
        return new[] { point.X, point.Y };
    }

    /// <summary>Whether the pointer is over a window of the same process as <paramref name="hwnd"/>.</summary>
    public static bool PointerOver(IntPtr hwnd)
    {
        POINT point;
        uint under, mine;
        GetCursorPos(out point);
        GetWindowThreadProcessId(WindowFromPoint(point), out under);
        GetWindowThreadProcessId(hwnd, out mine);
        return under == mine;
    }

    /// <summary>The window in front, or zero for none.</summary>
    public static IntPtr Foreground()
    {
        return GetForegroundWindow();
    }

    /// <summary>
    /// Presses a key without modifiers, such as Enter, by posting it to the window, which needs neither the focus nor the
    /// keyboard. Not a chord: Avalonia reads Ctrl and Shift from the real keyboard.
    /// </summary>
    public static void PostKey(IntPtr hwnd, int key)
    {
        long scan = MapVirtualKey((uint)key, 0 /* MAPVK_VK_TO_VSC */);
        PostMessage(hwnd, 0x0100 /* WM_KEYDOWN */, new IntPtr(key), new IntPtr(1 | (scan << 16)));
        PostMessage(hwnd, 0x0101 /* WM_KEYUP */, new IntPtr(key), new IntPtr(1 | (scan << 16) | (1L << 30) | (1L << 31)));
    }

    /// <summary>Brings the window back from being minimized, and to the front; whether it got there.</summary>
    public static bool Restore(IntPtr hwnd)
    {
        ShowWindow(hwnd, 9 /* SW_RESTORE */);
        TapAlt();
        SetForegroundWindow(hwnd);
        for (var waited = 0; waited < 1000 && GetForegroundWindow() != hwnd; waited += 50) { Thread.Sleep(50); }
        Thread.Sleep(300);
        return GetForegroundWindow() == hwnd;
    }

    /// <summary>Gives the front back to <paramref name="hwnd"/>, which had it before <see cref="Restore"/>.</summary>
    public static void GiveBack(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) { return; }
        TapAlt();
        SetForegroundWindow(hwnd);
    }

    /// <summary>Windows lets a process take the front only just after it had input: a tap of Alt counts.</summary>
    static void TapAlt()
    {
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2 /* KEYEVENTF_KEYUP */, UIntPtr.Zero);
    }

    /// <summary>
    /// Presses <paramref name="keys"/> (virtual-key codes) together in the window, which must be in front: keys go to the
    /// window in front, wherever the pointer is, so none are pressed otherwise.
    /// </summary>
    public static void Press(IntPtr hwnd, int[] keys)
    {
        if (GetForegroundWindow() != hwnd) { throw new InvalidOperationException("Claudette isn't in front, so the keys would go to another window."); }
        foreach (var key in keys) { keybd_event((byte)key, 0, 0, UIntPtr.Zero); }
        Thread.Sleep(50);
        for (var i = keys.Length - 1; i >= 0; i--) { keybd_event((byte)keys[i], 0, 2, UIntPtr.Zero); }
    }
}

/// <summary>
/// Notes each step during which a window of Claudette's came to the front, other than while <see cref="Lent"/>: a run
/// leaves the focus to the user.
/// </summary>
public static class FocusWatch
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    static Timer timer;
    static uint watched;
    static uint last;
    static readonly List<string> taken = new List<string>();

    /// <summary>What the script is doing, to say when Claudette came to the front.</summary>
    public static volatile string Step = "";

    /// <summary>The script brought Claudette to the front itself, for keys or the pointer.</summary>
    public static volatile bool Lent;

    public static void Start(int processId)
    {
        Stop();
        lock (taken) { taken.Clear(); }
        watched = (uint)processId;
        last = 0;
        Lent = false;
        Step = "starting Claudette";
        timer = new Timer(delegate { Check(); }, null, 0, 100);
    }

    /// <summary>Stops watching; the steps during which Claudette came to the front.</summary>
    public static string[] Stop()
    {
        if (timer != null) { timer.Dispose(); timer = null; }
        lock (taken) { return taken.ToArray(); }
    }

    static void Check()
    {
        uint now;
        GetWindowThreadProcessId(GetForegroundWindow(), out now);
        // Coming to the front counts, not staying there once lent.
        if (now == watched && last != watched && !Lent)
        {
            lock (taken) { if (!taken.Contains(Step)) { taken.Add(Step); } }
        }
        last = now;
    }
}
'@
}
[WindowShot]::SetProcessDPIAware() | Out-Null

$ae = [System.Windows.Automation.AutomationElement]
$descendants = [System.Windows.Automation.TreeScope]::Descendants

# A window's handle or an element: where a search starts.
function Get-Root($from) {
    if ($from -is [IntPtr]) { $ae::FromHandle($from) } else { $from }
}

# The controls named $text. "Open diff#2" is the second one.
function Find-ByText($from, [string] $text, [switch] $Optional) {
    $index = 0
    if ($text -match '^(.*)#(\d+)$') { $text = $Matches[1]; $index = [int] $Matches[2] - 1 }
    $named = New-Object System.Windows.Automation.PropertyCondition($ae::NameProperty, $text)
    $all = (Get-Root $from).FindAll($descendants, $named)
    if ($all.Count -le $index) {
        if ($Optional) { return $null }
        throw "Found $($all.Count) controls named '$text'."
    }
    $all[$index]
}

# The first control whose name starts with $prefix, such as History's chip for a project, "starfall, 5 sessions"; or null.
function Find-ByPrefix($from, [string] $prefix) {
    $all = (Get-Root $from).FindAll($descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($element in $all) {
        if ($element.Current.Name.StartsWith($prefix, [StringComparison]::Ordinal)) { return $element }
    }
    $null
}

# The first control of a type, such as 'Tree' or 'Edit', named $text when that's given; or null.
function Find-ByType($from, [string] $type, [string] $text = '') {
    $found = New-Object System.Windows.Automation.PropertyCondition($ae::ControlTypeProperty, [System.Windows.Automation.ControlType]::($type))
    $condition = $found
    if ($text) {
        $named = New-Object System.Windows.Automation.PropertyCondition($ae::NameProperty, $text)
        $condition = New-Object System.Windows.Automation.AndCondition($found, $named)
    }
    (Get-Root $from).FindFirst($descendants, $condition)
}

# The control whose x:Name is $id (UI Automation's AutomationId), such as the composer's text box; or null.
function Find-ById($from, [string] $id) {
    (Get-Root $from).FindFirst($descendants, (New-Object System.Windows.Automation.PropertyCondition($ae::AutomationIdProperty, $id)))
}

# The control around $element.
function Get-Parent($element) {
    [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($element)
}

# The lowest text under $from, such as the last line of a page that doesn't fill its panel; or null.
function Find-Lowest($from) {
    $type = New-Object System.Windows.Automation.PropertyCondition($ae::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $lowest = $null
    foreach ($text in (Get-Root $from).FindAll($descendants, $type)) {
        $rect = $text.Current.BoundingRectangle
        if (-not $rect.IsEmpty -and (-not $lowest -or $rect.Bottom -gt $lowest.Current.BoundingRectangle.Bottom)) { $lowest = $text }
    }
    $lowest
}

# Prints the controls under $from, one a line: type, name and id. For finding what to click.
function Show-Tree($from, [int] $depth = 12) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    $walk = {
        param($element, $level)
        $type = $element.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
        $rect = $element.Current.BoundingRectangle
        '{0}{1} "{2}" #{3} [{4:N0},{5:N0} {6:N0}x{7:N0}]' -f ('  ' * $level), $type, $element.Current.Name, $element.Current.AutomationId, $rect.X, $rect.Y, $rect.Width, $rect.Height
        if ($level -lt $depth) {
            for ($child = $walker.GetFirstChild($element); $child; $child = $walker.GetNextSibling($child)) { & $walk $child ($level + 1) }
        }
    }
    & $walk (Get-Root $from) 0
}

# Clicks the control named $text, or the nearest one around it that can be clicked.
function Invoke-ByText($from, [string] $text) {
    Invoke-Element (Find-ByText $from $text) $text
}

function Invoke-Element($element, [string] $what) {
    $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    while ($element) {
        $pattern = $null
        if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref] $pattern)) {
            $pattern.Invoke()
            return
        }
        $element = $walker.GetParent($element)
    }
    throw "Nothing to click around '$what'."
}

# Selects the item at $index (from 0) of the tree named $tree, such as a node of the agent map.
function Select-TreeItem([IntPtr] $hwnd, [string] $tree, [int] $index) {
    $found = Find-ByType $hwnd 'Tree' $tree
    if (-not $found) { throw "Found no tree named '$tree'." }
    $type = New-Object System.Windows.Automation.PropertyCondition($ae::ControlTypeProperty, [System.Windows.Automation.ControlType]::TreeItem)
    $items = $found.FindAll($descendants, $type)
    if ($items.Count -le $index) { throw "The tree '$tree' has $($items.Count) items." }
    $items[$index].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

# Scrolls $element into view in the list or conversation that holds it, if that can. With the conversation's scroll pane
# as $scroller, it then scrolls back until the element is $gap device-independent pixels under the pane's top, so what sits
# above the element, such as a card's header, shows too.
function Show-Element([IntPtr] $hwnd, $element, $scroller = $null, [int] $gap = 0) {
    $pattern = $null
    if ($element.TryGetCurrentPattern([System.Windows.Automation.ScrollItemPattern]::Pattern, [ref] $pattern)) { $pattern.ScrollIntoView() }
    if (-not $scroller) { return }
    $scroll = $scroller.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    $least = $gap * [WindowShot]::Scale($hwnd)
    for ($step = 0; $step -lt 20; $step++) {
        Start-Sleep -Milliseconds 200
        if ($element.Current.BoundingRectangle.Top - $scroller.Current.BoundingRectangle.Top -ge $least) { break }
        $scroll.Scroll([System.Windows.Automation.ScrollAmount]::NoAmount, [System.Windows.Automation.ScrollAmount]::SmallDecrement)
    }
}

# Puts $text in the text box $element, without typing it.
function Set-ElementText($element, [string] $text) {
    $element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
}

# Sends $text from the selected tab's composer.
function Send-Message([IntPtr] $hwnd, [string] $text) {
    $box = Find-ById $hwnd 'Composer'
    if (-not $box) { throw 'Found no composer.' }
    Set-ElementText $box $text
    Start-Sleep -Milliseconds 300
    Invoke-ByText $hwnd 'Send'
}

# Takes the keyboard and the pointer for a shot that needs them (guide-screenshots.ps1's -WithInput): brings the window to
# the front, noting the window that had it and where the pointer was, for Exit-Input to put back.
function Enter-Input([IntPtr] $hwnd) {
    $script:lentFrom = [WindowShot]::Foreground()
    $script:pointerWas = [WindowShot]::Pointer()
    [FocusWatch]::Lent = $true
    if (-not [WindowShot]::Restore($hwnd)) {
        Exit-Input $hwnd
        throw "Couldn't bring Claudette to the front."
    }
}

# Puts the pointer back, gives the front back to the window that had it, and puts Claudette behind it again.
function Exit-Input([IntPtr] $hwnd) {
    [WindowShot]::MovePointer($script:pointerWas[0], $script:pointerWas[1])
    [WindowShot]::GiveBack($script:lentFrom)
    [WindowShot]::ToBack($hwnd)
    Start-Sleep -Milliseconds 300
    [FocusWatch]::Lent = $false
}

# Puts the pointer on the middle of $element, for the tip over it to show.
function Move-PointerTo($element) {
    $rect = $element.Current.BoundingRectangle
    [WindowShot]::MovePointer([int] ($rect.Left + $rect.Width / 2), [int] ($rect.Top + $rect.Height / 2))
}

$keyCodes = @{ Ctrl = 0x11; Shift = 0x10; Alt = 0x12; Esc = 0x1B; Enter = 0x0D; Tab = 0x09; F3 = 0x72; F4 = 0x73 }

# Presses a key chord, such as 'Ctrl+Shift+P', on the real keyboard: the window must be in front (Enter-Input).
function Send-Keys([IntPtr] $hwnd, [string] $chord) {
    $keys = foreach ($name in $chord -split '\+') {
        if ($keyCodes.ContainsKey($name)) { $keyCodes[$name] } else { [int] [char] $name.ToUpperInvariant() }
    }
    [WindowShot]::Press($hwnd, [int[]] $keys)
}

# Presses a key without modifiers, such as 'Enter' or 'Esc', by posting it to the window: no focus needed.
function Send-WindowKey([IntPtr] $hwnd, [string] $key) {
    [WindowShot]::PostKey($hwnd, $keyCodes[$key])
}

# What the script is doing, for Stop-Claudette to say if Claudette came to the front meanwhile.
function Set-Step([string] $name) {
    [FocusWatch]::Step = $name
}

# Waits for a window of Claudette's other than $main, such as Settings, and puts it behind the user's windows.
function Get-OtherWindow($claudette, [IntPtr] $main, [string] $what, [int] $seconds = 20) {
    Wait-Until { [WindowShot]::OtherWindow($claudette.Id, $main) -ne [IntPtr]::Zero } $what $seconds
    $other = [WindowShot]::OtherWindow($claudette.Id, $main)
    [WindowShot]::ToBack($other)
    $other
}

# Waits while the pointer is over Claudette, where a window of it shows past the user's: what's under it would show lit.
function Wait-PointerAway([IntPtr] $hwnd, [string] $path) {
    $deadline = (Get-Date).AddSeconds(5)
    while ([WindowShot]::PointerOver($hwnd)) {
        if ((Get-Date) -gt $deadline) {
            Write-Warning "The pointer is over Claudette, so what's under it may show lit in $(Split-Path $path -Leaf)."
            return
        }
        Start-Sleep -Milliseconds 250
    }
}

# The window as $path, down to $bottom on the screen when that isn't zero. -Hovered for a tip the pointer is on purpose
# over its window for.
function Save-Window([IntPtr] $hwnd, [string] $path, [int] $bottom = 0, [switch] $Hovered) {
    if (-not $Hovered) { Wait-PointerAway $hwnd $path }
    [WindowShot]::Save($hwnd, $path, $bottom)
}

# The part of the screen the controls cover.
function Get-Bounds($elements) {
    $union = [System.Windows.Rect]::Empty
    foreach ($element in $elements) { $union = [System.Windows.Rect]::Union($union, $element.Current.BoundingRectangle) }
    $union
}

# $rect of the window, with $margin device-independent pixels around it, as $path. $margin is one number, or the left, top,
# right and bottom's, for a card that UI Automation doesn't show the edges of.
function Save-Rect([IntPtr] $hwnd, [string] $path, [System.Windows.Rect] $rect, [double[]] $margin = @(8)) {
    if ($rect.IsEmpty -or $rect.Width -lt 1) { throw "Nothing to capture for $path." }
    if ($margin.Count -eq 1) { $margin = $margin * 4 }
    $scale = [WindowShot]::Scale($hwnd)
    Wait-PointerAway $hwnd $path
    [WindowShot]::SaveRegion($hwnd, $path, [int] ($rect.Left - $margin[0] * $scale), [int] ($rect.Top - $margin[1] * $scale),
        [int] ($rect.Right + $margin[2] * $scale), [int] ($rect.Bottom + $margin[3] * $scale))
}

# The part of the window the controls cover, with $margin around it (Save-Rect).
function Save-Elements([IntPtr] $hwnd, [string] $path, $elements, [double[]] $margin = @(8)) {
    Save-Rect $hwnd $path (Get-Bounds $elements) $margin
}

function Save-Element([IntPtr] $hwnd, [string] $path, $element, [double[]] $margin = @(8)) {
    Save-Rect $hwnd $path $element.Current.BoundingRectangle $margin
}

# Closes a window of Claudette's, such as Settings, as its close button does.
function Close-Window([IntPtr] $hwnd) {
    $ae::FromHandle($hwnd).GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
}

# Turns the check box named $text on or off.
function Set-Toggle($from, [string] $text, [bool] $on) {
    $toggle = (Find-ByText $from $text).GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if (($toggle.Current.ToggleState -eq 'On') -ne $on) { $toggle.Toggle() }
}

# Selects the list item named $text, such as one of Settings' categories.
function Select-ListItem($from, [string] $text) {
    $item = Find-ByType $from 'ListItem' $text
    if (-not $item) { throw "Found no list item named '$text'." }
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
}

function Wait-Until([scriptblock] $condition, [string] $what, [int] $seconds = 60) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while (-not (& $condition)) {
        if ((Get-Date) -gt $deadline) { throw "Timed out waiting for $what." }
        Start-Sleep -Milliseconds 250
    }
}

# Builds Claudette and the demo; returns where their programs are.
function Build-Claudette([string] $repo) {
    Write-Host 'Building Claudette and the demo...'
    & dotnet build (Join-Path $repo 'src\Claudette.App\Claudette.App.csproj') -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }
    & dotnet build (Join-Path $repo 'tools\Claudette.Demo\Claudette.Demo.csproj') -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }
    [pscustomobject] @{
        App = Join-Path $repo 'src\Claudette.App\bin\Debug\net10.0\Claudette.exe'
        Demo = Join-Path $repo 'tools\Claudette.Demo\bin\Debug\net10.0\claudette-demo.exe'
    }
}

# Makes the demo's content in $work afresh, for the style and theme: still, and without the OS's notifications, which
# Claudette would send from behind the user's windows.
function New-DemoContent([string] $demo, [string] $work, [string] $projects, [string] $style, [string] $theme) {
    & $demo $work --theme $theme --style $style --projects $projects --still --quiet | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'claudette-demo failed.' }
}

# Starts Claudette on the demo in $work (fake-claude as Claude Code, so no account and no tokens), once it has a window,
# behind the user's windows and without the focus. $environment adds to what it starts with, such as
# FAKE_CLAUDE_LOGGED_IN = '0' for the sign-in screen.
function Start-Claudette([string] $app, [string] $work, [hashtable] $environment = @{}) {
    $start = New-Object System.Diagnostics.ProcessStartInfo $app
    $start.UseShellExecute = $false
    $start.EnvironmentVariables['CLAUDETTE_HOME'] = Join-Path $work 'home'
    $start.EnvironmentVariables['CLAUDE_CONFIG_DIR'] = Join-Path $work 'claude-config'
    $start.EnvironmentVariables['FAKE_CLAUDE_USAGE'] = Join-Path $work 'usage.json'
    $start.EnvironmentVariables['CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC'] = '1'
    # A source build otherwise copies itself to its data folder and runs from there.
    $start.EnvironmentVariables['CLAUDETTE_RUN_IN_PLACE'] = '1'
    $start.EnvironmentVariables['CLAUDETTE_UNFOCUSED'] = '1'
    foreach ($name in $environment.Keys) { $start.EnvironmentVariables[$name] = $environment[$name] }
    $claudette = [System.Diagnostics.Process]::Start($start)
    [FocusWatch]::Start($claudette.Id)
    Wait-Until { $claudette.Refresh(); $claudette.MainWindowHandle -ne [IntPtr]::Zero } 'the window'
    [WindowShot]::ToBack($claudette.MainWindowHandle)
    $claudette
}

# Closed by its window, so it stops each tab's Claude Code as it would for a user. Says if it came to the front meanwhile.
function Stop-Claudette($claudette) {
    $taken = [FocusWatch]::Stop()
    if ($taken) { Write-Warning "Claudette came to the front: $($taken -join ', ')." }
    $claudette.CloseMainWindow() | Out-Null
    if (-not $claudette.WaitForExit(30000)) { Write-Warning "Claudette (pid $($claudette.Id)) didn't close." }
}

Export-ModuleMember -Function Get-Root, Get-Parent, Find-ByText, Find-ByPrefix, Find-ByType, Find-ById, Find-Lowest, Show-Tree, Invoke-ByText, Invoke-Element,
    Select-TreeItem, Select-ListItem, Show-Element, Set-ElementText, Set-Toggle, Send-Message, Enter-Input, Exit-Input, Move-PointerTo, Send-Keys,
    Send-WindowKey, Set-Step, Get-OtherWindow, Wait-PointerAway, Save-Window, Get-Bounds, Save-Rect, Save-Elements, Save-Element, Close-Window,
    Wait-Until, Build-Claudette, New-DemoContent, Start-Claudette, Stop-Claudette
