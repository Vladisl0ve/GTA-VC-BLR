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
        profile.Assets.Add(new InstallerAsset
        {
            Id = Guid.NewGuid(),
            Role = InstallerAssetRole.Additional,
            OriginalFileName = "settings.ini",
            DestinationPath = "plugins/settings.ini",
            Data = [1, 2, 3],
        });

        InstallerProfileValidator.Validate(profile);

        Assert.AreEqual("plugins\\settings.ini", profile.Assets[^1].DestinationPath);
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
    [DataRow("fontb.txd")]
    [DataRow("models\\fonts.txd")]
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
    public void Validate_MissingAsiLoader_IsAccepted()
    {
        var profile = CreateValidProfile();

        InstallerProfileValidator.Validate(profile);
    }

    [TestMethod]
    public void Validate_OptionalAsiLoader_IsAccepted()
    {
        var profile = CreateValidProfile();
        profile.Assets.Add(CreateBinary(InstallerAssetRole.AsiLoader, "dinput8.dll"));

        InstallerProfileValidator.Validate(profile);
    }

    [TestMethod]
    public void Validate_MissingRequiredResource_IsRejected()
    {
        var profile = CreateValidProfile();
        profile.Assets.RemoveAll(asset => asset.Role == InstallerAssetRole.SilentPatch);

        Assert.Throws<InvalidDataException>(() => InstallerProfileValidator.Validate(profile));
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

    private static InstallerProfile CreateValidProfile() => new()
    {
        ProductId = Guid.NewGuid(),
        Name = "Belarusian",
        Version = "1.0.0",
        Publisher = "Belarusian Games",
        OutputFileName = "Setup.exe",
        Assets =
        [
            CreateBinary(InstallerAssetRole.MainAsi, "BelarusianLanguage.asi"),
            CreateBinary(InstallerAssetRole.SilentPatch, "SilentPatchVC.asi"),
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
