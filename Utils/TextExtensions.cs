using System.Globalization;
using System.IO;
using System.Text;

namespace GTA_GXT_Editor.Utils;

public static class TextExtensions
{
    public static bool GXTValueIsValid(this byte[] inputBytes)
    {
        ArgumentNullException.ThrowIfNull(inputBytes);

        if (inputBytes.Length < 2 || inputBytes.Length % 2 != 0)
        {
            return false;
        }

        for (var index = 0; index < inputBytes.Length - 2; index += 2)
        {
            if (inputBytes[index] == 0 && inputBytes[index + 1] == 0)
            {
                return false;
            }
        }

        return inputBytes[^2] == 0 && inputBytes[^1] == 0;
    }

    public static string FillWithZeros(this string inputString, int desiredLength)
    {
        ArgumentNullException.ThrowIfNull(inputString);

        if (inputString.Length > desiredLength)
        {
            throw new ArgumentException(
                $"Строка не может быть длиннее {desiredLength} символов.",
                nameof(inputString));
        }

        return inputString.PadRight(desiredLength, '\0');
    }

    public static Dictionary<int[], char> LoadCyrillicCharsDictionary(this string charsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(charsFilePath);

        var dictionary = new Dictionary<int[], char>();
        foreach (var (line, lineNumber) in File.ReadLines(charsFilePath, Encoding.GetEncoding(1251))
                     .Select((line, index) => (line, index + 1)))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || string.IsNullOrEmpty(parts[1]))
            {
                throw new FormatException(
                    $"Некорректная строка {lineNumber} в словаре '{Path.GetFileName(charsFilePath)}'.");
            }

            var indexes = parts[0]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => int.Parse(value, CultureInfo.InvariantCulture))
                .ToArray();
            dictionary.Add(indexes, parts[1][0]);
        }

        return dictionary;
    }

    public static string GetClearName(this string dirtyName)
    {
        ArgumentNullException.ThrowIfNull(dirtyName);
        var nullIndex = dirtyName.IndexOf('\0');
        return nullIndex < 0 ? dirtyName : dirtyName[..nullIndex];
    }
}
