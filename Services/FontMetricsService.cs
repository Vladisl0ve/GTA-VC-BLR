using System.IO;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class FontMetricsService
{
    public const int HeadingBelarusianTMetricIndex = 198;

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

    public static FontMetricResolution Resolve(
        FontMetricsProfile profile,
        FontTextureKind font,
        byte code,
        FontRenderContext context = FontRenderContext.Default)
    {
        ValidateFont(font);
        ValidateContext(context);
        _ = GetMetricIndex(code);

        var effectiveFont = context == FontRenderContext.Heading
            ? FontTextureKind.Font1
            : font;
        var routedCode = context == FontRenderContext.Heading && code is 0x91 or 0xA8
            ? checked((byte)(HeadingBelarusianTMetricIndex + FontMetricsValidator.MinimumCode))
            : code;
        var metricIndex = GetMetricIndex(routedCode);
        var baseAdvance = GetBaseAdvance(profile, effectiveFont, routedCode);
        var contextOverride = GetContextOverrideAdvance(
            profile,
            effectiveFont,
            code,
            context);
        return new FontMetricResolution(
            metricIndex,
            routedCode,
            effectiveFont,
            baseAdvance,
            contextOverride,
            contextOverride ?? baseAdvance);
    }

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
}
