using System.IO;
using System.Runtime.CompilerServices;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public sealed class GameFontPreviewService
{
    private static readonly IReadOnlyDictionary<byte, char> EmptyCharacterMap =
        new Dictionary<byte, char>();

    private readonly ConditionalWeakTable<TxdTexture, AtlasCache> _atlasCaches = new();

    public GameFontLayoutResult Layout(
        string text,
        TxdTexture texture,
        CharacterMapProfile characterMap,
        FontMetricsProfile metrics,
        ViceCityFontStyle style,
        FontRenderContext context = FontRenderContext.Default,
        int scale = 1)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(characterMap);
        ArgumentNullException.ThrowIfNull(metrics);
        ValidateScale(scale);

        var font = Enum.IsDefined(style)
            ? FontMetricsService.GetEffectiveFont(style)
            : default;
        var fatalIssues = ValidateInputs(texture, characterMap, metrics, style, context);
        if (fatalIssues.Count > 0)
        {
            return EmptyLayout(style, font, context, scale, fatalIssues);
        }

        var atlas = _atlasCaches.GetValue(texture, CreateAtlasCache);
        var lineHeight = checked(atlas.LineHeight * scale);
        var encodeMap = characterMap.ToEncodeMap();
        var glyphs = new List<PositionedGlyph>(text.Length);
        var issues = new List<GameFontPreviewIssue>();
        var x = 0;
        var y = 0;
        var maximumAdvanceWidth = 0;
        var maximumPixelWidth = 0;
        var lineCount = 1;

        for (var textIndex = 0; textIndex < text.Length;)
        {
            if (TryConsumeTokenOrNewline(text, ref textIndex, out var isNewline))
            {
                if (isNewline)
                {
                    maximumAdvanceWidth = Math.Max(maximumAdvanceWidth, x);
                    x = 0;
                    y = checked(y + lineHeight);
                    lineCount++;
                }

                continue;
            }

            var characterIndex = textIndex;
            var character = text[textIndex++];
            if (!TryEncode(character, encodeMap, out var code))
            {
                issues.Add(new GameFontPreviewIssue(
                    GameFontPreviewIssueKind.CharacterCannotBeEncoded,
                    characterIndex,
                    $"Character \"{character}\" cannot be encoded by the current mapping.",
                    character));
                continue;
            }

            FontMetricResolution metric;
            try
            {
                metric = FontMetricsService.Resolve(metrics, style, code, context);
            }
            catch (ArgumentOutOfRangeException)
            {
                issues.Add(new GameFontPreviewIssue(
                    GameFontPreviewIssueKind.MetricUnavailable,
                    characterIndex,
                    $"Code 0x{code:X2} for character \"{character}\" has no font metric.",
                    character,
                    code));
                continue;
            }

            if (!atlas.CellsByCode.TryGetValue(metric.GlyphCode, out var cell))
            {
                issues.Add(new GameFontPreviewIssue(
                    GameFontPreviewIssueKind.GlyphUnavailable,
                    characterIndex,
                    $"Code 0x{metric.GlyphCode:X2} for character \"{character}\" has no glyph in texture {texture.Name}.",
                    character,
                    metric.GlyphCode));
                continue;
            }

            var scaledAdvance = checked(metric.EffectiveAdvance * scale);
            var glyph = new PositionedGlyph(
                character,
                code,
                metric.GlyphCode,
                cell,
                x,
                y,
                metric.EffectiveAdvance,
                scaledAdvance,
                scale);
            glyphs.Add(glyph);
            maximumPixelWidth = Math.Max(maximumPixelWidth, checked(x + glyph.PixelWidth));
            x = checked(x + scaledAdvance);
        }

        maximumAdvanceWidth = Math.Max(maximumAdvanceWidth, x);
        maximumPixelWidth = Math.Max(maximumPixelWidth, maximumAdvanceWidth);
        var hasLayoutHeight = text.Length > 0;
        var pixelHeight = hasLayoutHeight ? checked(lineCount * lineHeight) : 0;

        return new GameFontLayoutResult(
            glyphs.ToArray(),
            issues.ToArray(),
            style,
            font,
            context,
            scale,
            lineHeight,
            hasLayoutHeight ? lineCount : 0,
            maximumAdvanceWidth,
            maximumPixelWidth,
            pixelHeight);
    }

    public static GameFontPreviewBitmap Render(
        TxdTexture texture,
        GameFontLayoutResult layout,
        bool showMetricsGuides = false)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(layout);

        if (layout.PixelWidth == 0 || layout.PixelHeight == 0)
        {
            return new GameFontPreviewBitmap(0, 0, 0, []);
        }

        var expectedName = FontMetricsService.GetTextureName(layout.Font);
        if (!texture.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Layout for {expectedName} cannot be rendered from texture {texture.Name}.",
                nameof(texture));
        }

        var source = texture.PreviewPixelsBgra32.Length > 0
            ? texture.PreviewPixelsBgra32
            : texture.PixelsBgra32;
        var requiredSourceLength = checked(texture.Width * texture.Height * 4);
        if (source.Length != requiredSourceLength)
        {
            throw new InvalidDataException(
                $"Texture {texture.Name} must contain exactly {requiredSourceLength} BGRA32 bytes.");
        }

        var width = showMetricsGuides ? checked(layout.PixelWidth + 1) : layout.PixelWidth;
        var height = layout.PixelHeight;
        var stride = checked(width * 4);
        var target = new byte[checked(stride * height)];
        foreach (var glyph in layout.Glyphs)
        {
            DrawGlyphNearestNeighbor(source, texture.Width, target, width, height, glyph);
        }

        if (showMetricsGuides)
        {
            foreach (var glyph in layout.Glyphs)
            {
                var bottom = Math.Min(height, checked(glyph.Y + layout.LineHeight));
                DrawGuide(target, width, glyph.X, glyph.Y, bottom);
                DrawGuide(target, width, checked(glyph.X + glyph.Advance), glyph.Y, bottom);
            }
        }

        return new GameFontPreviewBitmap(width, height, stride, target);
    }

    private static List<GameFontPreviewIssue> ValidateInputs(
        TxdTexture texture,
        CharacterMapProfile characterMap,
        FontMetricsProfile metrics,
        ViceCityFontStyle style,
        FontRenderContext context)
    {
        var issues = new List<GameFontPreviewIssue>();
        if (!Enum.IsDefined(style) || !FontMetricsService.IsRuntimeContext(context))
        {
            issues.Add(new GameFontPreviewIssue(
                GameFontPreviewIssueKind.UnsupportedFont,
                -1,
                "The selected font or render context is unsupported."));
            return issues;
        }

        var font = FontMetricsService.GetEffectiveFont(style);
        var expectedName = FontMetricsService.GetTextureName(font);
        if (!texture.Name.Equals(expectedName, StringComparison.OrdinalIgnoreCase) || !texture.IsFontAtlas)
        {
            issues.Add(new GameFontPreviewIssue(
                GameFontPreviewIssueKind.UnsupportedFont,
                -1,
                $"Texture {texture.Name} cannot be used as Vice City {expectedName}."));
        }

        foreach (var message in FontMetricsValidator.Validate(metrics))
        {
            issues.Add(new GameFontPreviewIssue(
                GameFontPreviewIssueKind.InvalidMetrics,
                -1,
                message));
        }

        foreach (var message in CharacterMapService.Validate(characterMap))
        {
            issues.Add(new GameFontPreviewIssue(
                GameFontPreviewIssueKind.InvalidMapping,
                -1,
                message));
        }

        return issues;
    }

    private static GameFontLayoutResult EmptyLayout(
        ViceCityFontStyle style,
        FontTextureKind font,
        FontRenderContext context,
        int scale,
        IReadOnlyList<GameFontPreviewIssue> issues) => new(
            [],
            issues,
            style,
            font,
            context,
            scale,
            0,
            0,
            0,
            0,
            0);

    private static AtlasCache CreateAtlasCache(TxdTexture texture)
    {
        var cells = GlyphAtlasService.CreateCells(
            texture,
            EmptyCharacterMap,
            GXTType.GtaViceCity);
        var cellsByCode = cells
            .Where(cell => cell.Code.HasValue)
            .ToDictionary(cell => cell.Code!.Value);
        return new AtlasCache(cellsByCode, cells.Count == 0 ? 0 : cells.Max(cell => cell.Height));
    }

    private static bool TryConsumeTokenOrNewline(
        string text,
        ref int textIndex,
        out bool isNewline)
    {
        isNewline = false;
        if (text[textIndex] is '\r' or '\n')
        {
            if (text[textIndex] == '\r' &&
                textIndex + 1 < text.Length &&
                text[textIndex + 1] == '\n')
            {
                textIndex += 2;
            }
            else
            {
                textIndex++;
            }

            isNewline = true;
            return true;
        }

        if (text[textIndex] != '~')
        {
            return false;
        }

        var tokenEnd = text.IndexOf('~', textIndex + 1);
        if (tokenEnd < 0)
        {
            return false;
        }

        var token = text.AsSpan(textIndex + 1, tokenEnd - textIndex - 1);
        isNewline = token.Equals("n", StringComparison.OrdinalIgnoreCase);
        textIndex = tokenEnd + 1;
        return true;
    }

    private static bool TryEncode(
        char character,
        IReadOnlyDictionary<char, byte> encodeMap,
        out byte code)
    {
        if (encodeMap.TryGetValue(character, out code))
        {
            return true;
        }

        if (character is >= ' ' and <= '~')
        {
            code = (byte)character;
            return true;
        }

        code = 0;
        return false;
    }

    private static void DrawGlyphNearestNeighbor(
        byte[] source,
        int sourceWidth,
        byte[] target,
        int targetWidth,
        int targetHeight,
        PositionedGlyph glyph)
    {
        for (var sourceY = 0; sourceY < glyph.Cell.Height; sourceY++)
        {
            for (var sourceX = 0; sourceX < glyph.Cell.Width; sourceX++)
            {
                var sourceOffset = checked(
                    ((glyph.Cell.Y + sourceY) * sourceWidth + glyph.Cell.X + sourceX) * 4);
                for (var scaleY = 0; scaleY < glyph.Scale; scaleY++)
                {
                    var targetY = checked(glyph.Y + sourceY * glyph.Scale + scaleY);
                    if ((uint)targetY >= (uint)targetHeight)
                    {
                        continue;
                    }

                    for (var scaleX = 0; scaleX < glyph.Scale; scaleX++)
                    {
                        var targetX = checked(glyph.X + sourceX * glyph.Scale + scaleX);
                        if ((uint)targetX >= (uint)targetWidth)
                        {
                            continue;
                        }

                        var targetOffset = checked((targetY * targetWidth + targetX) * 4);
                        BlendPixel(source, sourceOffset, target, targetOffset);
                    }
                }
            }
        }
    }

    private static void BlendPixel(byte[] source, int sourceOffset, byte[] target, int targetOffset)
    {
        var sourceAlpha = source[sourceOffset + 3];
        if (sourceAlpha == 0)
        {
            return;
        }

        if (sourceAlpha == byte.MaxValue || target[targetOffset + 3] == 0)
        {
            Buffer.BlockCopy(source, sourceOffset, target, targetOffset, 4);
            return;
        }

        var destinationAlpha = target[targetOffset + 3];
        var inverseSourceAlpha = byte.MaxValue - sourceAlpha;
        var outputAlphaNumerator =
            sourceAlpha * byte.MaxValue + destinationAlpha * inverseSourceAlpha;
        var outputAlpha = (outputAlphaNumerator + 127) / byte.MaxValue;
        for (var channel = 0; channel < 3; channel++)
        {
            var outputChannelNumerator =
                source[sourceOffset + channel] * sourceAlpha * byte.MaxValue +
                target[targetOffset + channel] * destinationAlpha * inverseSourceAlpha;
            target[targetOffset + channel] = checked((byte)(
                (outputChannelNumerator + outputAlphaNumerator / 2) / outputAlphaNumerator));
        }

        target[targetOffset + 3] = checked((byte)outputAlpha);
    }

    private static void DrawGuide(byte[] target, int width, int x, int top, int bottom)
    {
        if ((uint)x >= (uint)width)
        {
            return;
        }

        for (var y = Math.Max(0, top); y < bottom; y++)
        {
            var offset = checked((y * width + x) * 4);
            target[offset] = 0xFF;
            target[offset + 1] = 0x80;
            target[offset + 2] = 0x00;
            target[offset + 3] = 0xFF;
        }
    }

    private static void ValidateScale(int scale)
    {
        if (scale is not (1 or 2 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Preview scale must be 1, 2, or 4.");
        }
    }

    private sealed record AtlasCache(
        IReadOnlyDictionary<byte, GlyphCell> CellsByCode,
        int LineHeight);
}
