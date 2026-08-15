using System.IO;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Contracts;

public abstract class CommonGXTManager
{
    public abstract GxtLanguage Language { get; }

    public abstract string? CharacterMapPath { get; set; }

    public abstract CharacterMapProfile CharacterMap { get; set; }

    public IReadOnlyDictionary<byte, char> DecodeCharacterMap => CharacterMap.ToDecodeMap();

    public IReadOnlyDictionary<char, byte> EncodeCharacterMap => CharacterMap.ToEncodeMap();

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

    public virtual string ConvertBytesToText(byte[] inputBytes)
    {
        ArgumentNullException.ThrowIfNull(inputBytes);
        return Services.CharacterMapCodec.Decode(inputBytes, CharacterMap);
    }

    public virtual byte[] ConvertTextToBytes(string inputString)
    {
        ArgumentNullException.ThrowIfNull(inputString);
        return Services.CharacterMapCodec.Encode(inputString, CharacterMap);
    }

    public void ReloadCharacterMap()
    {
        if (CharacterMapPath is null)
        {
            throw new InvalidOperationException("Путь к пользовательскому маппингу не задан.");
        }

        CharacterMap = CharacterMapFileSerializer.Load(CharacterMapPath);
    }

}
