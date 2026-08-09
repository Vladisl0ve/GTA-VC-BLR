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

        if (inputBytes.Length == 0)
        {
            return string.Empty;
        }

        var charactersByByte = CyrillicCharsDictionary
            .SelectMany(pair => pair.Key.Select(index => (Index: index, pair.Value)))
            .ToDictionary(pair => pair.Index, pair => pair.Value);
        var result = new char[(inputBytes.Length + 1) / 2];
        var resultLength = 0;

        for (var index = 0; index < inputBytes.Length; index += 2)
        {
            var value = inputBytes[index];
            if (value == 0)
            {
                break;
            }

            result[resultLength++] = charactersByByte.GetValueOrDefault(value, (char)value);
        }

        return new string(result, 0, resultLength);
    }

    public virtual byte[] ConvertTextToBytes(string inputString)
    {
        ArgumentNullException.ThrowIfNull(inputString);

        var bytesByCharacter = CyrillicCharsDictionary
            .ToDictionary(pair => pair.Value, pair => checked((byte)pair.Key[0]));
        var targetBytes = new byte[(inputString.Length + 1) * 2];

        for (var index = 0; index < inputString.Length; index++)
        {
            if (bytesByCharacter.TryGetValue(inputString[index], out var dictionaryByte))
            {
                targetBytes[index * 2] = dictionaryByte;
            }
            else if (inputString[index] <= byte.MaxValue)
            {
                targetBytes[index * 2] = (byte)inputString[index];
            }
            else
            {
                throw new InvalidDataException(
                    $"Символ '{inputString[index]}' отсутствует в выбранном словаре символов.");
            }
        }

        return targetBytes;
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
