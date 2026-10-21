using System.Globalization;

namespace Claudette.Demo;

/// <summary>
/// Stands in for the Unreal editor, for the guide's picture of an editor's log under its tab (DESIGN.md §18, "Editor
/// logs"): <c>guide-screenshots.ps1</c> runs a copy of claudette-demo named <c>UnrealEditor</c> with
/// <c>--stand-in-editor &lt;uproject&gt;</c>, so Claudette sees an Unreal editor with the demo's project open. It writes
/// the project's <c>Saved/Logs/&lt;Name&gt;.log</c> as the editor starts one, its times in UTC as Unreal writes them by
/// default, with a line of each verbosity for the log's filter, then waits, as an open editor does, until it's ended, or
/// for ten minutes at most, so it never stays behind.
/// </summary>
internal static class StandInEditor
{
    private static readonly TimeSpan Longest = TimeSpan.FromMinutes(10);

    public static int Run(string uproject)
    {
        var name = Path.GetFileNameWithoutExtension(uproject);
        var logs = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(uproject))!, "Saved", "Logs");
        Directory.CreateDirectory(logs);
        var now = DateTime.Now;
        string At(int milliseconds) => now.ToUniversalTime().AddMilliseconds(milliseconds).ToString("[yyyy.MM.dd-HH.mm.ss:fff]", CultureInfo.InvariantCulture);
        var lines = new List<string> { $"Log file open, {now.ToString("MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture)}" };
        var frame = 0;
        void Line(int milliseconds, string text) => lines.Add($"{At(milliseconds)}[{frame,3}]{text}");
        Line(12, "LogInit: Display: Running engine for game: Starfall");
        Line(14, "LogInit: Display: Engine Version: 5.6.0-0+++UE5+Release-5.6");
        Line(15, $"LogInit: Display: Project file: {Path.GetFullPath(uproject).Replace('\\', '/')}");
        Line(310, "LogConfig: Applying CVar settings from Section [/Script/Engine.RendererSettings] File [Engine]");
        Line(840, "LogModuleManager: Display: Loaded module Starfall");
        Line(1210, "LogAssetRegistry: Display: Asset registry cache read as 38.2 MiB");
        Line(1630, "LogShaderCompilers: Display: Using 14 local workers for shader compilation");
        Line(2040, "LogStreaming: Verbose: Async loading 212 packages for /Game/Maps/TrainingCourse");
        frame = 1;
        Line(3910, "LogTemp: Warning: GrappleComponent: no surface on BP_TrainingCourse is tagged GrappleAnchor");
        Line(3920, "LogBlueprint: Error: [AssetLog] /Game/UI/WBP_GrappleHUD: Missing function GetGrappleCharge on GrappleComponent");
        lines.Add("  Is GetGrappleCharge private, or was it renamed?");
        Line(4410, "LogLoad: Took 4.41 seconds to LoadMap(/Game/Maps/TrainingCourse)");
        frame = 212;
        Line(9050, "LogPlayLevel: Display: Starting PIE (Play In Editor) session...");
        Line(9810, "LogGrapple: Display: Fired along the camera's aim, hit at 1840.0 uu");
        Line(10420, "LogGrapple: Display: Pulling toward the anchor, 1200.0 uu/s");
        Line(11030, "LogGrapple: Display: Let go on jump");
        Line(11660, "LogTemp: Warning: GrappleComponent: pull force clamped on a slope of 62 degrees");
        Line(12300, "LogPlayLevel: Display: Shutting down PIE...");
        File.WriteAllLines(Path.Combine(logs, $"{name}.log"), lines);
        Thread.Sleep(Longest);
        return 0;
    }
}
