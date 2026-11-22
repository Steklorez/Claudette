using System.Buffers.Binary;

namespace Deventry.Platform.Presence.Windows;

/// <summary>
/// What <c>WTSQuerySessionInformation</c>'s <c>WTSSessionInfoEx</c> says, read from its bytes: a <c>WTSINFOEXW</c>, its
/// level, then <c>WTSINFOEX_LEVEL1_W</c>'s session id, connect state and flags. The level's union starts 8 bytes in,
/// because <c>WTSINFOEX_LEVEL1_W</c> holds <c>LARGE_INTEGER</c> times further on.
/// </summary>
internal static class WtsSessionInfo
{
    private const int LevelOne = 1;
    private const int FlagsOffset = 16;

    /// <summary><c>WTS_SESSIONSTATE_LOCK</c>; <c>WTS_SESSIONSTATE_UNLOCK</c> is 1 and <c>WTS_SESSIONSTATE_UNKNOWN</c> -1.</summary>
    private const int SessionStateLock = 0;

    /// <summary>
    /// The session is locked. Not its connect state, 4 bytes before, which is 0 (<c>WTSActive</c>) for every signed-in
    /// session: read there, every Windows computer was taken for locked.
    /// </summary>
    public static bool IsLocked(ReadOnlySpan<byte> info) =>
        info.Length >= FlagsOffset + sizeof(int)
        && BinaryPrimitives.ReadInt32LittleEndian(info) == LevelOne
        && BinaryPrimitives.ReadInt32LittleEndian(info[FlagsOffset..]) == SessionStateLock;
}
