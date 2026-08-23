using System.Buffers.Binary;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class InstallerProfileValidatorTests
{
    [TestMethod]
    public void Validate_CompleteProfile_NormalizesRelativePaths()
    {
        var profile = CreateValidProfile();
        var hotel = profile.Assets.Single(asset =>
            asset.DestinationPath.Equals("data\\maps\\hotel\\hotel.IPL", StringComparison.OrdinalIgnoreCase));
        hotel.DestinationPath = "data/maps/hotel/hotel.IPL";

        InstallerProfileValidator.Validate(profile);

        Assert.AreEqual("data\\maps\\hotel\\hotel.IPL", hotel.DestinationPath);
    }

    [TestMethod]
    [DataRow("C:\\game\\file.ini")]
    [DataRow("..\\file.ini")]
    [DataRow("plugins\\..\\file.ini")]
    [DataRow("CON.txt")]
    [DataRow("folder\\LPT1.ini")]
    [DataRow("folder\\bad?.ini")]
    public void Validate_UnsafeDestination_IsRejected(string destination)
    {
        var profile = CreateValidProfile();
        profile.Assets.Add(new InstallerAsset
        {
            Id = Guid.NewGuid(),
            Role = InstallerAssetRole.Additional,
            OriginalFileName = "file.ini",
            DestinationPath = destination,
            Data = [1],
        });

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
    }

    [TestMethod]
    public void Validate_CaseInsensitiveDestinationConflict_IsRejected()
    {
        var profile = CreateValidProfile();
        profile.Assets.Add(CreateAdditional("plugins\\Settings.ini"));
        profile.Assets.Add(CreateAdditional("PLUGINS\\settings.INI"));

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
    }

    [TestMethod]
    [DataRow("TEXT\\BELARUS.GXT")]
    [DataRow("models\\fonts.txd")]
    [DataRow("BelarusianLanguage.ini")]
    public void Validate_AutomaticPayloadDestinationConflict_IsRejected(string destination)
    {
        var profile = CreateValidProfile();
        profile.Assets.Add(CreateAdditional(destination));

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
    }

    [TestMethod]
    [DataRow("README.txt")]
    [DataRow("APPLY_MOD.cmd")]
    [DataRow("CLEAN_MOD.cmd")]
    [DataRow("checksums.sha256")]
    [DataRow("source.cpp")]
    [DataRow("package.zip")]
    public void Validate_ExcludedGamePayload_IsRejected(string destination)
    {
        var profile = CreateValidProfile();
        profile.Assets.Add(CreateAdditional(destination));

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
    }

    [TestMethod]
    public void Validate_NonX86Dll_IsRejected()
    {
        var profile = CreateValidProfile();
        var loader = CreateBinary(InstallerAssetRole.AsiLoader, "dinput8.dll");
        loader.Data[0] = 0;
        profile.Assets.Add(loader);

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
    }

    [TestMethod]
    public void Validate_CompleteProfileWithoutAsiLoader_IsAccepted()
    {
        var profile = CreateValidProfile();

        InstallerProfileValidator.Validate(profile);
    }

    [TestMethod]
    public void Validate_AsiLoader_IsRejectedForExport()
    {
        var profile = CreateValidProfile();
        profile.Assets.Add(CreateBinary(InstallerAssetRole.AsiLoader, "dinput8.dll"));

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
    }

    [TestMethod]
    public void Validate_MissingSilentPatchFile_IsRejected()
    {
        var profile = CreateValidProfile();
        profile.Assets.Remove(profile.Assets.Single(asset =>
            asset.DestinationPath.Equals("SilentPatchVC.ini", StringComparison.OrdinalIgnoreCase)));

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
    }

    [TestMethod]
    public void Validate_AdditionalFile_IsRejectedForExport()
    {
        var profile = CreateValidProfile();
        profile.Assets.Add(CreateAdditional("plugins\\settings.ini"));

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
    }

    [TestMethod]
    public void Validate_UnknownRole_IsRejected()
    {
        var profile = CreateValidProfile();
        profile.Assets[0] = new InstallerAsset
        {
            Id = Guid.NewGuid(),
            Role = (InstallerAssetRole)999,
            OriginalFileName = "unknown.bin",
            DestinationPath = "unknown.bin",
            Data = [1],
        };

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
    }

    [TestMethod]
    public void ValidateForStorage_EmptyDraft_IsAccepted()
    {
        var profile = CreateValidProfile();
        profile.Assets.Clear();

        InstallerProfileValidator.ValidateForStorage(profile);
    }

    [TestMethod]
    public void ValidateForStorage_MissingMainAsiOrSilentPatch_IsAccepted()
    {
        var withoutMainAsi = CreateValidProfile();
        withoutMainAsi.Assets.RemoveAll(asset => asset.Role == InstallerAssetRole.MainAsi);
        var withoutSilentPatch = CreateValidProfile();
        withoutSilentPatch.Assets.RemoveAll(asset => asset.Role == InstallerAssetRole.SilentPatch);

        InstallerProfileValidator.ValidateForStorage(withoutMainAsi);
        InstallerProfileValidator.ValidateForStorage(withoutSilentPatch);
    }

    [TestMethod]
    public void Validate_MissingModelsArchive_IsRejected()
    {
        var profile = CreateValidProfile();
        profile.Assets.RemoveAll(asset => asset.Role == InstallerAssetRole.ModelsArchive);

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
    }

    [TestMethod]
    public void ValidateForStorage_MissingOrPartialModelsArchive_IsAccepted()
    {
        var withoutArchive = CreateValidProfile();
        withoutArchive.Assets.RemoveAll(asset => asset.Role == InstallerAssetRole.ModelsArchive);
        var onlyImg = CreateValidProfile();
        onlyImg.Assets.RemoveAll(asset =>
            asset.Role == InstallerAssetRole.ModelsArchive &&
            asset.DestinationPath.Equals(
                InstallerProfileValidator.Gta3DirDestination,
                StringComparison.OrdinalIgnoreCase));

        InstallerProfileValidator.ValidateForStorage(withoutArchive);
        InstallerProfileValidator.ValidateForStorage(onlyImg);
    }

    [TestMethod]
    public void ValidateForStorage_ModelsArchiveWithWrongDestination_IsRejected()
    {
        var profile = CreateValidProfile();
        var archive = profile.Assets.Single(asset =>
            asset.Role == InstallerAssetRole.ModelsArchive &&
            asset.DestinationPath.Equals(
                InstallerProfileValidator.Gta3ImgDestination,
                StringComparison.OrdinalIgnoreCase));
        archive.DestinationPath = "MODELS\\other.img";

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.ValidateForStorage(profile));
    }

    [TestMethod]
    public void Validate_TwentyThreeGameTxd_IsAccepted()
    {
        var profile = CreateValidProfile();
        for (var index = 0; index < InstallerProfileValidator.MaximumGameTxdAssets; index++)
        {
            profile.Assets.Add(CreateGameTxd($"loadsc{index}.txd"));
        }

        InstallerProfileValidator.Validate(profile);
        InstallerProfileValidator.ValidateForStorage(profile);
    }

    [TestMethod]
    public void Validate_TwentyFourGameTxd_IsRejected()
    {
        var profile = CreateValidProfile();
        for (var index = 0; index <= InstallerProfileValidator.MaximumGameTxdAssets; index++)
        {
            profile.Assets.Add(CreateGameTxd($"loadsc{index}.txd"));
        }

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.ValidateForStorage(profile));
    }

    [TestMethod]
    [DataRow("txd\\nested\\signs.txd")]
    [DataRow("txd\\signs.png")]
    [DataRow("MODELS\\signs.txd")]
    [DataRow("signs.txd")]
    public void Validate_InvalidGameTxdDestination_IsRejected(string destination)
    {
        var profile = CreateValidProfile();
        profile.Assets.Add(new InstallerAsset
        {
            Id = Guid.NewGuid(),
            Role = InstallerAssetRole.GameTxd,
            OriginalFileName = Path.GetFileName(destination),
            DestinationPath = destination,
            Data = [1],
        });

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.ValidateForStorage(profile));
    }

    [TestMethod]
    public void GetGameTxdDestination_UsesFileNameUnderTxd()
    {
        Assert.AreEqual("txd\\loadsc0.txd", InstallerProfileValidator.GetGameTxdDestination("loadsc0.txd"));
        Assert.IsTrue(InstallerProfileValidator.IsGameTxdDestination("txd\\loadsc0.txd"));
        Assert.IsFalse(InstallerProfileValidator.IsGameTxdDestination("txd\\a\\b.txd"));
    }

    [TestMethod]
    public void ValidateForStorage_PresentMainAsiWithWrongDestination_IsRejected()
    {
        var profile = CreateValidProfile();
        var mainAsi = profile.Assets.Single(asset => asset.Role == InstallerAssetRole.MainAsi);
        mainAsi.DestinationPath = "plugins\\BelarusianLanguage.asi";

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.ValidateForStorage(profile));
    }

    [TestMethod]
    public void Validate_EmptyDraft_IsRejectedForExport()
    {
        var profile = CreateValidProfile();
        profile.Assets.Clear();

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
    }

    [TestMethod]
    public void ValidateForStorage_LegacyRolesRemainReadable()
    {
        var profile = CreateValidProfile();
        profile.Assets.RemoveAll(asset =>
            asset.Role == InstallerAssetRole.SilentPatch &&
            !asset.DestinationPath.Equals("SilentPatchVC.asi", StringComparison.OrdinalIgnoreCase));
        profile.Assets.Add(CreateBinary(InstallerAssetRole.AsiLoader, "dinput8.dll"));
        profile.Assets.Add(CreateAdditional("plugins\\settings.ini"));

        InstallerProfileValidator.ValidateForStorage(profile);
    }

    [TestMethod]
    public void Validate_MoreThan512Attachments_IsRejected()
    {
        var profile = CreateValidProfile();
        for (var index = profile.Assets.Count; index <= InstallerProfileValidator.MaximumAssets; index++)
        {
            profile.Assets.Add(new InstallerAsset
            {
                Id = Guid.NewGuid(),
                Role = InstallerAssetRole.Additional,
                OriginalFileName = $"file-{index}.ini",
                DestinationPath = $"extra\\file-{index}.ini",
                Data = [1],
            });
        }

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
    }

    [TestMethod]
    public void Validate_WithoutReleaseReadMes_Succeeds()
    {
        InstallerProfileValidator.Validate(CreateValidProfile());
        InstallerProfileValidator.ValidateForStorage(CreateValidProfile());
    }

    [TestMethod]
    public void ValidateForReleaseZip_RequiresBothReadMes()
    {
        var profile = CreateValidProfile();
        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.ValidateForReleaseZip(profile));

        profile.ReleaseReadMeEnglish = CreateReadMe("ReadMe.txt", "English");
        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.ValidateForReleaseZip(profile));

        profile.ReleaseReadMeBelarusian = CreateReadMe("ПрачытайМяне.txt", "Беларуская");
        InstallerProfileValidator.ValidateForReleaseZip(profile);
        InstallerProfileValidator.Validate(profile);
    }

    [TestMethod]
    public void ValidateForStorage_AcceptsOptionalReadMes()
    {
        var profile = CreateValidProfile();
        profile.Assets.Clear();
        profile.ReleaseReadMeEnglish = CreateReadMe("notes.txt", "draft");

        InstallerProfileValidator.ValidateForStorage(profile);
    }

    [TestMethod]
    public void Validate_ReadMeWithWrongExtension_IsRejected()
    {
        var profile = CreateValidProfile();
        profile.ReleaseReadMeEnglish = CreateReadMe("ReadMe.md", "English");

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.ValidateForStorage(profile));
    }

    [TestMethod]
    public void Validate_ReadMeSharingAssetId_IsRejected()
    {
        var profile = CreateValidProfile();
        var mainAsi = profile.Assets.Single(asset => asset.Role == InstallerAssetRole.MainAsi);
        profile.ReleaseReadMeEnglish = new InstallerReleaseDocument
        {
            Id = mainAsi.Id,
            OriginalFileName = "ReadMe.txt",
            Data = "English"u8.ToArray(),
        };

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.ValidateForStorage(profile));
    }

    [TestMethod]
    public void Validate_EmptyReadMe_IsRejected()
    {
        var profile = CreateValidProfile();
        profile.ReleaseReadMeEnglish = new InstallerReleaseDocument
        {
            Id = Guid.NewGuid(),
            OriginalFileName = "ReadMe.txt",
            Data = [],
        };

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.ValidateForStorage(profile));
    }

    private static InstallerProfile CreateValidProfile() => new()
    {
        ProductId = Guid.NewGuid(),
        Name = "Belarusian",
        Version = "1.0.0",
        Publisher = "Belarusian Games",
        OutputFileName = "Setup.exe",
        Assets =
        [
            CreateBinary(InstallerAssetRole.MainAsi, InstallerProfileValidator.MainAsiDestination),
            CreatePayload(
                InstallerAssetRole.ModelsArchive,
                InstallerProfileValidator.Gta3ImgDestination),
            CreatePayload(
                InstallerAssetRole.ModelsArchive,
                InstallerProfileValidator.Gta3DirDestination),
            .. InstallerProfileValidator.SilentPatchDestinations.Select(destination =>
                Path.GetExtension(destination).Equals(".asi", StringComparison.OrdinalIgnoreCase)
                    ? CreateBinary(InstallerAssetRole.SilentPatch, destination)
                    : new InstallerAsset
                    {
                        Id = Guid.NewGuid(),
                        Role = InstallerAssetRole.SilentPatch,
                        OriginalFileName = Path.GetFileName(destination),
                        DestinationPath = destination,
                        Data = [1, 2, 3],
                    }),
        ],
    };

    private static InstallerAsset CreateBinary(InstallerAssetRole role, string destination) => new()
    {
        Id = Guid.NewGuid(),
        Role = role,
        OriginalFileName = destination,
        DestinationPath = destination,
        Data = CreateX86PeImage(),
    };

    private static InstallerAsset CreateAdditional(string destination) => new()
    {
        Id = Guid.NewGuid(),
        Role = InstallerAssetRole.Additional,
        OriginalFileName = "file.ini",
        DestinationPath = destination,
        Data = [1],
    };

    private static InstallerAsset CreatePayload(InstallerAssetRole role, string destination) => new()
    {
        Id = Guid.NewGuid(),
        Role = role,
        OriginalFileName = Path.GetFileName(destination),
        DestinationPath = destination,
        Data = [1, 2, 3],
    };

    private static InstallerAsset CreateGameTxd(string fileName) => new()
    {
        Id = Guid.NewGuid(),
        Role = InstallerAssetRole.GameTxd,
        OriginalFileName = fileName,
        DestinationPath = InstallerProfileValidator.GetGameTxdDestination(fileName),
        Data = [1],
    };

    private static InstallerReleaseDocument CreateReadMe(string fileName, string text) => new()
    {
        Id = Guid.NewGuid(),
        OriginalFileName = fileName,
        Data = System.Text.Encoding.UTF8.GetBytes(text),
    };

    private static byte[] CreateX86PeImage()
    {
        var data = new byte[128];
        data[0] = (byte)'M';
        data[1] = (byte)'Z';
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(0x3C, 4), 0x40);
        "PE\0\0"u8.CopyTo(data.AsSpan(0x40));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x44, 2), 0x014C);
        return data;
    }
}
