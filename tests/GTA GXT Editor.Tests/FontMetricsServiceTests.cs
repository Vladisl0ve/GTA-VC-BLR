using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class FontMetricsServiceTests
{
    [TestMethod]
    public void GetMetricIndex_UsesPrintableCodeBounds()
    {
        Assert.AreEqual(0, FontMetricsService.GetMetricIndex(0x20));
        Assert.AreEqual(FontMetricsTable.MetricCount - 1, FontMetricsService.GetMetricIndex(0xF1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FontMetricsService.GetMetricIndex(0x1F));
        Assert.Throws<ArgumentOutOfRangeException>(() => FontMetricsService.GetMetricIndex(0xF2));
    }

    [TestMethod]
    public void Resolve_ReturnsBaseOverrideAndEffectiveAdvance()
    {
        var profile = CreateProfile();
        SetAdvance(profile, FontTextureKind.Font1, 0x54, 14);
        profile.Overrides.Add(new FontMetricOverride
        {
            Context = FontRenderContext.Gameplay,
            Font = FontTextureKind.Font1,
            Code = 0x54,
            Advance = 13,
        });

        var defaultMetric = FontMetricsService.Resolve(
            profile,
            ViceCityFontStyle.Standard,
            0x54);
        var gameplayMetric = FontMetricsService.Resolve(
            profile,
            ViceCityFontStyle.Standard,
            0x54,
            FontRenderContext.Gameplay);
        var menuMetric = FontMetricsService.Resolve(
            profile,
            ViceCityFontStyle.Standard,
            0x54,
            FontRenderContext.MainMenu);

        Assert.AreEqual(0x54 - 0x20, defaultMetric.MetricIndex);
        Assert.AreEqual((ushort)14, defaultMetric.BaseAdvance);
        Assert.IsNull(defaultMetric.ContextOverride);
        Assert.AreEqual((ushort)14, defaultMetric.EffectiveAdvance);
        Assert.AreEqual((ushort?)13, gameplayMetric.ContextOverride);
        Assert.AreEqual((ushort)13, gameplayMetric.EffectiveAdvance);
        Assert.IsNull(menuMetric.ContextOverride);
        Assert.AreEqual((ushort)14, menuMetric.EffectiveAdvance);
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(1, 208)]
    [DataRow(2, 2)]
    [DataRow(4, 93)]
    [DataRow(7, 206)]
    [DataRow(8, 94)]
    [DataRow(9, 95)]
    [DataRow(10, 10)]
    [DataRow(14, 207)]
    [DataRow(15, 15)]
    [DataRow(16, 144)]
    [DataRow(26, 154)]
    [DataRow(27, 27)]
    [DataRow(33, 155)]
    [DataRow(58, 180)]
    [DataRow(59, 59)]
    [DataRow(65, 155)]
    [DataRow(90, 180)]
    [DataRow(91, 91)]
    [DataRow(96, 181)]
    [DataRow(118, 203)]
    [DataRow(119, 181)]
    [DataRow(140, 202)]
    [DataRow(141, 204)]
    [DataRow(142, 204)]
    [DataRow(143, 205)]
    [DataRow(144, 144)]
    [DataRow(209, 209)]
    public void RouteHeadingMetricIndex_MatchesViceCityFindNewCharacter(
        int metricIndex,
        int expected)
    {
        Assert.AreEqual(expected, FontMetricsService.RouteHeadingMetricIndex(metricIndex));
    }

    [TestMethod]
    public void Resolve_StylesChooseTextureAndHeadingRoutesMetricAndGlyphSeparately()
    {
        var profile = FontMetricsPresets.BelarusianViceCity;

        var bank = FontMetricsService.Resolve(
            profile,
            ViceCityFontStyle.Bank,
            (byte)'A');
        var standard = FontMetricsService.Resolve(
            profile,
            ViceCityFontStyle.Standard,
            (byte)'A');
        var headingUpper = FontMetricsService.Resolve(
            profile,
            ViceCityFontStyle.Heading,
            0x91,
            FontRenderContext.Gameplay);
        var headingLower = FontMetricsService.Resolve(
            profile,
            ViceCityFontStyle.Heading,
            0xA8,
            FontRenderContext.SaveLoad);
        var headingBlank = FontMetricsService.Resolve(
            profile,
            ViceCityFontStyle.Heading,
            (byte)'!');

        Assert.AreEqual(FontTextureKind.Font2, bank.EffectiveFont);
        Assert.AreEqual(FontTextureKind.Font1, standard.EffectiveFont);
        Assert.AreEqual(FontTextureKind.Font1, headingUpper.EffectiveFont);
        Assert.AreEqual(198, headingUpper.MetricIndex);
        Assert.AreEqual(198, headingLower.MetricIndex);
        Assert.AreEqual((byte)0xE6, headingUpper.MetricCode);
        Assert.AreEqual((byte)0xE6, headingUpper.GlyphCode);
        Assert.AreEqual((ushort)18, headingUpper.EffectiveAdvance);
        Assert.AreEqual((ushort)18, headingLower.EffectiveAdvance);
        Assert.IsNull(headingUpper.ContextOverride);
        Assert.IsNull(headingLower.ContextOverride);
        Assert.AreEqual(208, headingBlank.MetricIndex);
        Assert.AreEqual((byte)0xF0, headingBlank.MetricCode);
        Assert.AreEqual((byte)0x20, headingBlank.GlyphCode);
    }

    [TestMethod]
    public void GetAdvance_UsesIndependentFontTables()
    {
        var profile = CreateProfile();
        SetAdvance(profile, FontTextureKind.Font1, (byte)'A', 21);
        SetAdvance(profile, FontTextureKind.Font2, (byte)'A', 9);

        Assert.AreEqual(
            (ushort)21,
            FontMetricsService.GetAdvance(profile, FontTextureKind.Font1, (byte)'A'));
        Assert.AreEqual(
            (ushort)9,
            FontMetricsService.GetAdvance(profile, FontTextureKind.Font2, (byte)'A'));
    }

    [TestMethod]
    public void BelarusianTAndLowercaseT_UseVerifiedRuntimeContexts()
    {
        var profile = FontMetricsPresets.BelarusianViceCity;

        Assert.AreEqual(
            (ushort)14,
            FontMetricsService.GetAdvance(profile, FontTextureKind.Font1, 0x91));
        Assert.AreEqual(
            (ushort)13,
            FontMetricsService.GetAdvance(
                profile,
                FontTextureKind.Font1,
                0x91,
                FontRenderContext.Gameplay));
        Assert.AreEqual(
            (ushort)15,
            FontMetricsService.GetAdvance(
                profile,
                FontTextureKind.Font1,
                0xA8,
                FontRenderContext.SaveLoad));
        Assert.AreEqual(
            (ushort)16,
            FontMetricsService.GetAdvance(
                profile,
                FontTextureKind.Font1,
                0x91,
                FontRenderContext.ExitConfirmation));
        Assert.AreEqual(
            (ushort)18,
            FontMetricsService.GetAdvance(
                profile,
                FontTextureKind.Font1,
                0xA8,
                FontRenderContext.Heading));
        Assert.AreEqual(
            (ushort)22,
            FontMetricsService.GetAdvance(
                profile,
                FontTextureKind.Font2,
                0xA8,
            FontRenderContext.Gameplay));
    }

    [TestMethod]
    [DataRow(FontRenderContext.Default, 14, 14)]
    [DataRow(FontRenderContext.Gameplay, 13, 14)]
    [DataRow(FontRenderContext.Subtitles, 13, 14)]
    [DataRow(FontRenderContext.MainMenu, 14, 14)]
    [DataRow(FontRenderContext.SaveLoad, 15, 15)]
    [DataRow(FontRenderContext.ExitConfirmation, 16, 16)]
    public void StandardStyle_UsesAllVerifiedRuntimeMetricContexts(
        FontRenderContext context,
        int expectedUpper,
        int expectedLower)
    {
        var profile = FontMetricsPresets.BelarusianViceCity;

        var upper = FontMetricsService.Resolve(
            profile,
            ViceCityFontStyle.Standard,
            0x91,
            context);
        var lower = FontMetricsService.Resolve(
            profile,
            ViceCityFontStyle.Standard,
            0xA8,
            context);

        Assert.AreEqual((ushort)expectedUpper, upper.EffectiveAdvance);
        Assert.AreEqual((ushort)expectedLower, lower.EffectiveAdvance);
    }

    [TestMethod]
    public void GetAdvance_RejectsMissingShortAndDuplicateMetrics()
    {
        var missing = CreateProfile();
        missing = new FontMetricsProfile
        {
            Font1 = null!,
            Font2 = missing.Font2,
        };
        var shortTable = CreateProfile();
        shortTable = new FontMetricsProfile
        {
            Font1 = new FontMetricsTable { Advances = new ushort[1] },
            Font2 = shortTable.Font2,
        };
        var duplicate = CreateProfile();
        duplicate.Overrides.AddRange(
        [
            Override(FontRenderContext.Gameplay, FontTextureKind.Font1, 0x54, 12),
            Override(FontRenderContext.Gameplay, FontTextureKind.Font1, 0x54, 13),
        ]);

        Assert.Throws<InvalidDataException>(() =>
            FontMetricsService.GetAdvance(missing, FontTextureKind.Font1, 0x54));
        Assert.Throws<InvalidDataException>(() =>
            FontMetricsService.GetAdvance(shortTable, FontTextureKind.Font1, 0x54));
        Assert.Throws<InvalidDataException>(() =>
            FontMetricsService.GetAdvance(
                duplicate,
                FontTextureKind.Font1,
                0x54,
                FontRenderContext.Gameplay));
    }

    private static FontMetricOverride Override(
        FontRenderContext context,
        FontTextureKind font,
        byte code,
        ushort advance) => new()
    {
        Context = context,
        Font = font,
        Code = code,
        Advance = advance,
    };

    private static FontMetricsProfile CreateProfile() => new()
    {
        Font1 = new FontMetricsTable { Advances = new ushort[FontMetricsTable.MetricCount] },
        Font2 = new FontMetricsTable { Advances = new ushort[FontMetricsTable.MetricCount] },
    };

    private static void SetAdvance(
        FontMetricsProfile profile,
        FontTextureKind font,
        byte code,
        ushort advance)
    {
        var table = font == FontTextureKind.Font1 ? profile.Font1 : profile.Font2;
        table.Advances[FontMetricsService.GetMetricIndex(code)] = advance;
    }
}
