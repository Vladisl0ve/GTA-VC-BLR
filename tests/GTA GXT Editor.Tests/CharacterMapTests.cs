using System.Text;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class CharacterMapTests
{
    [TestMethod]
    public void BelarusianPreset_LoadsTxdCompatible1CLayout()
    {
        const string expected =
            "АБВГДЕЁЖЗІЙКЛМНОПРСТУЎФХЦЧШЫЬЭЮЯ" +
            "абвгдеёжзійклмнопрстуўфхцчшыьэюя";
        var assetPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "ViceCity",
            "belarusian.gxtmap.json");

        Assert.IsTrue(File.Exists(assetPath));
        var assetProfile = CharacterMapFileSerializer.Load(assetPath);
        var profile = CharacterMapPresets.Belarusian;
        var decodeMap = profile.ToDecodeMap();

        CollectionAssert.AreEqual(
            assetProfile.Mappings.Select(mapping => mapping.Character).ToArray(),
            profile.Mappings.Select(mapping => mapping.Character).ToArray());
        CollectionAssert.AreEqual(
            assetProfile.Mappings.Select(mapping => mapping.PreferredCode).ToArray(),
            profile.Mappings.Select(mapping => mapping.PreferredCode).ToArray());
        Assert.HasCount(64, profile.Mappings);
        Assert.AreEqual(expected, new string(profile.Mappings.Select(mapping => mapping.Character).ToArray()));
        Assert.HasCount(64, profile.Mappings);
        Assert.HasCount(67, decodeMap);
        Assert.AreEqual('Ё', decodeMap[0x96]);
        Assert.AreEqual('І', decodeMap[(byte)'I']);
        Assert.AreEqual('Ў', decodeMap[0x86]);
        Assert.AreEqual('ё', decodeMap[0xAF]);
        Assert.AreEqual('і', decodeMap[(byte)'i']);
        Assert.AreEqual('ў', decodeMap[0x9D]);
        Assert.AreEqual('Т', decodeMap[0x91]);
        Assert.AreEqual('т', decodeMap[0xA8]);
        Assert.AreEqual('В', decodeMap[(byte)'B']);
        Assert.AreEqual('М', decodeMap[(byte)'M']);
        Assert.AreEqual('Н', decodeMap[(byte)'H']);
    }

    [TestMethod]
    public void BelarusianPreset_ReturnsIndependentCopies()
    {
        var first = CharacterMapPresets.Belarusian;
        first.Mappings[0].PreferredCode = 0x81;

        var second = CharacterMapPresets.Belarusian;

        Assert.AreEqual((byte)0x41, second.Mappings[0].PreferredCode);
    }

    [TestMethod]
    public void Json_RoundTripsCanonicalAndImportsShorthand()
    {
        var profile = new CharacterMapProfile
        {
            Mappings =
            [
                new CharacterMapEntry
                {
                    Character = 'В',
                    Codes = [0x81, (byte)'B'],
                    PreferredCode = 0x81,
                },
                new CharacterMapEntry
                {
                    Character = 'Ў',
                    Codes = [0x95],
                    PreferredCode = 0x95,
                },
            ],
        };

        var loaded = CharacterMapFileSerializer.Deserialize(
            CharacterMapFileSerializer.Serialize(profile));
        var shorthand = CharacterMapFileSerializer.Deserialize(
            Encoding.UTF8.GetBytes("{\"А\":\"0x80\",\"Ў\":\"0x95\"}"));

        CollectionAssert.AreEqual(new byte[] { 0x81, (byte)'B' }, loaded.Mappings[0].Codes);
        Assert.AreEqual((byte)0x81, loaded.Mappings[0].PreferredCode);
        Assert.AreEqual('А', shorthand.ToDecodeMap()[0x80]);
        Assert.AreEqual('Ў', shorthand.ToDecodeMap()[0x95]);
    }

    [TestMethod]
    public void LegacyTextDictionary_IsImported()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"gxt-map-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "characters.txt");
            File.WriteAllLines(path, ["128 А", "129 Б"], Encoding.UTF8);

            var profile = CharacterMapFileSerializer.Load(path);

            Assert.AreEqual('А', profile.ToDecodeMap()[0x80]);
            Assert.AreEqual('Б', profile.ToDecodeMap()[0x81]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void CustomCodec_PreservesTokensAndLatinWordsWithAsciiAliases()
    {
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceTexts: ["Hello"],
            language: GxtLanguage.English);
        manager.CyrillicCharsDictionary = new Dictionary<int[], char>
        {
            [[0x80, (byte)'B']] = 'В',
            [[0x81]] = 'Ж',
        };
        const string text = "~h~ В BETA Ж";

        var bytes = manager.ConvertTextToBytes(text);

        Assert.AreEqual((byte)'h', bytes[1 * 2]);
        Assert.AreEqual((byte)0x80, bytes[4 * 2]);
        Assert.AreEqual((byte)'B', bytes[6 * 2]);
        Assert.AreEqual(text, manager.ConvertBytesToText(bytes));
    }

    [TestMethod]
    public void Interpret_ChangesMeaningWithoutChangingBytes()
    {
        var manager = CreateManagerWithRawValue([0x80, 0, 0, 0]);
        var original = manager.GXTEntries.Single().Value.ToArray();
        var profile = SingleMapping('А', 0x80);

        var preview = CharacterMapService.Preview(manager, profile, CharacterMapApplyMode.Interpret);
        CharacterMapService.Apply(manager, profile, CharacterMapApplyMode.Interpret);

        Assert.IsTrue(preview.CanApply);
        Assert.AreEqual(1, preview.ChangedEntryCount);
        Assert.AreEqual(0, preview.ChangedByteCount);
        CollectionAssert.AreEqual(original, manager.GXTEntries.Single().Value);
        Assert.AreEqual("А", manager.ConvertBytesToText(manager.GXTEntries.Single().Value));
    }

    [TestMethod]
    public void Reencode_PreservesUnicodeAndChangesPreferredCode()
    {
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceTexts: ["А"],
            language: GxtLanguage.English);
        manager.CyrillicCharsDictionary = SingleMapping('А', 0x80).ToCharacterDictionary();
        manager.AddGXTEntry("HELLO", "А");
        var target = SingleMapping('А', 0x81);

        var preview = CharacterMapService.Preview(manager, target, CharacterMapApplyMode.Reencode);
        CharacterMapService.Apply(manager, target, CharacterMapApplyMode.Reencode);

        Assert.IsTrue(preview.CanApply);
        Assert.AreEqual(1, preview.ChangedEntryCount);
        Assert.IsGreaterThan(0, preview.ChangedByteCount);
        Assert.AreEqual((byte)0x81, manager.GXTEntries.Single().Value[0]);
        Assert.AreEqual("А", manager.ConvertBytesToText(manager.GXTEntries.Single().Value));
    }

    [TestMethod]
    public void Reencode_MissingCharacterDoesNotMutateManager()
    {
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceTexts: ["А"],
            language: GxtLanguage.English);
        var originalProfile = SingleMapping('А', 0x80);
        manager.CyrillicCharsDictionary = originalProfile.ToCharacterDictionary();
        manager.AddGXTEntry("HELLO", "А");
        var originalBytes = manager.GXTEntries.Single().Value.ToArray();
        var invalidTarget = new CharacterMapProfile();

        var preview = CharacterMapService.Preview(
            manager,
            invalidTarget,
            CharacterMapApplyMode.Reencode);

        Assert.IsFalse(preview.CanApply);
        Assert.Throws<InvalidDataException>(() => CharacterMapService.Apply(
            manager,
            invalidTarget,
            CharacterMapApplyMode.Reencode));
        CollectionAssert.AreEqual(originalBytes, manager.GXTEntries.Single().Value);
        Assert.AreEqual((byte)0x80, manager.CyrillicCharsDictionary.Single().Key[0]);
    }

    [TestMethod]
    public void Reencode_ConflictingProfileDoesNotMutateManager()
    {
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceTexts: ["А"],
            language: GxtLanguage.English);
        manager.CyrillicCharsDictionary = SingleMapping('А', 0x80).ToCharacterDictionary();
        manager.AddGXTEntry("HELLO", "А");
        var originalBytes = manager.GXTEntries.Single().Value.ToArray();
        var invalidTarget = new CharacterMapProfile
        {
            Mappings =
            [
                new CharacterMapEntry
                {
                    Character = 'А',
                    Codes = [0x81],
                    PreferredCode = 0x81,
                },
                new CharacterMapEntry
                {
                    Character = 'Б',
                    Codes = [0x81],
                    PreferredCode = 0x81,
                },
            ],
        };

        var preview = CharacterMapService.Preview(
            manager,
            invalidTarget,
            CharacterMapApplyMode.Reencode);

        Assert.IsFalse(preview.CanApply);
        Assert.Throws<InvalidDataException>(() => CharacterMapService.Apply(
            manager,
            invalidTarget,
            CharacterMapApplyMode.Reencode));
        CollectionAssert.AreEqual(originalBytes, manager.GXTEntries.Single().Value);
        Assert.AreEqual((byte)0x80, manager.CyrillicCharsDictionary.Single().Key[0]);
    }

    private static CharacterMapProfile SingleMapping(char character, byte code) => new()
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

    private static Contracts.CommonGXTManager CreateManagerWithRawValue(byte[] value)
    {
        var manager = GxtManagerFactory.Create(
            GXTType.GtaViceCity,
            sourceTexts: ["A"],
            language: GxtLanguage.English);
        manager.AddGXTEntry("HELLO", "A");
        manager.GXTEntries.Single().Value = value;
        return manager;
    }
}
