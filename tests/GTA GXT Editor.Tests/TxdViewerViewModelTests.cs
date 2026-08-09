using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.ViewModels;

namespace GTA_GXT_Editor.Tests;

[STATestClass]
public sealed class TxdViewerViewModelTests
{
    [TestMethod]
    public void ViceCity_HidesPagerFromFontAtlases()
    {
        var viewModel = new TxdViewerViewModel(CreateRequest(GXTType.GtaViceCity));

        CollectionAssert.AreEqual(
            new[] { "font1", "font2" },
            viewModel.Textures.Select(texture => texture.Name).ToArray());
        Assert.AreEqual("Ячейки font1 / font2", viewModel.GlyphAtlasHeading);
    }

    [TestMethod]
    public void GtaIII_KeepsPagerInFontAtlases()
    {
        var viewModel = new TxdViewerViewModel(CreateRequest(GXTType.GtaIII));

        CollectionAssert.AreEqual(
            new[] { "font1", "font2", "pager" },
            viewModel.Textures.Select(texture => texture.Name).ToArray());
        Assert.AreEqual("Ячейки font1 / font2 / pager", viewModel.GlyphAtlasHeading);
    }

    private static CharacterMapEditorRequest CreateRequest(GXTType gameType) => new(
        new TxdAttachment
        {
            Id = Guid.NewGuid(),
            OriginalFileName = "fonts.txd",
            DisplayName = "fonts",
            Data = [],
            Document = new TxdDocument
            {
                RenderWareVersion = 0,
                Textures =
                [
                    CreateTexture("font1"),
                    CreateTexture("font2"),
                    CreateTexture("pager"),
                ],
            },
        },
        gameType,
        new CharacterMapProfile(),
        [],
        [],
        GxtLanguage.English);

    private static TxdTexture CreateTexture(string name) => new()
    {
        Name = name,
        MaskName = string.Empty,
        Platform = TxdPlatform.D3D8,
        Width = 32,
        Height = 32,
        Depth = 32,
        MipmapCount = 1,
        RasterFormat = 0x0500,
        Compression = TxdCompression.None,
        HasAlpha = true,
        PixelsBgra32 = new byte[32 * 32 * 4],
    };
}
