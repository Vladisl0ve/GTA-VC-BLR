using System.IO;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class FontMetricsService
{
    public const int HeadingBelarusianTMetricIndex = 198;
    private const int HeadingBlankGlyphMetricIndex = 208;

    public static int GetMetricIndex(byte code)
    {
        if (code is < FontMetricsValidator.MinimumCode or > FontMetricsValidator.MaximumCode)
        {
            throw new ArgumentOutOfRangeException(
                nameof(code),
                code,
                $"Font metric code must be in the 0x{FontMetricsValidator.MinimumCode:X2}-0x{FontMetricsValidator.MaximumCode:X2} range.");
        }

        return code - FontMetricsValidator.MinimumCode;
    }

    public static ushort GetBaseAdvance(
        FontMetricsProfile profile,
        FontTextureKind font,
        byte code)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateFont(font);
        var metricIndex = GetMetricIndex(code);
        var table = font switch
        {
            FontTextureKind.Font1 => profile.Font1,
            FontTextureKind.Font2 => profile.Font2,
            _ => throw new ArgumentOutOfRangeException(nameof(font), font, "Unsupported font texture kind."),
        };

        if (table?.Advances is null)
        {
            throw new InvalidDataException($"Font metrics table {font} is missing.");
        }

        if (table.Advances.Length != FontMetricsTable.MetricCount)
        {
            throw new InvalidDataException(
                $"Font metrics table {font} must contain exactly {FontMetricsTable.MetricCount} advances.");
        }

        return table.Advances[metricIndex];
    }

    public static ushort? GetContextOverrideAdvance(
        FontMetricsProfile profile,
        FontTextureKind font,
        byte code,
        FontRenderContext context)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateFont(font);
        ValidateContext(context);
        _ = GetMetricIndex(code);

        // Default is the canonical extracted table before runtime context writes.
        if (context == FontRenderContext.Default)
        {
            return null;
        }

        if (profile.Overrides is null)
        {
            throw new InvalidDataException("Font metrics overrides are missing.");
        }

        FontMetricOverride? match = null;
        foreach (var item in profile.Overrides)
        {
            if (item is null || item.Context != context || item.Font != font || item.Code != code)
            {
                continue;
            }

            if (match is not null)
            {
                throw new InvalidDataException(
                    $"Font metric override for {context}, {font}, code 0x{code:X2} is duplicated.");
            }

            match = item;
        }

        return match?.Advance;
    }

    public static ushort GetAdvance(
        FontMetricsProfile profile,
        FontTextureKind font,
        byte code,
        FontRenderContext context = FontRenderContext.Default) =>
        Resolve(profile, font, code, context).EffectiveAdvance;

    // Compatibility overload for callers that use the version-1 combined model.
    // Persisted Heading overrides remain valid data, but heading rendering itself
    // is resolved through Vice City's complete character-routing table.
    public static FontMetricResolution Resolve(
        FontMetricsProfile profile,
        FontTextureKind font,
        byte code,
        FontRenderContext context = FontRenderContext.Default)
    {
        ValidateFont(font);
        ValidateContext(context);
        var style = context == FontRenderContext.Heading
            ? ViceCityFontStyle.Heading
            : GetStyle(font);
        var metricContext = context == FontRenderContext.Heading
            ? FontRenderContext.Default
            : context;
        return Resolve(profile, style, code, metricContext);
    }

    public static FontMetricResolution Resolve(
        FontMetricsProfile profile,
        ViceCityFontStyle style,
        byte code,
        FontRenderContext context = FontRenderContext.Default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateStyle(style);
        ValidateRuntimeContext(context);

        var originalMetricIndex = GetMetricIndex(code);
        var metricIndex = style == ViceCityFontStyle.Heading
            ? RouteHeadingMetricIndex(originalMetricIndex)
            : originalMetricIndex;
        var metricCode = GetMetricCode(metricIndex);
        var glyphMetricIndex = style == ViceCityFontStyle.Heading &&
                               metricIndex == HeadingBlankGlyphMetricIndex
            ? 0
            : metricIndex;
        var glyphCode = GetMetricCode(glyphMetricIndex);
        var effectiveFont = GetEffectiveFont(style);
        var baseAdvance = GetBaseAdvance(profile, effectiveFont, metricCode);
        var contextOverride = GetContextOverrideAdvance(
            profile,
            effectiveFont,
            metricCode,
            context);
        return new FontMetricResolution(
            metricIndex,
            metricCode,
            glyphCode,
            style,
            effectiveFont,
            baseAdvance,
            contextOverride,
            contextOverride ?? baseAdvance);
    }

    public static FontTextureKind GetEffectiveFont(ViceCityFontStyle style)
    {
        ValidateStyle(style);
        return style == ViceCityFontStyle.Bank
            ? FontTextureKind.Font2
            : FontTextureKind.Font1;
    }

    public static ViceCityFontStyle GetStyle(FontTextureKind font)
    {
        ValidateFont(font);
        return font == FontTextureKind.Font2
            ? ViceCityFontStyle.Bank
            : ViceCityFontStyle.Standard;
    }

    public static string GetTextureName(FontTextureKind font)
    {
        ValidateFont(font);
        return font == FontTextureKind.Font1 ? "font1" : "font2";
    }

    public static bool IsRuntimeContext(FontRenderContext context) =>
        Enum.IsDefined(context) && context != FontRenderContext.Heading;

    public static int RouteHeadingMetricIndex(int metricIndex)
    {
        if ((uint)metricIndex >= FontMetricsTable.MetricCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(metricIndex),
                metricIndex,
                $"Font metric index must be in the 0-{FontMetricsTable.MetricCount - 1} range.");
        }

        // Exact CFont::FindNewCharacter routing from classic Vice City. The input
        // is already code - 0x20, matching the value used by the game renderer.
        return metricIndex switch
        {
            >= 16 and <= 26 => metricIndex + 128,
            >= 8 and <= 9 => metricIndex + 86,
            4 => 93,
            7 => 206,
            14 => 207,
            >= 33 and <= 58 => metricIndex + 122,
            >= 65 and <= 90 => metricIndex + 90,
            >= 96 and <= 118 => metricIndex + 85,
            >= 119 and <= 140 => metricIndex + 62,
            >= 141 and <= 142 => 204,
            143 => 205,
            1 => HeadingBlankGlyphMetricIndex,
            _ => metricIndex,
        };
    }

    private static byte GetMetricCode(int metricIndex) => checked((byte)(
        FontMetricsValidator.MinimumCode + metricIndex));

    private static void ValidateFont(FontTextureKind font)
    {
        if (!Enum.IsDefined(font))
        {
            throw new ArgumentOutOfRangeException(nameof(font), font, "Unsupported font texture kind.");
        }
    }

    private static void ValidateContext(FontRenderContext context)
    {
        if (!Enum.IsDefined(context))
        {
            throw new ArgumentOutOfRangeException(nameof(context), context, "Unsupported font render context.");
        }
    }

    private static void ValidateRuntimeContext(FontRenderContext context)
    {
        ValidateContext(context);
        if (context == FontRenderContext.Heading)
        {
            throw new ArgumentOutOfRangeException(
                nameof(context),
                context,
                "Heading is a Vice City font style, not a runtime metrics context.");
        }
    }

    private static void ValidateStyle(ViceCityFontStyle style)
    {
        if (!Enum.IsDefined(style))
        {
            throw new ArgumentOutOfRangeException(nameof(style), style, "Unsupported Vice City font style.");
        }
    }
}
