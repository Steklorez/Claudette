using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Claudette.Platform.Shell.Windows;

/// <summary>
/// Keeps a window behind every other one, unfocused (DESIGN.md §9, "Taking screenshots without the focus"): each move
/// in the z-order a window is about to make, which <c>WM_WINDOWPOSCHANGING</c> announces, becomes a move to the bottom
/// without activating it. So a Claudette window that opens, or would come forward, while the screenshot scripts drive
/// it never shows over the user's windows, a fullscreen one included; PrintWindow captures it behind them all the same.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class WindowsBehind
{
    /// <summary><c>WM_WINDOWPOSCHANGING</c>: the window's size, place or place in the z-order is about to change.</summary>
    public const uint WindowPosChanging = 0x0046;

    private const uint NoSize = 0x0001;
    private const uint NoMove = 0x0002;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;
    private static readonly IntPtr Bottom = new(1);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Window;
        public IntPtr InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }

    /// <summary>
    /// Rewrites the <c>WINDOWPOS</c> that <paramref name="windowPos"/> (a <c>WM_WINDOWPOSCHANGING</c>'s lParam) points to,
    /// so a move in the z-order goes to the bottom and doesn't activate the window. A move that leaves the z-order alone
    /// is left as it is.
    /// </summary>
    public static void KeepBehind(IntPtr windowPos)
    {
        var pos = Marshal.PtrToStructure<WindowPos>(windowPos);
        if ((pos.Flags & NoZOrder) != 0)
        {
            return;
        }
        pos.InsertAfter = Bottom;
        pos.Flags |= NoActivate;
        Marshal.StructureToPtr(pos, windowPos, false);
    }

    /// <summary>Puts the window behind every other one now, without activating it: for a popup already shown.</summary>
    public static void SendToBack(IntPtr window) => SetWindowPos(window, Bottom, 0, 0, 0, 0, NoSize | NoMove | NoActivate);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
