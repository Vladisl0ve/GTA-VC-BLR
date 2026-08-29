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
        Assert.AreEqual("font1 / font2 cells", viewModel.GlyphAtlasHeading);
    }

    [TestMethod]
    public void GtaIII_KeepsPagerInFontAtlases()
    {
        var viewModel = new TxdViewerViewModel(CreateRequest(GXTType.GtaIII));

        CollectionAssert.AreEqual(
            new[] { "font1", "font2", "pager" },
            viewModel.Textures.Select(texture => texture.Name).ToArray());
        Assert.AreEqual("font1 / font2 / pager cells", viewModel.GlyphAtlasHeading);
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

    [TestMethod]
    public void Metrics_AreDetachedAndResultReturnsAnotherDeepClone()
    {
        var metrics = CreateMetrics(defaultAdvance: 4);
        var viewModel = new TxdViewerViewModel(CreateRequest(
            GXTType.GtaViceCity,
            metrics: metrics));

        metrics.Font1.Advances[FontMetricsService.GetMetricIndex((byte)'A')] = 99;
        viewModel.PreviewText = "A";
        var result = viewModel.CreateResult();

        Assert.IsNotNull(viewModel.FontMetrics);
        Assert.AreNotSame(metrics, viewModel.FontMetrics);
        Assert.AreEqual(4, viewModel.PreviewLayout?.AdvanceWidth);
        Assert.IsNotNull(result.FontMetrics);
        Assert.AreNotSame(viewModel.FontMetrics, result.FontMetrics);
        Assert.AreNotSame(viewModel.FontMetrics.Font1.Advances, result.FontMetrics.Font1.Advances);
    }

    [TestMethod]
    public void Preview_StyleContextScaleAndGuidesRefreshIndependentlyFromSelectedAtlas()
    {
        var metrics = CreateMetrics(defaultAdvance: 1);
        SetAdvance(metrics, FontTextureKind.Font1, (byte)'A', 7);
        SetAdvance(metrics, FontTextureKind.Font2, (byte)'A', 3);
        metrics.Overrides.Add(new FontMetricOverride
        {
            Context = FontRenderContext.Gameplay,
            Font = FontTextureKind.Font2,
            Code = (byte)'A',
            Advance = 2,
        });
        var viewModel = new TxdViewerViewModel(CreateRequest(
            GXTType.GtaViceCity,
            metrics: metrics));
        viewModel.PreviewText = "AA";
        viewModel.SelectedGlyph = viewModel.Glyphs.Single(item => item.Cell.Code == (byte)'A');

        Assert.AreEqual(FontTextureKind.Font1, viewModel.SelectedFont);
        Assert.AreEqual(FontTextureKind.Font1, viewModel.PreviewFont);
        Assert.AreEqual(ViceCityFontStyle.Standard, viewModel.SelectedFontStyle.Style);
        Assert.AreEqual(14, viewModel.PreviewLayout?.AdvanceWidth);
        Assert.AreEqual((byte)'A', viewModel.SelectedGlyphCode);
        Assert.AreEqual((byte)'A' - 0x20, viewModel.SelectedMetricIndex);
        Assert.AreEqual((ushort)7, viewModel.SelectedFont1Advance);
        Assert.AreEqual((ushort)3, viewModel.SelectedFont2Advance);

        viewModel.SelectedTexture = viewModel.Textures.Single(texture => texture.Name == "font2");
        viewModel.SelectedGlyph = viewModel.Glyphs.Single(item => item.Cell.Code == (byte)'A');
        Assert.AreEqual(FontTextureKind.Font2, viewModel.SelectedFont);
        Assert.AreEqual(FontTextureKind.Font1, viewModel.PreviewFont);
        Assert.AreEqual(14, viewModel.PreviewLayout?.AdvanceWidth);
        Assert.AreEqual((ushort)3, viewModel.SelectedEffectiveAdvance);

        viewModel.SelectedFontStyle = viewModel.FontStyles.Single(option =>
            option.Style == ViceCityFontStyle.Bank);
        Assert.AreEqual(FontTextureKind.Font2, viewModel.PreviewFont);
        Assert.AreEqual(ViceCityFontStyle.Bank, viewModel.PreviewLayout?.Style);
        Assert.AreEqual(6, viewModel.PreviewLayout?.AdvanceWidth);

        var beforeContext = viewModel.PreviewImage;
        viewModel.SelectedRenderContext = viewModel.RenderContexts.Single(option =>
            option.Context == FontRenderContext.Gameplay);
        Assert.AreEqual(4, viewModel.PreviewLayout?.AdvanceWidth);
        Assert.AreEqual((ushort)3, viewModel.SelectedBaseAdvance);
        Assert.AreEqual((ushort)2, viewModel.SelectedContextOverrideAdvance);
        Assert.AreEqual((ushort)2, viewModel.SelectedEffectiveAdvance);
        Assert.AreNotSame(beforeContext, viewModel.PreviewImage);

        viewModel.SelectedPreviewScale = viewModel.PreviewScales.Single(option => option.Scale == 4);
        Assert.AreEqual(16, viewModel.PreviewLayout?.AdvanceWidth);
        var beforeGuides = viewModel.PreviewImage;
        viewModel.ShowMetricGuides = false;
        Assert.AreNotSame(beforeGuides, viewModel.PreviewImage);
    }

    [TestMethod]
    public void Preview_OffersAllViceCityStylesAndOnlyRuntimeMetricsContexts()
    {
        var viewModel = new TxdViewerViewModel(CreateRequest(
            GXTType.GtaViceCity,
            metrics: CreateMetrics(defaultAdvance: 2)));

        CollectionAssert.AreEqual(
            new[]
            {
                ViceCityFontStyle.Bank,
                ViceCityFontStyle.Standard,
                ViceCityFontStyle.Heading,
            },
            viewModel.FontStyles.Select(option => option.Style).ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                FontRenderContext.Default,
                FontRenderContext.Gameplay,
                FontRenderContext.Subtitles,
                FontRenderContext.MainMenu,
                FontRenderContext.SaveLoad,
                FontRenderContext.ExitConfirmation,
            },
            viewModel.RenderContexts.Select(option => option.Context).ToArray());
        Assert.AreEqual(ViceCityFontStyle.Standard, viewModel.SelectedFontStyle.Style);
    }

    [TestMethod]
    public void MappingPreferredCodeChangeRefreshesPreviewImmediately()
    {
        var profile = Map('Ж', 0x80);
        var metrics = CreateMetrics(defaultAdvance: 1);
        SetAdvance(metrics, FontTextureKind.Font1, 0x80, 5);
        SetAdvance(metrics, FontTextureKind.Font1, 0x81, 9);
        var viewModel = new TxdViewerViewModel(CreateRequest(
            GXTType.GtaViceCity,
            profile,
            metrics));
        viewModel.PreviewText = "Ж";
        Assert.AreEqual((byte)0x80, viewModel.PreviewLayout?.Glyphs.Single().Code);
        Assert.AreEqual(5, viewModel.PreviewLayout?.AdvanceWidth);

        viewModel.SelectedGlyph = viewModel.Glyphs.Single(item => item.Cell.Code == 0x81);
        viewModel.AssignmentText = "Ж";
        viewModel.AssignCommand.Execute(null);
        viewModel.MakePreferredCommand.Execute(null);

        Assert.AreEqual((byte)0x81, viewModel.PreviewLayout?.Glyphs.Single().Code);
        Assert.AreEqual(9, viewModel.PreviewLayout?.AdvanceWidth);
    }

    [TestMethod]
    public void MetricsCommands_ImportExportResetAndBelarusianPresetRefreshPreview()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"txd-metrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var initial = CreateMetrics(defaultAdvance: 2);
            var imported = CreateMetrics(defaultAdvance: 8);
            var importPath = Path.Combine(directory, "import.fontmetrics.json");
            var exportPath = Path.Combine(directory, "export.fontmetrics.json");
            FontMetricsFileSerializer.Save(importPath, imported);
            var dialogs = new FakeDialogService { OpenPath = importPath, SavePath = exportPath };
            var viewModel = new TxdViewerViewModel(CreateRequest(
                GXTType.GtaViceCity,
                metrics: initial), dialogs);
            viewModel.PreviewText = "A";

            viewModel.ImportFontMetricsCommand.Execute(null);
            Assert.AreEqual(8, viewModel.PreviewLayout?.AdvanceWidth);
            viewModel.ExportFontMetricsCommand.Execute(null);
            Assert.AreEqual(
                (ushort)8,
                FontMetricsService.GetAdvance(
                    FontMetricsFileSerializer.Load(exportPath),
                    FontTextureKind.Font1,
                    (byte)'A'));

            viewModel.ResetFontMetricsCommand.Execute(null);
            Assert.AreEqual(2, viewModel.PreviewLayout?.AdvanceWidth);
            viewModel.UseBelarusianPresetCommand.Execute(null);
            Assert.IsNotNull(viewModel.FontMetrics);
            Assert.AreEqual(
                FontMetricsService.GetAdvance(
                    FontMetricsPresets.BelarusianViceCity,
                    FontTextureKind.Font1,
                    (byte)'A'),
                viewModel.PreviewLayout?.AdvanceWidth);
            Assert.IsEmpty(dialogs.Errors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void MissingMetricsUnknownCharacterAndGtaIIIRemainDeterministicAndDoNotCrash()
    {
        var missing = new TxdViewerViewModel(CreateRequest(GXTType.GtaViceCity));
        Assert.IsNull(missing.PreviewImage);
        Assert.IsNull(missing.PreviewLayout);
        StringAssert.Contains(missing.PreviewStatus, "No font metrics");

        var unknown = new TxdViewerViewModel(CreateRequest(
            GXTType.GtaViceCity,
            metrics: CreateMetrics(defaultAdvance: 2)));
        unknown.PreviewText = "A漢B";
        Assert.IsNotNull(unknown.PreviewImage);
        Assert.HasCount(1, unknown.PreviewLayout?.Issues ?? []);
        StringAssert.Contains(unknown.PreviewStatus, "漢");

        var gtaIII = new TxdViewerViewModel(CreateRequest(
            GXTType.GtaIII,
            metrics: CreateMetrics(defaultAdvance: 2)));
        Assert.IsFalse(gtaIII.IsGameFontPreviewAvailable);
        Assert.IsNull(gtaIII.PreviewImage);
        Assert.IsFalse(gtaIII.UseBelarusianFontMetricsCommand.CanExecute(null));
    }

    [TestMethod]
    public void MalformedTexturePixels_DoNotCrashPreviewOrGlyphRefresh()
    {
        var viewModel = new TxdViewerViewModel(CreateRequest(
            GXTType.GtaViceCity,
            metrics: CreateMetrics(defaultAdvance: 2),
            malformedTextures: true));

        Assert.IsNull(viewModel.TextureImage);
        Assert.IsNull(viewModel.PreviewImage);
        StringAssert.Contains(viewModel.PreviewStatus, "could not be rendered");
        Assert.IsEmpty(viewModel.Glyphs);
    }

    [TestMethod]
    public void MissingTextureForSelectedStyle_DoesNotFallBackToSelectedAtlas()
    {
        var viewModel = new TxdViewerViewModel(CreateRequest(
            GXTType.GtaViceCity,
            metrics: CreateMetrics(defaultAdvance: 2),
            includeFont2: false));

        viewModel.SelectedFontStyle = viewModel.FontStyles.Single(option =>
            option.Style == ViceCityFontStyle.Bank);

        Assert.IsNull(viewModel.PreviewImage);
        Assert.IsNull(viewModel.PreviewLayout);
        StringAssert.Contains(viewModel.PreviewStatus, "font2");
        Assert.AreEqual("font1", viewModel.SelectedTexture?.Name);
    }

    private static CharacterMapEditorRequest CreateRequest(
        GXTType gameType,
        CharacterMapProfile? profile = null,
        FontMetricsProfile? metrics = null,
        bool malformedTextures = false,
        bool includeFont2 = true) => new(
        new TxdAttachment
        {
            Id = Guid.NewGuid(),
            OriginalFileName = "fonts.txd",
            DisplayName = "fonts",
            Data = [],
            Document = new TxdDocument
            {
                RenderWareVersion = 0,
                Textures = includeFont2
                    ?
                    [
                        CreateTexture("font1", malformedTextures),
                        CreateTexture("font2", malformedTextures),
                        CreateTexture("pager", malformedTextures),
                    ]
                    :
                    [
                        CreateTexture("font1", malformedTextures),
                        CreateTexture("pager", malformedTextures),
                    ],
            },
        },
        gameType,
        profile ?? new CharacterMapProfile(),
        [],
        [],
        GxtLanguage.English,
        metrics);

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

    private static TxdTexture CreateTexture(string name, bool malformedPixels = false) => new()
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
        PixelsBgra32 = malformedPixels ? [1] : new byte[32 * 32 * 4],
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
