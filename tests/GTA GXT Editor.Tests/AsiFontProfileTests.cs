using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class AsiFontProfileTests
{
    private const string CanonicalAsiPath =
        @"D:\Development\GTA VC BLR\!ACTUAL_PIERAKŁAD\GTA_VC_BLR_Installer_Input_v1.2.21\02_Main_ASI\BelarusianLanguage_v1.2.34_fontv8.asi";
    private const string CanonicalSha256 =
        "17D98CC31D63067EB9040A44453D2EB09C983ED19C2AFAB276A227B2FBA523A7";

    private string _testDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"gta-gxt-asi-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Read_Canonical1234_ExtractsProvenMetricsMappingAndRuntimeContexts()
    {
        var data = ReadCanonicalAsi();
        var result = new AsiFontProfileReader().Read(data, Path.GetFileName(CanonicalAsiPath));

        Assert.AreEqual(AsiRecognitionLevel.ExactKnownBinary, result.RecognitionLevel);
        Assert.AreEqual(CanonicalSha256, result.Build?.FileSha256);
        Assert.AreEqual("1.2.34", result.Build?.Version);
        Assert.IsNotNull(result.FontMetrics);
        Assert.HasCount(FontMetricsTable.MetricCount, result.FontMetrics.Font2.Advances);
        Assert.HasCount(FontMetricsTable.MetricCount, result.FontMetrics.Font1.Advances);
        Assert.IsNotNull(result.CharacterMap);
        Assert.AreEqual((byte)0x91, result.CharacterMap.ToEncodeMap()['Т']);
        Assert.AreEqual((byte)0xA8, result.CharacterMap.ToEncodeMap()['т']);
        Assert.HasCount(63, result.PhysicalPatches);
        Assert.HasCount(2, result.ExecutableTargets);
        AssertOverride(result.FontMetrics, FontRenderContext.Gameplay, 0x91, 13);
        AssertOverride(result.FontMetrics, FontRenderContext.SaveLoad, 0x91, 15);
        AssertOverride(result.FontMetrics, FontRenderContext.SaveLoad, 0xA8, 15);
        AssertOverride(result.FontMetrics, FontRenderContext.ExitConfirmation, 0x91, 16);
        AssertOverride(result.FontMetrics, FontRenderContext.ExitConfirmation, 0xA8, 16);
        AssertOverride(result.FontMetrics, FontRenderContext.Heading, 0x91, 18);
        AssertOverride(result.FontMetrics, FontRenderContext.Heading, 0xA8, 18);
    }

    [TestMethod]
    public void ByxV6_RoundTrip_PreservesMainAsiBindingAndEffectiveOverrides()
    {
        var data = ReadCanonicalAsi();
        var project = CreateProject(data);
        var synchronizer = new AsiProjectFontProfileService();
        synchronizer.Synchronize(project);
        var effectiveMap = project.CharacterMap!.Clone();
        effectiveMap.Mappings.Single(item => item.Character == 'В').PreferredCode = 0x42;
        var effectiveMetrics = project.FontMetrics!.Clone();
        var metricIndex = FontMetricsService.GetMetricIndex(0x91);
        effectiveMetrics.Font1.Advances[metricIndex]++;
        effectiveMetrics.Overrides.RemoveAll(item =>
            item.Context == FontRenderContext.Heading &&
            item.Font == FontTextureKind.Font1 &&
            item.Code == 0x91);
        synchronizer.UpdateEffectiveProfiles(project, effectiveMap, effectiveMetrics);

        var path = Path.Combine(_testDirectory, "font-profile.byx");
        var serializer = new ByxProjectSerializer(new GxtManagerFactory(), new TxdReader());
        serializer.Save(path, project);
        var loaded = serializer.Load(path);

        Assert.IsNotNull(loaded.AsiFontProfileBinding);
        Assert.AreEqual(project.InstallerProfile!.Assets.Single().Id, loaded.AsiFontProfileBinding.AsiAssetId);
        Assert.AreEqual(CanonicalSha256, loaded.AsiFontProfileBinding.AsiSha256);
        Assert.HasCount(1, loaded.AsiFontProfileBinding.CharacterMapPatches);
        Assert.HasCount(1, loaded.AsiFontProfileBinding.FontAdvancePatches);
        Assert.HasCount(1, loaded.AsiFontProfileBinding.FontContextOverridePatches);
        Assert.AreEqual((byte)0x42, loaded.CharacterMap!.ToEncodeMap()['В']);
        Assert.AreEqual(
            effectiveMetrics.Font1.Advances[metricIndex],
            loaded.FontMetrics!.Font1.Advances[metricIndex]);
        Assert.IsFalse(loaded.FontMetrics.Overrides.Any(item =>
            item.Context == FontRenderContext.Heading && item.Code == 0x91));
        CollectionAssert.AreEqual(data, loaded.InstallerProfile!.Assets.Single().Data);

        using var archive = ZipFile.OpenRead(path);
        Assert.IsNotNull(archive.GetEntry("font/asi-binding.json"));
        var manifest = ReadJson(archive, "manifest.json");
        Assert.AreEqual(6, manifest["version"]!.GetValue<int>());
        Assert.AreEqual(
            Convert.ToHexStringLower(SHA256.HashData(data)),
            manifest["installer"]!["assets"]![0]!["sha256"]!.GetValue<string>());
    }

    [TestMethod]
    public void Load_V5CanonicalProject_MigratesToEmptyOverridesInMemory()
    {
        var data = ReadCanonicalAsi();
        var project = CreateProject(data);
        var serializer = new ByxProjectSerializer(new GxtManagerFactory(), new TxdReader());
        var path = Path.Combine(_testDirectory, "legacy-v5.byx");
        serializer.Save(path, project);
        ConvertToV5(path);

        var loaded = serializer.Load(path);

        Assert.IsNotNull(loaded.AsiFontProfileBinding);
        Assert.IsEmpty(loaded.AsiFontProfileBinding.CharacterMapPatches);
        Assert.IsEmpty(loaded.AsiFontProfileBinding.FontAdvancePatches);
        Assert.IsEmpty(loaded.AsiFontProfileBinding.FontContextOverridePatches);
        Assert.IsTrue(loaded.IsDirty);
    }

    [TestMethod]
    public void ReplaceMainAsi_WithConflictingRebase_PreservesPreviousProjectState()
    {
        var baseMap = CreateMap(('А', 0x80), ('Б', 0x81));
        var replacementBase = CreateMap(('А', 0x80), ('Б', 0x81), ('В', 0x82));
        var firstData = new byte[] { 1 };
        var secondData = new byte[] { 2 };
        var reader = new StubReader(new Dictionary<byte, CharacterMapProfile>
        {
            [1] = baseMap,
            [2] = replacementBase,
        });
        var synchronizer = new AsiProjectFontProfileService(reader);
        var project = CreateProject(firstData, baseMap);
        synchronizer.Synchronize(project);
        var edited = project.CharacterMap!.Clone();
        var letterA = edited.Mappings.Single(item => item.Character == 'А');
        letterA.Codes.Clear();
        letterA.Codes.Add(0x82);
        letterA.PreferredCode = 0x82;
        synchronizer.UpdateEffectiveProfiles(project, edited, null);
        var oldBinding = project.AsiFontProfileBinding!.Clone();
        var oldFingerprint = FontProfileFingerprint.Compute(project.CharacterMap);
        var replacement = CreateInstallerProfile(secondData);

        var exception = Assert.Throws<InvalidDataException>(() =>
            synchronizer.Synchronize(project, replacement));

        StringAssert.Contains(exception.Message, "conflicts");
        Assert.AreEqual(oldBinding.AsiAssetId, project.AsiFontProfileBinding!.AsiAssetId);
        Assert.AreEqual(oldBinding.AsiSha256, project.AsiFontProfileBinding.AsiSha256);
        Assert.AreEqual(oldFingerprint, FontProfileFingerprint.Compute(project.CharacterMap!));
        CollectionAssert.AreEqual(firstData, project.InstallerProfile!.Assets.Single().Data);
    }

    [TestMethod]
    public void ValidButUnknownAsi_DoesNotReplaceExistingManualProfiles()
    {
        var data = ReadCanonicalAsi();
        var markerOffset = data.AsSpan().IndexOf("BelarusianLanguage "u8);
        Assert.IsGreaterThanOrEqualTo(0, markerOffset);
        data[markerOffset] = (byte)'X';
        var read = new AsiFontProfileReader().Read(data, "unknown.asi");
        Assert.AreEqual(AsiRecognitionLevel.Unknown, read.RecognitionLevel);

        var manualMap = CreateMap(('Ж', 0x80));
        var project = CreateProject(data, manualMap);
        var originalFingerprint = FontProfileFingerprint.Compute(project.CharacterMap!);
        new AsiProjectFontProfileService().Synchronize(project);

        Assert.IsNull(project.AsiFontProfileBinding);
        Assert.AreEqual(AsiRecognitionLevel.Unknown, project.AsiFontProfileState?.RecognitionLevel);
        Assert.AreEqual(originalFingerprint, FontProfileFingerprint.Compute(project.CharacterMap!));
        CollectionAssert.AreEqual(data, project.InstallerProfile!.Assets.Single().Data);
    }

    private static EditorProject CreateProject(
        byte[] asiData,
        CharacterMapProfile? characterMap = null)
    {
        var map = characterMap?.Clone() ?? CharacterMapPresets.Belarusian;
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceName: "american.gxt",
            sourceTexts: ["Hello"],
            language: GxtLanguage.Belarusian);
        manager.CharacterMap = map.Clone();
        manager.AddGXTEntry("HELLO", "Hello");
        return new EditorProject
        {
            GxtSourceName = "american.gxt",
            GameType = GXTType.GtaViceCity,
            GxtManager = manager,
            UsesCustomDictionary = true,
            CharacterMap = map,
            FontMetrics = characterMap is null ? FontMetricsPresets.BelarusianViceCity : null,
            InstallerProfile = CreateInstallerProfile(asiData),
        };
    }

    private static InstallerProfile CreateInstallerProfile(byte[] data) => new()
    {
        Publisher = "Belarusian Games",
        Assets =
        [
            new InstallerAsset
            {
                Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                Role = InstallerAssetRole.MainAsi,
                OriginalFileName = "BelarusianLanguage.asi",
                DestinationPath = InstallerProfileValidator.MainAsiDestination,
                Data = data.ToArray(),
            },
        ],
    };

    private static CharacterMapProfile CreateMap(params (char Character, byte Code)[] mappings) =>
        new()
        {
            IsVerified = true,
            Mappings = mappings.Select(item => new CharacterMapEntry
            {
                Character = item.Character,
                Codes = [item.Code],
                PreferredCode = item.Code,
            }).ToList(),
        };

    private static byte[] ReadCanonicalAsi()
    {
        if (!File.Exists(CanonicalAsiPath))
        {
            Assert.Inconclusive($"Canonical ASI test input is unavailable: {CanonicalAsiPath}");
        }

        return File.ReadAllBytes(CanonicalAsiPath);
    }

    private static void AssertOverride(
        FontMetricsProfile profile,
        FontRenderContext context,
        byte code,
        ushort expected)
    {
        var item = profile.Overrides.Single(value =>
            value.Context == context && value.Font == FontTextureKind.Font1 && value.Code == code);
        Assert.AreEqual(expected, item.Advance);
    }

    private static JsonObject ReadJson(ZipArchive archive, string entryName)
    {
        using var reader = new StreamReader(archive.GetEntry(entryName)!.Open());
        return JsonNode.Parse(reader.ReadToEnd())!.AsObject();
    }

    private static void ConvertToV5(string path)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        archive.GetEntry("font/asi-binding.json")?.Delete();
        var manifest = ReadJson(archive, "manifest.json");
        manifest["version"] = 5;
        manifest.Remove("asiFontProfileBinding");
        archive.GetEntry("manifest.json")!.Delete();
        var entry = archive.CreateEntry("manifest.json");
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, manifest, new JsonSerializerOptions { WriteIndented = true });
    }

    private sealed class StubReader(
        IReadOnlyDictionary<byte, CharacterMapProfile> profiles) : IAsiFontProfileReader
    {
        public AsiFontProfileReadResult Read(ReadOnlyMemory<byte> data, string sourceName)
        {
            var bytes = data.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            return new AsiFontProfileReadResult(
                AsiRecognitionLevel.VersionedMetadata,
                new AsiPeInfo(0x014C, 0, 0x400000, 0x1000, []),
                new AsiBuildInfo("BelarusianLanguage", bytes[0].ToString(), null, hash),
                null,
                profiles[bytes[0]].Clone(),
                new AsiMappingAssociation("test", FontProfileFingerprint.Compute(profiles[bytes[0]]), true),
                [],
                [],
                []);
        }
    }
}
