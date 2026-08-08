using System.IO;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.Contracts;

public abstract class CommonGXTManager
{
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

    public abstract void SaveGXTChanges(string gxtFilePath);

    public abstract List<GXTBase> ReadGXTFile(string gxtFilePath);

    public abstract void WriteGXTFile(string gxtFilePath);

    public string ConvertBytesToText(byte[] inputBytes)
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

    public byte[] ConvertTextToBytes(string inputString)
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
