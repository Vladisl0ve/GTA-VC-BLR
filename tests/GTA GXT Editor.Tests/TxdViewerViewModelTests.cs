using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
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

    [TestMethod]
    public void ImportAndExportProfileCommands_UseViewModelServices()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"txd-viewer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var importPath = Path.Combine(directory, "import.gxtmap.json");
            var exportPath = Path.Combine(directory, "export.gxtmap.json");
            CharacterMapFileSerializer.Save(importPath, new CharacterMapProfile
            {
                Mappings =
                [
                    new CharacterMapEntry
                    {
                        Character = 'Ж',
                        Codes = [0x80],
                        PreferredCode = 0x80,
                    },
                ],
            });
            var dialogs = new FakeDialogService
            {
                OpenPath = importPath,
                SavePath = exportPath,
            };
            var viewModel = new TxdViewerViewModel(CreateRequest(GXTType.GtaViceCity), dialogs);

            viewModel.ImportProfileCommand.Execute(null);
            viewModel.ExportProfileCommand.Execute(null);

            Assert.AreEqual('Ж', viewModel.Profile.ToDecodeMap()[0x80]);
            Assert.AreEqual('Ж', CharacterMapFileSerializer.Load(exportPath).ToDecodeMap()[0x80]);
            Assert.IsEmpty(dialogs.Errors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ApplyCommand_PublishesVerifiedResult()
    {
        var viewModel = new TxdViewerViewModel(CreateRequest(GXTType.GtaViceCity));
        var applied = false;
        viewModel.ApplySucceeded += (_, _) => applied = true;

        viewModel.ApplyCommand.Execute(null);

        Assert.IsTrue(applied);
        Assert.IsNotNull(viewModel.Result);
        Assert.IsTrue(viewModel.Result.Profile.IsVerified);
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

    private sealed class FakeDialogService : IDialogService
    {
        public string? OpenPath { get; init; }

        public string? SavePath { get; init; }

        public List<string> Errors { get; } = [];

        public string? OpenFile(string title, string filter) => OpenPath;

        public IReadOnlyList<string> OpenFiles(string title, string filter) => [];

        public string? SaveFile(string title, string filter, string suggestedPath) => SavePath;

        public bool Confirm(string message, string title) => true;

        public void ShowInfo(string message, string? title = null)
        {
        }

        public void ShowError(string message, string? title = null) => Errors.Add(message);

        public EntryEditorResult? EditEntry(EntryEditorRequest request) => null;
    }
}
