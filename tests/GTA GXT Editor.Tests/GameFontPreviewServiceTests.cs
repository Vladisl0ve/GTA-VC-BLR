using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class GameFontPreviewServiceTests
{
    private readonly GameFontPreviewService _service = new();

    [TestMethod]
    public void Layout_UsesOnlyScaledMetricsForHorizontalPositions()
    {
        var metrics = CreateMetrics(defaultAdvance: 1);
        SetAdvance(metrics, FontTextureKind.Font1, (byte)'A', 10);
        SetAdvance(metrics, FontTextureKind.Font1, (byte)'B', 20);
        SetAdvance(metrics, FontTextureKind.Font1, (byte)'C', 7);

        var layout = _service.Layout(
            "ABC",
            CreateTexture("font1"),
            new CharacterMapProfile(),
            metrics,
            ViceCityFontStyle.Standard);

        CollectionAssert.AreEqual(new[] { 0, 10, 30 }, layout.Glyphs.Select(glyph => glyph.X).ToArray());
        CollectionAssert.AreEqual(new[] { 10, 20, 7 }, layout.Glyphs.Select(glyph => glyph.Advance).ToArray());
        Assert.AreEqual(37, layout.AdvanceWidth);
        Assert.AreEqual(37, layout.PixelWidth);
        Assert.IsEmpty(layout.Issues);
    }

    [TestMethod]
    public void Layout_ScalesGlyphAndAdvanceTogetherAtOneTwoAndFour()
    {
        var texture = CreateTexture("font1");
        var metrics = CreateMetrics(defaultAdvance: 3);

        foreach (var scale in new[] { 1, 2, 4 })
        {
            var layout = _service.Layout(
                "AA",
                texture,
                new CharacterMapProfile(),
                metrics,
                ViceCityFontStyle.Standard,
                scale: scale);

            Assert.AreEqual(3 * scale, layout.Glyphs[0].Advance);
            Assert.AreEqual(3 * scale, layout.Glyphs[1].X);
            Assert.AreEqual(2 * scale, layout.Glyphs[0].PixelWidth);
            Assert.AreEqual(2 * scale, layout.Glyphs[0].PixelHeight);
            Assert.AreEqual(6 * scale, layout.AdvanceWidth);
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => _service.Layout(
            "A",
            texture,
            new CharacterMapProfile(),
            metrics,
            ViceCityFontStyle.Standard,
            scale: 3));
    }

    [TestMethod]
    public void Layout_SelectsMatchingTextureAndIndependentFontMetrics()
    {
        var metrics = CreateMetrics(defaultAdvance: 1);
        SetAdvance(metrics, FontTextureKind.Font1, (byte)'A', 21);
        SetAdvance(metrics, FontTextureKind.Font2, (byte)'A', 9);

        var font1 = _service.Layout(
            "AA",
            CreateTexture("font1"),
            new CharacterMapProfile(),
            metrics,
            ViceCityFontStyle.Standard);
        var font2 = _service.Layout(
            "AA",
            CreateTexture("font2"),
            new CharacterMapProfile(),
            metrics,
            ViceCityFontStyle.Bank);
        var mismatch = _service.Layout(
            "AA",
            CreateTexture("font2"),
            new CharacterMapProfile(),
            metrics,
            ViceCityFontStyle.Standard);

        Assert.AreEqual(42, font1.AdvanceWidth);
        Assert.AreEqual(18, font2.AdvanceWidth);
        Assert.IsTrue(mismatch.Issues.Any(issue =>
            issue.Kind == GameFontPreviewIssueKind.UnsupportedFont));
        Assert.IsEmpty(mismatch.Glyphs);
    }

    [TestMethod]
    public void Layout_ParsesControlTokensAndBothKindsOfNewline()
    {
        var layout = _service.Layout(
            "A~r~B~n~C\r\nD~unknown~E",
            CreateTexture("font1"),
            new CharacterMapProfile(),
            CreateMetrics(defaultAdvance: 1),
            ViceCityFontStyle.Standard);

        CollectionAssert.AreEqual(
            new[] { 'A', 'B', 'C', 'D', 'E' },
            layout.Glyphs.Select(glyph => glyph.Character).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 0, 2, 4, 4 },
            layout.Glyphs.Select(glyph => glyph.Y).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1, 0, 0, 1 },
            layout.Glyphs.Select(glyph => glyph.X).ToArray());
        Assert.AreEqual(3, layout.LineCount);
        Assert.AreEqual(6, layout.PixelHeight);
        Assert.IsEmpty(layout.Issues);
    }

    [TestMethod]
    public void Layout_UnknownCharacterIsSkippedWithDeterministicIssue()
    {
        var layout = _service.Layout(
            "A漢B",
            CreateTexture("font1"),
            new CharacterMapProfile(),
            CreateMetrics(defaultAdvance: 3),
            ViceCityFontStyle.Standard);

        CollectionAssert.AreEqual(new[] { 'A', 'B' }, layout.Glyphs.Select(glyph => glyph.Character).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 3 }, layout.Glyphs.Select(glyph => glyph.X).ToArray());
        Assert.HasCount(1, layout.Issues);
        Assert.AreEqual(GameFontPreviewIssueKind.CharacterCannotBeEncoded, layout.Issues[0].Kind);
        Assert.AreEqual(1, layout.Issues[0].TextIndex);
        StringAssert.Contains(layout.Issues[0].Message, "漢");
    }

    [TestMethod]
    public void Layout_BelarusianSampleUsesPreferredCodesAndCanonicalMetrics()
    {
        const string sample = "Вайс-Сіці Ўў";
        var characterMap = CharacterMapPresets.Belarusian;
        var metrics = FontMetricsPresets.BelarusianViceCity;
        var layout = _service.Layout(
            sample,
            CreateTexture("font1"),
            characterMap,
            metrics,
            ViceCityFontStyle.Standard,
            FontRenderContext.Default);
        var expectedCodes = sample.Select(character =>
            characterMap.ToEncodeMap().GetValueOrDefault(character, (byte)character)).ToArray();
        var expectedWidth = expectedCodes.Sum(code =>
            FontMetricsService.GetAdvance(metrics, FontTextureKind.Font1, code));

        Assert.IsEmpty(layout.Issues);
        Assert.HasCount(sample.Length, layout.Glyphs);
        CollectionAssert.AreEqual(expectedCodes, layout.Glyphs.Select(glyph => glyph.Code).ToArray());
        Assert.AreEqual(expectedWidth, layout.AdvanceWidth);
        Assert.AreEqual((byte)0x86, layout.Glyphs.Single(glyph => glyph.Character == 'Ў').Code);
        Assert.AreEqual((byte)0x9D, layout.Glyphs.Single(glyph => glyph.Character == 'ў').Code);
        Assert.AreEqual(0x86 - 0x20, FontMetricsService.GetMetricIndex(0x86));
        Assert.AreEqual(0x9D - 0x20, FontMetricsService.GetMetricIndex(0x9D));
    }

    [TestMethod]
    public void Layout_UsesContextOverridesForBelarusianTAndLowercaseT()
    {
        var defaultLayout = _service.Layout(
            "Тт",
            CreateTexture("font1"),
            CharacterMapPresets.Belarusian,
            FontMetricsPresets.BelarusianViceCity,
            ViceCityFontStyle.Standard);
        var saveLayout = _service.Layout(
            "Тт",
            CreateTexture("font1"),
            CharacterMapPresets.Belarusian,
            FontMetricsPresets.BelarusianViceCity,
            ViceCityFontStyle.Standard,
            FontRenderContext.SaveLoad);

        CollectionAssert.AreEqual(new[] { 14, 14 }, defaultLayout.Glyphs.Select(glyph => glyph.Advance).ToArray());
        CollectionAssert.AreEqual(new[] { 15, 15 }, saveLayout.Glyphs.Select(glyph => glyph.Advance).ToArray());
        Assert.AreEqual(28, defaultLayout.AdvanceWidth);
        Assert.AreEqual(30, saveLayout.AdvanceWidth);
    }

    [TestMethod]
    public void HeadingStyle_UsesRoutedFont1GlyphsAndMetrics()
    {
        var texture = CreateTexture("font1");
        var metrics = CreateMetrics(defaultAdvance: 1);
        SetAdvance(metrics, FontTextureKind.Font1, (byte)'A', 3);
        SetAdvance(metrics, FontTextureKind.Font1, 0xBB, 5);
        SetCellPixels(texture, (byte)'A',
        [
            1, 2, 3, 255,
            1, 2, 3, 255,
            1, 2, 3, 255,
            1, 2, 3, 255,
        ]);
        SetCellPixels(texture, 0xBB,
        [
            4, 5, 6, 255,
            4, 5, 6, 255,
            4, 5, 6, 255,
            4, 5, 6, 255,
        ]);

        var standard = _service.Layout(
            "A",
            texture,
            new CharacterMapProfile(),
            metrics,
            ViceCityFontStyle.Standard);
        var heading = _service.Layout(
            "A",
            texture,
            new CharacterMapProfile(),
            metrics,
            ViceCityFontStyle.Heading);
        var standardBitmap = GameFontPreviewService.Render(texture, standard);
        var headingBitmap = GameFontPreviewService.Render(texture, heading);

        Assert.AreEqual((byte)'A', standard.Glyphs.Single().GlyphCode);
        Assert.AreEqual((byte)0xBB, heading.Glyphs.Single().GlyphCode);
        Assert.AreEqual(3, standard.AdvanceWidth);
        Assert.AreEqual(5, heading.AdvanceWidth);
        Assert.AreEqual(ViceCityFontStyle.Heading, heading.Style);
        Assert.AreEqual(FontTextureKind.Font1, heading.Font);
        AssertPixel(standardBitmap, 0, 0, [1, 2, 3, 255]);
        AssertPixel(headingBitmap, 0, 0, [4, 5, 6, 255]);
    }

    [TestMethod]
    public void HeadingStyle_AsiGlyphOverridesFollowBelarusianAtlasCells()
    {
        var texture = CreateTexture("font1");
        var metrics = CreateMetrics(defaultAdvance: 1);
        SetAdvance(metrics, FontTextureKind.Font1, 0xEB, 15);
        SetAdvance(metrics, FontTextureKind.Font1, 0xEC, 16);
        SetAdvance(metrics, FontTextureKind.Font1, 0xED, 17);
        var mapping = new CharacterMapProfile
        {
            Mappings =
            [
                new CharacterMapEntry { Character = 'Ё', Codes = [0x96], PreferredCode = 0x96 },
                new CharacterMapEntry { Character = 'Я', Codes = [0xAD], PreferredCode = 0xAD },
                new CharacterMapEntry { Character = 'я', Codes = [0xAE], PreferredCode = 0xAE },
                new CharacterMapEntry { Character = 'ё', Codes = [0xAF], PreferredCode = 0xAF },
            ],
        };
        IReadOnlyDictionary<char, byte> headingGlyphCodes = new Dictionary<char, byte>
        {
            ['Ё'] = 0xEC,
            ['Я'] = 0xEB,
            ['я'] = 0xEB,
            ['ё'] = 0xEC,
        };

        var routed = _service.Layout(
            "ЁЯяё",
            texture,
            mapping,
            metrics,
            ViceCityFontStyle.Heading);
        var overridden = _service.Layout(
            "ЁЯяё",
            texture,
            mapping,
            metrics,
            ViceCityFontStyle.Heading,
            headingGlyphCodes: headingGlyphCodes);
        var standard = _service.Layout(
            "ЁЯяё",
            texture,
            mapping,
            metrics,
            ViceCityFontStyle.Standard,
            headingGlyphCodes: headingGlyphCodes);

        CollectionAssert.AreEqual(
            new byte[] { 0xEB, 0xEC, 0xEC, 0xED },
            routed.Glyphs.Select(glyph => glyph.GlyphCode).ToArray());
        CollectionAssert.AreEqual(
            new byte[] { 0xEC, 0xEB, 0xEB, 0xEC },
            overridden.Glyphs.Select(glyph => glyph.GlyphCode).ToArray());
        CollectionAssert.AreEqual(
            new[] { 16, 15, 15, 16 },
            overridden.Glyphs.Select(glyph => glyph.Advance).ToArray());
        CollectionAssert.AreEqual(
            new byte[] { 0x96, 0xAD, 0xAE, 0xAF },
            standard.Glyphs.Select(glyph => glyph.GlyphCode).ToArray());
    }

    [TestMethod]
    public void Layout_InvalidMetricsAndUnavailableGlyphReturnIssuesWithoutCrashing()
    {
        var invalidMetrics = new FontMetricsProfile
        {
            Font1 = new FontMetricsTable { Advances = new ushort[1] },
            Font2 = new FontMetricsTable { Advances = new ushort[FontMetricsTable.MetricCount] },
        };
        var invalidLayout = _service.Layout(
            "A",
            CreateTexture("font1"),
            new CharacterMapProfile(),
            invalidMetrics,
            ViceCityFontStyle.Standard);
        var missingGlyphMap = Map('¤', 0xF0);
        var missingGlyphLayout = _service.Layout(
            "¤",
            CreateTexture("font1"),
            missingGlyphMap,
            CreateMetrics(defaultAdvance: 1),
            ViceCityFontStyle.Standard);

        Assert.IsTrue(invalidLayout.Issues.Any(issue => issue.Kind == GameFontPreviewIssueKind.InvalidMetrics));
        Assert.IsEmpty(invalidLayout.Glyphs);
        Assert.HasCount(1, missingGlyphLayout.Issues);
        Assert.AreEqual(GameFontPreviewIssueKind.GlyphUnavailable, missingGlyphLayout.Issues[0].Kind);
        Assert.IsEmpty(missingGlyphLayout.Glyphs);
    }

    [TestMethod]
    public void Render_ReplicatesFullAtlasCellWithNearestNeighbor()
    {
        var texture = CreateTexture("font1");
        SetCellPixels(texture, (byte)'A',
        [
            1, 2, 3, 255,
            4, 5, 6, 255,
            7, 8, 9, 255,
            10, 11, 12, 255,
        ]);
        var layout = _service.Layout(
            "A",
            texture,
            new CharacterMapProfile(),
            CreateMetrics(defaultAdvance: 1),
            ViceCityFontStyle.Standard,
            scale: 2);

        var bitmap = GameFontPreviewService.Render(texture, layout);

        Assert.AreEqual(4, bitmap.Width);
        Assert.AreEqual(4, bitmap.Height);
        AssertPixel(bitmap, 0, 0, [1, 2, 3, 255]);
        AssertPixel(bitmap, 1, 1, [1, 2, 3, 255]);
        AssertPixel(bitmap, 2, 0, [4, 5, 6, 255]);
        AssertPixel(bitmap, 0, 2, [7, 8, 9, 255]);
        AssertPixel(bitmap, 3, 3, [10, 11, 12, 255]);
    }

    [TestMethod]
    public void Render_BlendsOverlappingSemitransparentGlyphsWithoutOverflow()
    {
        var texture = CreateTexture("font2");
        SetCellPixels(texture, (byte)'A',
        [
            255, 250, 255, 85,
            255, 250, 255, 68,
            255, 250, 255, 85,
            255, 250, 255, 68,
        ]);
        var layout = _service.Layout(
            "AA",
            texture,
            new CharacterMapProfile(),
            CreateMetrics(defaultAdvance: 1),
            ViceCityFontStyle.Bank);

        var bitmap = GameFontPreviewService.Render(texture, layout);

        Assert.AreEqual(3, bitmap.Width);
        AssertPixel(bitmap, 1, 0, [255, 250, 255, 130]);
        AssertPixel(bitmap, 1, 1, [255, 250, 255, 130]);
    }

    [TestMethod]
    public void Render_MetricsGuidesIncludeAdvanceBoundary()
    {
        var texture = CreateTexture("font1");
        var layout = _service.Layout(
            "A",
            texture,
            new CharacterMapProfile(),
            CreateMetrics(defaultAdvance: 3),
            ViceCityFontStyle.Standard);

        var bitmap = GameFontPreviewService.Render(texture, layout, showMetricsGuides: true);

        Assert.AreEqual(4, bitmap.Width);
        AssertPixel(bitmap, 0, 0, [0xFF, 0x80, 0x00, 0xFF]);
        AssertPixel(bitmap, 3, 1, [0xFF, 0x80, 0x00, 0xFF]);
    }

    private static CharacterMapProfile Map(char character, byte code) => new()
    {
        Mappings =
        [
            new CharacterMapEntry
            {
                Character = character,
                Codes = [code],
                PreferredCode = code,
            },
        ],
    };

    private static FontMetricsProfile CreateMetrics(ushort defaultAdvance)
    {
        var profile = new FontMetricsProfile
        {
            Font1 = new FontMetricsTable { Advances = new ushort[FontMetricsTable.MetricCount] },
            Font2 = new FontMetricsTable { Advances = new ushort[FontMetricsTable.MetricCount] },
        };
        Array.Fill(profile.Font1.Advances, defaultAdvance);
        Array.Fill(profile.Font2.Advances, defaultAdvance);
        return profile;
    }

    private static void SetAdvance(
        FontMetricsProfile profile,
        FontTextureKind font,
        byte code,
        ushort advance)
    {
        var table = font == FontTextureKind.Font1 ? profile.Font1 : profile.Font2;
        table.Advances[FontMetricsService.GetMetricIndex(code)] = advance;
    }

    private static TxdTexture CreateTexture(string name)
    {
        const int width = 32;
        const int height = 26;
        return new TxdTexture
        {
            Name = name,
            MaskName = string.Empty,
            Platform = TxdPlatform.D3D8,
            Width = width,
            Height = height,
            Depth = 32,
            MipmapCount = 1,
            RasterFormat = 0x0500,
            Compression = TxdCompression.None,
            HasAlpha = true,
            PixelsBgra32 = new byte[width * height * 4],
        };
    }

    private static void SetCellPixels(TxdTexture texture, byte code, byte[] pixels)
    {
        var cell = GlyphAtlasService.CreateCells(
                texture,
                new Dictionary<byte, char>(),
                Common.GXTType.GtaViceCity)
            .Single(candidate => candidate.Code == code);
        Assert.AreEqual(cell.Width * cell.Height * 4, pixels.Length);
        for (var row = 0; row < cell.Height; row++)
        {
            Buffer.BlockCopy(
                pixels,
                row * cell.Width * 4,
                texture.PixelsBgra32,
                ((cell.Y + row) * texture.Width + cell.X) * 4,
                cell.Width * 4);
        }
    }

    private static void AssertPixel(
        GameFontPreviewBitmap bitmap,
        int x,
        int y,
        byte[] expected)
    {
        var offset = y * bitmap.Stride + x * 4;
        CollectionAssert.AreEqual(expected, bitmap.PixelsBgra32[offset..(offset + 4)]);
    }
}
