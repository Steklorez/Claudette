using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Deventry.Core.Settings;

namespace Deventry.App.Themes;

/// <summary>
/// The colors of Settings → Appearance → Style (DESIGN.md §3, "Visual style"). Standard is App.axaml's tokens, Rider's
/// Islands colors, and gives Fluent Rider's palette: its greys and blue. Claude puts <see cref="ClaudeColors"/> over those
/// tokens, and gives Fluent a palette of the Claude apps' warm greys, ivory and orange; Codex puts <see cref="CodexColors"/>
/// over them, with a palette of the ChatGPT and Codex apps' white, neutral greys and OpenAI's blue. Fluent draws with the
/// palette what it draws itself: window backgrounds, text, controls and the accent, whose lighter and darker shades it
/// works out from it. All three work in light and dark. The Claude and Codex styles' shapes are styles under the
/// "rounded" class (<see cref="HasRoundedShapes"/>), which the shell's <see cref="Controls.AppearanceScope"/> takes.
/// </summary>
public sealed class AppColors(Application app)
{
    /// <summary>Claude's orange.</summary>
    public static readonly Color ClaudeAccent = Color.Parse("#D97757");

    /// <summary>OpenAI's blue, from the palette in its design guidelines for apps in ChatGPT.</summary>
    public static readonly Color CodexAccent = Color.Parse("#0285FF");

    /// <summary>Rider's blue.</summary>
    public static readonly Color RiderAccent = Color.Parse("#3574F0");

    /// <summary>The Standard style's interface font, Rider's: Inter, which Deventry ships.</summary>
    public static readonly FontFamily InterFont = new("fonts:Inter#Inter, $Default");

    /// <summary>
    /// Serifs close to the Claude apps' reply typeface, most alike first, with one on each OS: Charter on macOS (and
    /// as Bitstream Charter on Linux), Georgia and Cambria on Windows, Noto Serif and DejaVu Serif on Linux.
    /// </summary>
    public const string SerifFonts = "Charter, Bitstream Charter, Georgia, Cambria, Noto Serif, DejaVu Serif";

    /// <summary>Standard's value of each token a style has changed, kept the first time one does, by theme.</summary>
    private readonly Dictionary<(ThemeVariant Variant, object Key), object?> _standard = [];
    private ClaudeColors? _claude;
    private CodexColors? _codex;

    /// <summary>The style applied; null until the first is.</summary>
    public AppStyle? Current { get; private set; }

    /// <summary>The interface's font: Inter in the Standard style, as Rider's; the system's in the Claude and Codex styles.</summary>
    public static FontFamily UiFont(AppStyle style) => style == AppStyle.Standard ? InterFont : FontFamily.Default;

    /// <summary>
    /// The font of Claude's replies: the conversation font when one is set, otherwise a serif in the Claude style, as
    /// the Claude apps set replies, and the interface's in Standard and Codex.
    /// </summary>
    public static FontFamily ReplyFont(AppStyle style, string? conversationFont) =>
        conversationFont is { } font ? new FontFamily($"{font}, {FontFamily.DefaultFontFamilyName}")
        : style == AppStyle.Claude ? new FontFamily($"{SerifFonts}, {FontFamily.DefaultFontFamilyName}")
        : UiFont(style);

    /// <summary>
    /// The Claude and Codex styles share the apps' rounder shapes: the "rounded" class on an AppearanceScope, which their
    /// styles look for.
    /// </summary>
    public static bool HasRoundedShapes(AppStyle style) => style != AppStyle.Standard;

    public void Apply(AppStyle style)
    {
        if (style == Current)
        {
            return;
        }
        Current = style;
        var chosen = Tokens(style);
        foreach (var (variant, key) in chosen.Keys)
        {
            if (!_standard.ContainsKey((variant, key)) && Target(variant) is { } tokens)
            {
                _standard[(variant, key)] = tokens.TryGetValue(key, out var original) ? original : null;
            }
        }
        // Every token a style has changed takes this style's value, or goes back to Standard's.
        foreach (var ((variant, key), original) in _standard)
        {
            if (Target(variant) is not { } tokens)
            {
                continue;
            }
            if (chosen.TryGetValue((variant, key), out var value))
            {
                tokens[key] = value;
            }
            else if (original is not null)
            {
                tokens[key] = original;
            }
            else
            {
                tokens.Remove(key);
            }
        }
        // Fluent reads most palette colors once, when its resources are first used, so a palette set on the running
        // theme would only change the accent. A fresh theme with the palette already set reads them all.
        if (app.Styles.OfType<FluentTheme>().FirstOrDefault() is not { } fluent)
        {
            return;
        }
        var fresh = new FluentTheme { DensityStyle = fluent.DensityStyle };
        fresh.Palettes[ThemeVariant.Light] = style switch
        {
            AppStyle.Claude => ClaudeLightPalette(),
            AppStyle.Codex => CodexLightPalette(),
            _ => RiderLightPalette(),
        };
        fresh.Palettes[ThemeVariant.Dark] = style switch
        {
            AppStyle.Claude => ClaudeDarkPalette(),
            AppStyle.Codex => CodexDarkPalette(),
            _ => RiderDarkPalette(),
        };
        app.Styles[app.Styles.IndexOf(fluent)] = fresh;
    }

    /// <summary>The app's tokens for one theme.</summary>
    private IResourceDictionary? Target(ThemeVariant variant) =>
        app.Resources.ThemeDictionaries.TryGetValue(variant, out var target) ? target as IResourceDictionary : null;

    /// <summary>The tokens a style puts over Standard's, by theme: none for Standard itself.</summary>
    private Dictionary<(ThemeVariant Variant, object Key), object?> Tokens(AppStyle style)
    {
        ResourceDictionary? colors = style switch
        {
            AppStyle.Claude => _claude ??= new ClaudeColors(),
            AppStyle.Codex => _codex ??= new CodexColors(),
            _ => null,
        };
        var tokens = new Dictionary<(ThemeVariant Variant, object Key), object?>();
        if (colors is null)
        {
            return tokens;
        }
        foreach (var (variant, provider) in colors.ThemeDictionaries)
        {
            if (provider is IResourceDictionary dictionary)
            {
                foreach (var key in dictionary.Keys.ToList())
                {
                    tokens[(variant, key)] = dictionary[key];
                }
            }
        }
        return tokens;
    }

    /// <summary>Rider's Islands Light: a light grey frame, black text, Rider's greys and blue.</summary>
    private static ColorPaletteResources RiderLightPalette() => new()
    {
        Accent = RiderAccent,
        RegionColor = Color.Parse("#EBECF0"),
        BaseHigh = Color.Parse("#000000"),
        BaseMediumHigh = Color.Parse("#CC000000"),
        BaseMedium = Color.Parse("#99000000"),
        BaseMediumLow = Color.Parse("#66000000"),
        BaseLow = Color.Parse("#33000000"),
        ChromeAltLow = Color.Parse("#000000"),
        ChromeDisabledHigh = Color.Parse("#EBECF0"),
        ChromeDisabledLow = Color.Parse("#A8ADBD"),
        ChromeGray = Color.Parse("#6C707E"),
        ChromeHigh = Color.Parse("#C9CCD6"),
        ChromeLow = Color.Parse("#F7F8FA"),
        ChromeMedium = Color.Parse("#EBECF0"),
        ChromeMediumLow = Color.Parse("#FFFFFF"),
        ListLow = Color.Parse("#0F000000"),
        ListMedium = Color.Parse("#1F000000"),
    };

    /// <summary>Rider's Islands Dark: a grey frame, darker islands, soft white text, Rider's greys and blue.</summary>
    private static ColorPaletteResources RiderDarkPalette() => new()
    {
        Accent = RiderAccent,
        RegionColor = Color.Parse("#26282C"),
        AltHigh = Color.Parse("#191A1C"),
        AltMediumHigh = Color.Parse("#CC191A1C"),
        AltMedium = Color.Parse("#99191A1C"),
        AltMediumLow = Color.Parse("#66191A1C"),
        AltLow = Color.Parse("#33191A1C"),
        BaseHigh = Color.Parse("#DFE1E5"),
        BaseMediumHigh = Color.Parse("#CCDFE1E5"),
        BaseMedium = Color.Parse("#99DFE1E5"),
        BaseMediumLow = Color.Parse("#66DFE1E5"),
        BaseLow = Color.Parse("#33DFE1E5"),
        ChromeAltLow = Color.Parse("#DFE1E5"),
        ChromeDisabledHigh = Color.Parse("#393B40"),
        ChromeDisabledLow = Color.Parse("#6F737A"),
        ChromeGray = Color.Parse("#868A91"),
        ChromeHigh = Color.Parse("#5A5D63"),
        ChromeLow = Color.Parse("#1E1F22"),
        ChromeMedium = Color.Parse("#2B2D30"),
        ChromeMediumLow = Color.Parse("#2B2D30"),
        ListLow = Color.Parse("#14FFFFFF"),
        ListMedium = Color.Parse("#29FFFFFF"),
    };

    /// <summary>The Claude apps' light: ivory page, warm greys, near-black text.</summary>
    private static ColorPaletteResources ClaudeLightPalette() => new()
    {
        Accent = ClaudeAccent,
        RegionColor = Color.Parse("#FAF9F5"),
        BaseHigh = Color.Parse("#141413"),
        BaseMediumHigh = Color.Parse("#CC141413"),
        BaseMedium = Color.Parse("#99141413"),
        BaseMediumLow = Color.Parse("#66141413"),
        BaseLow = Color.Parse("#33141413"),
        ChromeAltLow = Color.Parse("#141413"),
        ChromeBlackHigh = Color.Parse("#141413"),
        ChromeBlackMedium = Color.Parse("#99141413"),
        ChromeBlackMediumLow = Color.Parse("#66141413"),
        ChromeBlackLow = Color.Parse("#33141413"),
        ChromeDisabledHigh = Color.Parse("#E8E6DC"),
        ChromeDisabledLow = Color.Parse("#8A8880"),
        ChromeGray = Color.Parse("#73726C"),
        ChromeHigh = Color.Parse("#D6D3C8"),
        ChromeLow = Color.Parse("#F5F4ED"),
        ChromeMedium = Color.Parse("#EDEBE3"),
        ChromeMediumLow = Color.Parse("#FAF9F5"),
        ListLow = Color.Parse("#141F1E1D"),
        ListMedium = Color.Parse("#291F1E1D"),
    };

    /// <summary>The Claude apps' dark: charcoal page, darker sidebar, off-white text.</summary>
    private static ColorPaletteResources ClaudeDarkPalette() => new()
    {
        Accent = ClaudeAccent,
        RegionColor = Color.Parse("#262624"),
        AltHigh = Color.Parse("#262624"),
        AltMediumHigh = Color.Parse("#CC262624"),
        AltMedium = Color.Parse("#99262624"),
        AltMediumLow = Color.Parse("#66262624"),
        AltLow = Color.Parse("#33262624"),
        BaseHigh = Color.Parse("#FAF9F5"),
        BaseMediumHigh = Color.Parse("#CCFAF9F5"),
        BaseMedium = Color.Parse("#99FAF9F5"),
        BaseMediumLow = Color.Parse("#66FAF9F5"),
        BaseLow = Color.Parse("#33FAF9F5"),
        ChromeAltLow = Color.Parse("#FAF9F5"),
        ChromeDisabledHigh = Color.Parse("#3A3935"),
        ChromeDisabledLow = Color.Parse("#8A8880"),
        ChromeGray = Color.Parse("#9C9A92"),
        ChromeHigh = Color.Parse("#6B6A65"),
        ChromeLow = Color.Parse("#1F1E1D"),
        ChromeMedium = Color.Parse("#2B2A28"),
        ChromeMediumLow = Color.Parse("#30302E"),
        ListLow = Color.Parse("#19FAF9F5"),
        ListMedium = Color.Parse("#33FAF9F5"),
    };

    /// <summary>The ChatGPT and Codex apps' light: white page, neutral greys, near-black text.</summary>
    private static ColorPaletteResources CodexLightPalette() => new()
    {
        Accent = CodexAccent,
        RegionColor = Color.Parse("#FFFFFF"),
        BaseHigh = Color.Parse("#0D0D0D"),
        BaseMediumHigh = Color.Parse("#CC0D0D0D"),
        BaseMedium = Color.Parse("#990D0D0D"),
        BaseMediumLow = Color.Parse("#660D0D0D"),
        BaseLow = Color.Parse("#330D0D0D"),
        ChromeAltLow = Color.Parse("#0D0D0D"),
        ChromeBlackHigh = Color.Parse("#0D0D0D"),
        ChromeBlackMedium = Color.Parse("#990D0D0D"),
        ChromeBlackMediumLow = Color.Parse("#660D0D0D"),
        ChromeBlackLow = Color.Parse("#330D0D0D"),
        ChromeDisabledHigh = Color.Parse("#E8E8E8"),
        ChromeDisabledLow = Color.Parse("#8F8F8F"),
        ChromeGray = Color.Parse("#5D5D5D"),
        ChromeHigh = Color.Parse("#D4D4D4"),
        ChromeLow = Color.Parse("#F9F9F9"),
        ChromeMedium = Color.Parse("#F3F3F3"),
        ChromeMediumLow = Color.Parse("#FFFFFF"),
        ListLow = Color.Parse("#140D0D0D"),
        ListMedium = Color.Parse("#290D0D0D"),
    };

    /// <summary>The ChatGPT and Codex apps' dark: grey page, darker sidebar, white text.</summary>
    private static ColorPaletteResources CodexDarkPalette() => new()
    {
        Accent = CodexAccent,
        RegionColor = Color.Parse("#212121"),
        AltHigh = Color.Parse("#212121"),
        AltMediumHigh = Color.Parse("#CC212121"),
        AltMedium = Color.Parse("#99212121"),
        AltMediumLow = Color.Parse("#66212121"),
        AltLow = Color.Parse("#33212121"),
        BaseHigh = Color.Parse("#FFFFFF"),
        BaseMediumHigh = Color.Parse("#CCFFFFFF"),
        BaseMedium = Color.Parse("#99FFFFFF"),
        BaseMediumLow = Color.Parse("#66FFFFFF"),
        BaseLow = Color.Parse("#33FFFFFF"),
        ChromeAltLow = Color.Parse("#FFFFFF"),
        ChromeDisabledHigh = Color.Parse("#303030"),
        ChromeDisabledLow = Color.Parse("#8F8F8F"),
        ChromeGray = Color.Parse("#AFAFAF"),
        ChromeHigh = Color.Parse("#5D5D5D"),
        ChromeLow = Color.Parse("#181818"),
        ChromeMedium = Color.Parse("#2A2A2A"),
        ChromeMediumLow = Color.Parse("#303030"),
        ListLow = Color.Parse("#19FFFFFF"),
        ListMedium = Color.Parse("#33FFFFFF"),
    };
}
