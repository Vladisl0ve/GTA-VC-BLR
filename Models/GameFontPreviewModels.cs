namespace GTA_GXT_Editor.Models;

public sealed record FontMetricResolution(
    int MetricIndex,
    byte RoutedCode,
    FontTextureKind EffectiveFont,
    ushort BaseAdvance,
    ushort? ContextOverride,
    ushort EffectiveAdvance);

public sealed record PositionedGlyph(
    char Character,
    byte Code,
    byte GlyphCode,
    GlyphCell Cell,
    int X,
    int Y,
    ushort MetricAdvance,
    int Advance,
    int Scale)
{
    public int PixelWidth => checked(Cell.Width * Scale);

    public int PixelHeight => checked(Cell.Height * Scale);
}

public enum GameFontPreviewIssueKind
{
    InvalidMetrics,
    InvalidMapping,
    UnsupportedFont,
    CharacterCannotBeEncoded,
    MetricUnavailable,
    GlyphUnavailable,
}

public sealed record GameFontPreviewIssue(
    GameFontPreviewIssueKind Kind,
    int TextIndex,
    string Message,
    char? Character = null,
    byte? Code = null);

public sealed record GameFontLayoutResult(
    IReadOnlyList<PositionedGlyph> Glyphs,
    IReadOnlyList<GameFontPreviewIssue> Issues,
    FontTextureKind Font,
    FontRenderContext Context,
    int Scale,
    int LineHeight,
    int LineCount,
    int AdvanceWidth,
    int PixelWidth,
    int PixelHeight)
{
    public bool CanRender => Issues.All(issue =>
        issue.Kind is not GameFontPreviewIssueKind.InvalidMetrics and
        not GameFontPreviewIssueKind.InvalidMapping and
        not GameFontPreviewIssueKind.UnsupportedFont);
}

public sealed record GameFontPreviewBitmap(
    int Width,
    int Height,
    int Stride,
    byte[] PixelsBgra32);
