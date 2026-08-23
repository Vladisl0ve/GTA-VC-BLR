using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class ReleaseZipExportServiceTests
{
    private string _testDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"gta-gxt-release-zip-tests-{Guid.NewGuid():N}");
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
    public async Task BuildAsync_PacksInstallerReadMesAndChecksum()
    {
        var installerBytes = "installer-bytes"u8.ToArray();
        var english = "English README"u8.ToArray();
        var belarusian = Encoding.UTF8.GetBytes("Беларускі README");
        var installer = new FakeInstallerExportService { InstallerBytes = installerBytes };
        var service = new ReleaseZipExportService(installer);
        var project = CreateProject(english, belarusian);
        var target = Path.Combine(_testDirectory, "output", "Release.zip");

        await service.BuildAsync(project, target, CancellationToken.None);

        Assert.AreEqual(1, installer.CallCount);
        Assert.IsTrue(File.Exists(target));
        var entries = ReadArchiveEntries(target);
        Assert.HasCount(4, entries);
        CollectionAssert.AreEqual(installerBytes, entries["Setup.exe"]);
        CollectionAssert.AreEqual(english, entries[ReleaseZipExportService.EnglishReadMeFileName]);
        CollectionAssert.AreEqual(belarusian, entries[ReleaseZipExportService.BelarusianReadMeFileName]);
        var checksumName = ReleaseZipExportService.CreateChecksumFileName("Setup.exe");
        var checksum = Encoding.UTF8.GetString(entries[checksumName]);
        var hash = Convert.ToHexStringLower(SHA256.HashData(installerBytes));
        Assert.AreEqual($"{hash}  Setup.exe\n", checksum);
        CollectionAssert.AreEqual(
            ReleaseZipExportService.CreateChecksumFile("Setup.exe", installerBytes),
            entries[checksumName]);
        Assert.IsFalse(checksum.StartsWith('\uFEFF'));
    }

    [TestMethod]
    public async Task BuildAsync_UsesCanonicalReadMeNames()
    {
        var installer = new FakeInstallerExportService();
        var service = new ReleaseZipExportService(installer);
        var project = CreateProject("en"u8.ToArray(), "be"u8.ToArray());
        project.InstallerProfile!.ReleaseReadMeEnglish = new InstallerReleaseDocument
        {
            Id = Guid.NewGuid(),
            OriginalFileName = "notes-en.txt",
            Data = "en"u8.ToArray(),
        };
        project.InstallerProfile.ReleaseReadMeBelarusian = new InstallerReleaseDocument
        {
            Id = Guid.NewGuid(),
            OriginalFileName = "notes-be.txt",
            Data = "be"u8.ToArray(),
        };
        var target = Path.Combine(_testDirectory, "canonical.zip");

        await service.BuildAsync(project, target, CancellationToken.None);

        using var archive = ZipFile.OpenRead(target);
        Assert.IsNotNull(archive.GetEntry(ReleaseZipExportService.EnglishReadMeFileName));
        Assert.IsNotNull(archive.GetEntry(ReleaseZipExportService.BelarusianReadMeFileName));
        Assert.IsNull(archive.GetEntry("notes-en.txt"));
        Assert.IsNull(archive.GetEntry("notes-be.txt"));
    }

    [TestMethod]
    public async Task BuildAsync_WithoutReadMes_DoesNotBuildInstaller()
    {
        var installer = new FakeInstallerExportService();
        var service = new ReleaseZipExportService(installer);
        var project = CreateProject(null, null);
        var target = Path.Combine(_testDirectory, "missing.zip");

        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.BuildAsync(project, target, CancellationToken.None));

        Assert.AreEqual(0, installer.CallCount);
        Assert.IsFalse(File.Exists(target));
    }

    [TestMethod]
    public async Task BuildAsync_InstallerFailure_PreservesExistingZipAndCleansStaging()
    {
        var installer = new FakeInstallerExportService
        {
            Exception = new InvalidOperationException("compiler failed"),
        };
        var service = new ReleaseZipExportService(installer);
        var target = Path.Combine(_testDirectory, "existing.zip");
        await File.WriteAllBytesAsync(target, [9, 9, 9]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.BuildAsync(CreateProject("en"u8.ToArray(), "be"u8.ToArray()), target, CancellationToken.None));

        CollectionAssert.AreEqual(new byte[] { 9, 9, 9 }, File.ReadAllBytes(target));
    }

    [TestMethod]
    public async Task BuildAsync_Cancellation_ProducesNoTarget()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var installer = new FakeInstallerExportService();
        var service = new ReleaseZipExportService(installer);
        var target = Path.Combine(_testDirectory, "cancelled.zip");

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.BuildAsync(
                CreateProject("en"u8.ToArray(), "be"u8.ToArray()),
                target,
                cancellation.Token));

        Assert.IsFalse(File.Exists(target));
        Assert.AreEqual(0, installer.CallCount);
    }

    private static EditorProject CreateProject(byte[]? english, byte[]? belarusian)
    {
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceName: "american.gxt",
            sourceTexts: ["Hello"],
            language: GxtLanguage.Belarusian);
        manager.AddGXTEntry("HELLO", "Прывітанне");
        return new EditorProject
        {
            GxtSourceName = "american.gxt",
            GameType = GXTType.GtaViceCity,
            GxtManager = manager,
            AttachedTxd = new TxdAttachment
            {
                Id = Guid.NewGuid(),
                OriginalFileName = "fonts.txd",
                DisplayName = "fonts",
                Data = [10, 20, 30],
                Document = new TxdDocument { RenderWareVersion = 0, Textures = [] },
            },
            InstallerProfile = new InstallerProfile
            {
                ProductId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                Name = "Belarusian",
                Version = "1.2.3",
                Publisher = "Belarusian Games",
                OutputFileName = "Setup.exe",
                Assets =
                [
                    CreateBinary(1, InstallerAssetRole.MainAsi, "BelarusianLanguage.asi"),
                    .. InstallerProfileValidator.SilentPatchDestinations.Select((destination, index) =>
                        CreateSilentPatchAsset(index + 2, destination)),
                    CreatePayload(12, InstallerAssetRole.ModelsArchive, InstallerProfileValidator.Gta3ImgDestination),
                    CreatePayload(13, InstallerAssetRole.ModelsArchive, InstallerProfileValidator.Gta3DirDestination),
                ],
                ReleaseReadMeEnglish = english is null ? null : new InstallerReleaseDocument
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000000064"),
                    OriginalFileName = "ReadMe.txt",
                    Data = english,
                },
                ReleaseReadMeBelarusian = belarusian is null ? null : new InstallerReleaseDocument
                {
                    Id = Guid.Parse("00000000-0000-0000-0000-000000000065"),
                    OriginalFileName = "ПрачытайМяне.txt",
                    Data = belarusian,
                },
            },
        };
    }

    private static InstallerAsset CreateBinary(int id, InstallerAssetRole role, string destination) => new()
    {
        Id = new Guid(id, 0, 0, new byte[8]),
        Role = role,
        OriginalFileName = destination,
        DestinationPath = destination,
        Data = CreateX86PeImage(),
    };

    private static InstallerAsset CreateSilentPatchAsset(int id, string destination) =>
        Path.GetExtension(destination).Equals(".asi", StringComparison.OrdinalIgnoreCase)
            ? CreateBinary(id, InstallerAssetRole.SilentPatch, destination)
            : CreatePayload(id, InstallerAssetRole.SilentPatch, destination);

    private static InstallerAsset CreatePayload(int id, InstallerAssetRole role, string destination) => new()
    {
        Id = new Guid(id, 0, 0, new byte[8]),
        Role = role,
        OriginalFileName = Path.GetFileName(destination),
        DestinationPath = destination,
        Data = Encoding.UTF8.GetBytes(destination),
    };

    private static byte[] CreateX86PeImage()
    {
        var data = new byte[128];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3C, 4), 0x40);
        "PE\0\0"u8.CopyTo(data.AsSpan(0x40));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x44, 2), 0x014C);
        return data;
    }

    private static Dictionary<string, byte[]> ReadArchiveEntries(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            entryStream.CopyTo(buffer);
            result.Add(entry.FullName, buffer.ToArray());
        }

        return result;
    }

    private sealed class FakeInstallerExportService : IInstallerExportService
    {
        public byte[] InstallerBytes { get; init; } = [1, 2, 3, 4];

        public int CallCount { get; private set; }

        public Exception? Exception { get; init; }

        public Task BuildAsync(
            EditorProject projectSnapshot,
            string targetPath,
            CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Exception is not null)
            {
                throw Exception;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllBytes(targetPath, InstallerBytes);
            return Task.CompletedTask;
        }
    }
}
