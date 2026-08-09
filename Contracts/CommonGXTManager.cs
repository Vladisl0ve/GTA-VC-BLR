using System.IO;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.Contracts;

public abstract class CommonGXTManager
{
    public abstract GxtLanguage Language { get; }

    public abstract string? CyrillicCharsDictionaryPath { get; set; }

    public abstract Dictionary<int[], char> CyrillicCharsDictionary { get; set; }

    public abstract List<GXTBase> GXTEntries { get; }

    public abstract void AddGXTEntry(
        string newDatName,
        string newDatValue,
        string? tableName = null);

    public abstract void EditGXTEntry(
        string datName,
        string newDatValue,
        string? currentTableName = null,
        string? newTableName = null);

    public abstract void RemoveGXTEntry(string datName, string? tableName = null);

    public virtual void SaveGXTChanges(string gxtFilePath) => WriteGXTFile(gxtFilePath);

    public virtual List<GXTBase> ReadGXTFile(string gxtFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gxtFilePath);
        using var stream = File.OpenRead(gxtFilePath);
        return ReadGXT(stream, Path.GetFileName(gxtFilePath));
    }

    public virtual void WriteGXTFile(string gxtFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gxtFilePath);
        using var stream = new FileStream(gxtFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
        WriteGXT(stream);
    }

    public abstract List<GXTBase> ReadGXT(Stream stream, string sourceName);

    public abstract void WriteGXT(Stream stream);

    public virtual IReadOnlyDictionary<byte, char> GetCharacterMap()
    {
        var result = new Dictionary<byte, char>();
        foreach (var pair in CyrillicCharsDictionary)
        {
            foreach (var index in pair.Key)
            {
                if (index is >= byte.MinValue and <= byte.MaxValue)
                {
                    result.TryAdd((byte)index, pair.Value);
                }
            }
        }

        return result;
    }

    public virtual string ConvertBytesToText(byte[] inputBytes)
    {
        ArgumentNullException.ThrowIfNull(inputBytes);
        return Services.CharacterMapCodec.Decode(inputBytes, CyrillicCharsDictionary);
    }

    public virtual byte[] ConvertTextToBytes(string inputString)
    {
        ArgumentNullException.ThrowIfNull(inputString);
        return Services.CharacterMapCodec.Encode(inputString, CyrillicCharsDictionary);
    }

    public void ReloadCyrillicCharsDictionary()
    {
        if (CyrillicCharsDictionaryPath is null)
        {
            throw new InvalidOperationException("Путь к пользовательскому словарю не задан.");
        }

        CyrillicCharsDictionary = CyrillicCharsDictionaryPath.LoadCyrillicCharsDictionary();
    }
}
