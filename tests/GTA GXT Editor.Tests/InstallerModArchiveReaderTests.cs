using System.IO.Compression;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class InstallerModArchiveReaderTests
{
    private string _testDirectory = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"gta-gxt-mod-archive-{Guid.NewGuid():N}");
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
    public void Read_ValidArchive_PreservesPathsAndIgnoresEmptyDirectories()
    {
        var archivePath = CreateArchive(
            ("plugins/config.ini", "enabled=1"u8.ToArray()),
            ("data/maps/example/file.dat", new byte[] { 1, 2, 3 }),
            ("empty/", Array.Empty<byte>()));

        var mod = InstallerModArchiveReader.Read(
            archivePath,
            "Example mod",
            isRequired: false,
            CreateProfile());

        Assert.AreEqual("Example mod", mod.Name);
        Assert.IsFalse(mod.IsRequired);
        Assert.HasCount(2, mod.Files);
        Assert.AreEqual("plugins\\config.ini", mod.Files[0].DestinationPath);
        Assert.AreEqual("data\\maps\\example\\file.dat", mod.Files[1].DestinationPath);
        CollectionAssert.AreEqual("enabled=1"u8.ToArray(), mod.Files[0].Data);
    }

    [TestMethod]
    public void Read_CommonTopLevelDirectory_IsRemovedAndDocumentationIsAccepted()
    {
        var archivePath = CreateArchive(
            ("GInputVC_BLR/GInputVC.asi", CreateX86PeImage()),
            ("GInputVC_BLR/ReadMe_GInput.txt", "Instructions"u8.ToArray()),
            ("GInputVC_BLR/docs/controls.md", "# Controls"u8.ToArray()));

        var mod = InstallerModArchiveReader.Read(
            archivePath,
            "GInputVC BLR",
            isRequired: false,
            CreateProfile());

        Assert.HasCount(3, mod.Files);
        Assert.AreEqual("GInputVC.asi", mod.Files[0].DestinationPath);
        Assert.AreEqual("ReadMe_GInput.txt", mod.Files[1].DestinationPath);
        Assert.AreEqual("docs\\controls.md", mod.Files[2].DestinationPath);
    }

    [TestMethod]
    public void Read_RootFilePreventsTopLevelDirectoryRemoval()
    {
        var archivePath = CreateArchive(
            ("config.ini", new byte[] { 1 }),
            ("plugins/example.dat", new byte[] { 2 }));

        var mod = InstallerModArchiveReader.Read(
            archivePath,
            "Mixed layout",
            isRequired: false,
            CreateProfile());

        Assert.AreEqual("config.ini", mod.Files[0].DestinationPath);
        Assert.AreEqual("plugins\\example.dat", mod.Files[1].DestinationPath);
    }

    [TestMethod]
    [DataRow("../outside.dat")]
    [DataRow("wrapper/../outside.dat")]
    [DataRow("/absolute.dat")]
    [DataRow("C:/absolute.dat")]
    public void Read_UnsafePath_IsRejected(string entryName)
    {
        var archivePath = CreateArchive((entryName, new byte[] { 1 }));

        Assert.Throws<InvalidDataException>(() => InstallerModArchiveReader.Read(
            archivePath,
            "Unsafe",
            isRequired: false,
            CreateProfile()));
    }

    [TestMethod]
    public void Read_DuplicateCaseInsensitiveDestination_IsRejected()
    {
        var archivePath = CreateArchive(
            ("wrapper/plugins/config.ini", new byte[] { 1 }),
            ("wrapper/PLUGINS/CONFIG.INI", new byte[] { 2 }));

        Assert.Throws<InvalidDataException>(() => InstallerModArchiveReader.Read(
            archivePath,
            "Duplicate",
            isRequired: false,
            CreateProfile()));
    }

    [TestMethod]
    public void Read_ConflictWithExistingPayload_IsRejected()
    {
        var archivePath = CreateArchive(("BelarusianLanguage.asi", CreateX86PeImage()));
        var profile = CreateProfile();
        profile.Assets.Add(new InstallerAsset
        {
            Id = Guid.NewGuid(),
            Role = InstallerAssetRole.MainAsi,
            OriginalFileName = "BelarusianLanguage.asi",
            DestinationPath = InstallerProfileValidator.MainAsiDestination,
            Data = CreateX86PeImage(),
        });

        Assert.Throws<InvalidDataException>(() => InstallerModArchiveReader.Read(
            archivePath,
            "Conflict",
            isRequired: true,
            profile));
    }

    [TestMethod]
    public void Read_ReservedGeneratedPayload_IsRejected()
    {
        var archivePath = CreateArchive(("wrapper/TEXT/BELARUS.GXT", new byte[] { 1 }));

        Assert.Throws<InvalidDataException>(() => InstallerModArchiveReader.Read(
            archivePath,
            "Conflict",
            isRequired: false,
            CreateProfile()));
    }

    [TestMethod]
    public void Read_InvalidX86Library_IsRejected()
    {
        var archivePath = CreateArchive(("plugins/broken.dll", new byte[] { 1, 2, 3 }));

        Assert.Throws<InvalidDataException>(() => InstallerModArchiveReader.Read(
            archivePath,
            "Broken library",
            isRequired: false,
            CreateProfile()));
    }

    [TestMethod]
    public void Read_NestedArchive_IsRejectedByPayloadRules()
    {
        var archivePath = CreateArchive(("wrapper/plugins/nested.zip", new byte[] { 1, 2, 3 }));

        Assert.Throws<InvalidDataException>(() => InstallerModArchiveReader.Read(
            archivePath,
            "Nested",
            isRequired: false,
            CreateProfile()));
    }

    [TestMethod]
    public void Read_EmptyArchive_IsRejected()
    {
        var archivePath = CreateArchive(("empty/", Array.Empty<byte>()));

        Assert.Throws<InvalidDataException>(() => InstallerModArchiveReader.Read(
            archivePath,
            "Empty",
            isRequired: false,
            CreateProfile()));
    }

    [TestMethod]
    public void Read_SymbolicLinkEntry_IsRejected()
    {
        var archivePath = Path.Combine(_testDirectory, "symlink.zip");
        using (var stream = File.Create(archivePath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("plugins/link.dat");
            entry.ExternalAttributes = unchecked((int)0xA0000000);
            using var output = entry.Open();
            output.WriteByte(1);
        }

        Assert.Throws<InvalidDataException>(() => InstallerModArchiveReader.Read(
            archivePath,
            "Link",
            isRequired: false,
            CreateProfile()));
    }

    [TestMethod]
    public void Read_WhenProfileReachedFileLimit_IsRejectedBeforeExtraction()
    {
        var archivePath = CreateArchive(("new.dat", new byte[] { 1 }));
        var profile = CreateProfile();
        for (var index = 0; index < InstallerProfileValidator.MaximumAssets; index++)
        {
            profile.Assets.Add(new InstallerAsset
            {
                Id = Guid.NewGuid(),
                Role = InstallerAssetRole.Additional,
                OriginalFileName = $"file-{index}.dat",
                DestinationPath = $"files\\file-{index}.dat",
                Data = [1],
            });
        }

        Assert.Throws<InvalidDataException>(() => InstallerModArchiveReader.Read(
            archivePath,
            "Too many",
            isRequired: false,
            profile));
    }

    private string CreateArchive(params (string Name, byte[] Data)[] entries)
    {
        var path = Path.Combine(_testDirectory, $"{Guid.NewGuid():N}.zip");
        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var item in entries)
        {
            var entry = archive.CreateEntry(item.Name);
            if (item.Name.EndsWith('/') || item.Name.EndsWith('\\'))
            {
                continue;
            }

            using var output = entry.Open();
            output.Write(item.Data);
        }

        return path;
    }

    private static InstallerProfile CreateProfile() => new()
    {
        ProductId = Guid.NewGuid(),
        Name = "Test installer",
        Version = "1.0.0",
        Publisher = "Tests",
        OutputFileName = "Setup.exe",
    };

    private static byte[] CreateX86PeImage()
    {
        var data = new byte[128];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BitConverter.TryWriteBytes(data.AsSpan(0x3C, 4), 0x40);
        "PE\0\0"u8.CopyTo(data.AsSpan(0x40));
        BitConverter.TryWriteBytes(data.AsSpan(0x44, 2), (ushort)0x014C);
        return data;
    }
}
