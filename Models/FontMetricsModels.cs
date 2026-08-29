using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Models;

public sealed class FontMetricsProfile
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public required FontMetricsTable Font2 { get; init; }

    public required FontMetricsTable Font1 { get; init; }

    public List<FontMetricOverride> Overrides { get; init; } = [];

    public FontMetricsProfile Clone() => new()
    {
        Version = Version,
        Font2 = Font2.Clone(),
        Font1 = Font1.Clone(),
        Overrides = Overrides.Select(item => item.Clone()).ToList(),
    };
}

public sealed class FontMetricsTable
{
    public const int MetricCount = 210;

    public required ushort[] Advances { get; init; }

    public FontMetricsTable Clone() => new()
    {
        Advances = Advances.ToArray(),
    };
}

public sealed class FontMetricOverride
{
    public required FontRenderContext Context { get; init; }

    public required FontTextureKind Font { get; init; }

    public required byte Code { get; init; }

    public required ushort Advance { get; init; }

    public FontMetricOverride Clone() => new()
    {
        Context = Context,
        Font = Font,
        Code = Code,
        Advance = Advance,
    };
}

public enum FontRenderContext
{
    Default,
    Gameplay,
    Subtitles,
    MainMenu,
    SaveLoad,
    ExitConfirmation,

    // Persisted Heading overrides are a compatibility representation for known
    // Belarusian T/t widths. Vice City actually remaps those glyphs to index 198.
    Heading,
}

public enum FontTextureKind
{
    Font1,
    Font2,
}

public static class FontMetricsPresets
{
    public static FontMetricsProfile BelarusianViceCity =>
        BundledFontMetricsProvider.BelarusianViceCity;
}
