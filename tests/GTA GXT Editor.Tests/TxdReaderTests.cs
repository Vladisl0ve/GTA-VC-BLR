using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class TxdReaderTests
{
    private readonly TxdReader _reader = new();

    [TestMethod]
    public void Read_Pal8Texture_DecodesPalettePixels()
    {
        var palette = new byte[256 * 4];
        palette[4] = 10;
        palette[5] = 20;
        palette[6] = 30;
        palette[7] = 40;
        var data = TestTxdFactory.Create(TestTxdFactory.Pal8(
            "font1",
            2,
            2,
            palette,
            [1, 0, 1, 0]));

        var document = _reader.Read(data, "fonts.txd");

        Assert.HasCount(1, document.Textures);
        CollectionAssert.AreEqual(new byte[] { 10, 20, 30, 40 }, document.Textures[0].PixelsBgra32[..4]);
        Assert.IsTrue(document.Textures[0].IsFontAtlas);
    }

    [TestMethod]
    public void Read_Pal4Texture_DecodesLowAndHighNibbles()
    {
        var palette = new byte[16 * 4];
        palette[4] = 10;
        palette[5] = 20;
        palette[6] = 30;
        palette[7] = 40;
        palette[8] = 50;
        palette[9] = 60;
        palette[10] = 70;
        palette[11] = 80;
        var data = TestTxdFactory.Create(
            TestTxdFactory.Pal4("font1", 2, 1, palette, [0x21]));

        var texture = _reader.Read(data, "fonts.txd").Textures.Single();

        CollectionAssert.AreEqual(
            new byte[] { 10, 20, 30, 40, 50, 60, 70, 80 },
            texture.PixelsBgra32);
    }

    [TestMethod]
    public void Read_CommonUncompressedFormats_DecodesToBgra32()
    {
        var data = TestTxdFactory.Create(
            TestTxdFactory.Uncompressed("rgb24", 9, 0x0600, 1, 1, 24, [1, 2, 3]),
            TestTxdFactory.Uncompressed("a1r5g5b5", 8, 0x0100, 1, 1, 16, [0x00, 0xFC]),
            TestTxdFactory.Uncompressed("r5g6b5", 8, 0x0200, 1, 1, 16, [0xE0, 0x07]),
            TestTxdFactory.Uncompressed("r4g4b4a4", 8, 0x0300, 1, 1, 16, [0x00, 0xFF]),
            TestTxdFactory.Uncompressed("r5g5b5", 8, 0x0A00, 1, 1, 16, [0x1F, 0x00]));

        var textures = _reader.Read(data, "formats.txd").Textures;

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 255 }, textures[0].PixelsBgra32);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 255 }, textures[1].PixelsBgra32);
        CollectionAssert.AreEqual(new byte[] { 0, 255, 0, 255 }, textures[2].PixelsBgra32);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 255 }, textures[3].PixelsBgra32);
        CollectionAssert.AreEqual(new byte[] { 255, 0, 0, 255 }, textures[4].PixelsBgra32);
        Assert.AreEqual(TxdPlatform.D3D9, textures[0].Platform);
    }

    [TestMethod]
    public void Read_TextureMask_AppliesLuminanceToPreviewAlpha()
    {
        var basePixels = Enumerable.Repeat((byte)255, 2 * 2 * 4).ToArray();
        var data = TestTxdFactory.Create(
            TestTxdFactory.Bgra32("font1", 2, 2, basePixels, "font1_mask", hasAlpha: false),
            TestTxdFactory.Lum8("font1_mask", 2, 2, [0, 64, 128, 255]));

        var document = _reader.Read(data, "fonts.txd");

        var font = document.Textures.Single(texture => texture.Name == "font1");
        CollectionAssert.AreEqual(
            new byte[] { 0, 64, 128, 255 },
            font.PreviewPixelsBgra32.Where((_, index) => index % 4 == 3).ToArray());
    }

    [TestMethod]
    public void Read_TextureMask_MultipliesEmbeddedAndMaskAlpha()
    {
        var data = TestTxdFactory.Create(
            TestTxdFactory.Bgra32("font1", 1, 1, [255, 255, 255, 128], "font1_mask"),
            TestTxdFactory.Bgra32("font1_mask", 1, 1, [0, 0, 0, 128]));

        var font = _reader.Read(data, "fonts.txd").Textures[0];

        Assert.AreEqual(64, font.PreviewPixelsBgra32[3]);
    }

    [TestMethod]
    public void Read_Dxt1AndDxt3_DecodesOpaqueRedBlocks()
    {
        byte[] dxt1 = [0x00, 0xF8, 0x00, 0x00, 0, 0, 0, 0];
        byte[] dxt3 = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, .. dxt1];
        var data = TestTxdFactory.Create(
            TestTxdFactory.Dxt("font1", 1, dxt1),
            TestTxdFactory.Dxt("font2", 3, dxt3));

        var document = _reader.Read(data, "fonts.txd");

        Assert.HasCount(2, document.Textures);
        foreach (var texture in document.Textures)
        {
            CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 255 }, texture.PixelsBgra32[..4]);
        }
    }

    [TestMethod]
    public void Read_D3D9Dxt1_UsesFourCc()
    {
        byte[] block = [0x00, 0xF8, 0x00, 0x00, 0, 0, 0, 0];
        var texture = _reader.Read(
            TestTxdFactory.Create(TestTxdFactory.DxtD3D9("font1", 0x31545844, block)),
            "d3d9.txd").Textures.Single();

        Assert.AreEqual(TxdPlatform.D3D9, texture.Platform);
        Assert.AreEqual(TxdCompression.Dxt1, texture.Compression);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 255, 255 }, texture.PixelsBgra32[..4]);
    }

    [TestMethod]
    public void Read_MultipleMipmaps_UsesOnlyFirstLevel()
    {
        var firstLevel = new byte[]
        {
            1, 2, 3, 255,
            4, 5, 6, 255,
            7, 8, 9, 255,
            10, 11, 12, 255,
        };
        var spec = TestTxdFactory.Bgra32("font1", 2, 2, firstLevel) with
        {
            AdditionalMipmaps = [[100, 101, 102, 255]],
        };

        var texture = _reader.Read(TestTxdFactory.Create(spec), "mipmaps.txd").Textures.Single();

        Assert.AreEqual(2, texture.MipmapCount);
        CollectionAssert.AreEqual(firstLevel, texture.PixelsBgra32);
    }

    [TestMethod]
    public void Read_UnsupportedPlatform_RejectsEntireDocument()
    {
        var texture = TestTxdFactory.Bgra32("font1", 1, 1, [0, 0, 0, 255]) with { Platform = 4 };

        var exception = Assert.Throws<InvalidDataException>(() =>
            _reader.Read(TestTxdFactory.Create(texture), "ps2.txd"));

        StringAssert.Contains(exception.Message, "platform");
    }

    [TestMethod]
    public void Read_TruncatedChunk_ThrowsInvalidData()
    {
        var data = TestTxdFactory.Create(TestTxdFactory.Bgra32("font1", 1, 1, [0, 0, 0, 255]));

        Assert.Throws<InvalidDataException>(() => _reader.Read(data[..^2], "broken.txd"));
    }

    [TestMethod]
    public void GlyphAtlas_UsesDictionaryAndFont2VerticalStep()
    {
        var font1 = CreateTexture("font1", 512, 512);
        var font2 = CreateTexture("font2", 512, 512);
        IReadOnlyDictionary<byte, char> mapping = new Dictionary<byte, char> { [0x80] = 'А' };

        var regular = GlyphAtlasService.CreateCells(font1, mapping);
        var bank = GlyphAtlasService.CreateCells(font2, mapping);
        var viceCityRegular = GlyphAtlasService.CreateCells(font1, mapping, GXTType.GtaViceCity);

        Assert.AreEqual(256, regular.Count);
        Assert.AreEqual(208, bank.Count);
        Assert.AreEqual(208, viceCityRegular.Count);
        Assert.AreEqual('А', regular.Single(cell => cell.Code == 0x80).Character);
        Assert.AreEqual(32, regular[0].Height);
        Assert.AreEqual(40, bank[0].Height);
        Assert.AreEqual(40, viceCityRegular[0].Height);
    }

    [TestMethod]
    public void GlyphAtlas_PagerLabelsAsciiAndKeepsBoundaryCellsSafe()
    {
        var pager = CreateTexture("pager", 512, 512);

        var cells = GlyphAtlasService.CreateCells(pager, new Dictionary<byte, char>());

        Assert.AreEqual("0x41  A", cells.Single(cell => cell.Code == 0x41).Label);
        Assert.AreEqual("0x80", cells.Single(cell => cell.Code == 0x80).Label);
        Assert.AreEqual((byte?)0xFF, cells[223].Code);
        Assert.IsNull(cells[224].Code);
        Assert.AreEqual(480, cells[^1].X);
        Assert.AreEqual(480, cells[^1].Y);
        Assert.AreEqual(32, cells[^1].Width);
        Assert.AreEqual(32, cells[^1].Height);
        Assert.AreEqual(32 * 32 * 4, GlyphAtlasService.Crop(pager, cells[^1]).Length);
    }

    private static TxdTexture CreateTexture(string name, int width, int height) => new()
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
