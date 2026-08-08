using System.Text;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.Tests;

[TestClass]
public sealed class GxtManagerTests
{
    private string _testDirectory = null!;
    private string _dictionaryPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _testDirectory = Path.Combine(Path.GetTempPath(), $"gta-gxt-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _dictionaryPath = Path.Combine(_testDirectory, "characters.txt");
        File.WriteAllText(_dictionaryPath, string.Empty, Encoding.GetEncoding(1251));
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
    public void GtaIII_ReadEditWrite_RoundTrips()
    {
        var sourcePath = Path.Combine(_testDirectory, "gta3.gxt");
        var outputPath = Path.Combine(_testDirectory, "gta3-output.gxt");
        WriteGtaIIIFile(sourcePath, "HELLO", "Hello");

        var manager = new GTAIII.GXTManager(sourcePath, _dictionaryPath);

        Assert.HasCount(1, manager.GXTEntries);
        Assert.AreEqual("HELLO", manager.GXTEntries[0].DatName.GetClearName());
        Assert.AreEqual("Hello", manager.ConvertBytesToText(manager.GXTEntries[0].Value));

        manager.EditGXTEntry("HELLO", "Updated");
        manager.SaveGXTChanges(outputPath);

        var reopened = new GTAIII.GXTManager(outputPath, _dictionaryPath);
        Assert.AreEqual("Updated", reopened.ConvertBytesToText(reopened.GXTEntries[0].Value));
    }

    [TestMethod]
    public void ViceCity_ClearNames_CanBeEditedAndRemoved()
    {
        var sourcePath = Path.Combine(_testDirectory, "vice-city.gxt");
        var outputPath = Path.Combine(_testDirectory, "vice-city-output.gxt");
        WriteViceCityFile(sourcePath, "MAIN", "HELLO", "Hello");

        var manager = new GTAVC.GXTManager(sourcePath, _dictionaryPath);

        manager.EditGXTEntry("HELLO", "World", "MAIN");
        manager.SaveGXTChanges(outputPath);

        var reopened = new GTAVC.GXTManager(outputPath, _dictionaryPath);
        Assert.HasCount(1, reopened.GXTEntries);
        Assert.AreEqual("World", reopened.ConvertBytesToText(reopened.GXTEntries[0].Value));

        reopened.RemoveGXTEntry("HELLO", "MAIN");
        Assert.IsEmpty(reopened.GXTEntries);

        reopened.SaveGXTChanges(outputPath);
        var emptyReopened = new GTAVC.GXTManager(outputPath, _dictionaryPath);
        Assert.IsEmpty(emptyReopened.GXTEntries);
    }

    [TestMethod]
    public void Factory_DetectsBothSupportedFormats()
    {
        var gtaIIIPath = Path.Combine(_testDirectory, "gta3.gxt");
        var viceCityPath = Path.Combine(_testDirectory, "vice-city.gxt");
        WriteGtaIIIFile(gtaIIIPath, "HELLO", "Hello");
        WriteViceCityFile(viceCityPath, "MAIN", "HELLO", "Hello");

        var factory = new GxtManagerFactory();

        Assert.AreEqual(GXTType.GtaIII, factory.DetectType(gtaIIIPath));
        Assert.AreEqual(GXTType.GtaViceCity, factory.DetectType(viceCityPath));
    }

    [TestMethod]
    public void CharacterDictionary_RoundTripsBelarusianLetters()
    {
        File.WriteAllLines(
            _dictionaryPath,
            ["161 Ў", "162 ў", "178 І", "179 і"],
            Encoding.GetEncoding(1251));
        var sourcePath = Path.Combine(_testDirectory, "belarusian.gxt");
        WriteGtaIIIFile(sourcePath, "TEXT", "ASCII");
        var manager = new GTAIII.GXTManager(sourcePath, _dictionaryPath);

        var bytes = manager.ConvertTextToBytes("ЎўІі");
        var result = manager.ConvertBytesToText(bytes);

        Assert.AreEqual("ЎўІі", result);
    }

    private static void WriteGtaIIIFile(string path, string name, string value)
    {
        var valueBytes = EncodeValue(value);
        using var stream = File.Create(path);
        stream.WriteString("TKEY");
        stream.WriteInt(12);
        stream.WriteInt(0);
        stream.WriteString(name.FillWithZeros(8));
        stream.WriteString("TDAT");
        stream.WriteInt(valueBytes.Length);
        stream.WriteBytes(valueBytes);
    }

    private static void WriteViceCityFile(string path, string table, string name, string value)
    {
        var valueBytes = EncodeValue(value);
        using var stream = File.Create(path);
        stream.WriteString("TABL");
        stream.WriteInt(12);
        stream.WriteString(table.FillWithZeros(8));
        stream.WriteInt(20);
        stream.WriteString("TKEY");
        stream.WriteInt(12);
        stream.WriteInt(0);
        stream.WriteString(name.FillWithZeros(8));
        stream.WriteString("TDAT");
        stream.WriteInt(valueBytes.Length);
        stream.WriteBytes(valueBytes);
    }

    private static byte[] EncodeValue(string value)
    {
        var bytes = new byte[(value.Length + 1) * 2];
        for (var index = 0; index < value.Length; index++)
        {
            bytes[index * 2] = checked((byte)value[index]);
        }

        return bytes;
    }
}
