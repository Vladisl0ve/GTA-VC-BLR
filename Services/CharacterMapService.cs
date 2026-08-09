using System.Globalization;
using System.IO;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class CharacterMapService
{
    public static IReadOnlyList<string> Validate(CharacterMapProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var issues = new List<string>();
        if (profile.Version != CharacterMapProfile.CurrentVersion)
        {
            issues.Add($"Версия профиля {profile.Version} не поддерживается.");
        }

        var characters = new HashSet<char>();
        var codes = new HashSet<byte>();
        foreach (var mapping in profile.Mappings)
        {
            if (char.IsControl(mapping.Character) || char.IsSurrogate(mapping.Character))
            {
                issues.Add($"Символ U+{(int)mapping.Character:X4} нельзя назначить ячейке шрифта.");
            }

            if (!characters.Add(mapping.Character))
            {
                issues.Add($"Символ '{mapping.Character}' описан несколько раз.");
            }

            if (mapping.Codes.Count == 0)
            {
                issues.Add($"Для символа '{mapping.Character}' не задан ни один код.");
                continue;
            }

            if (mapping.Codes.Any(code => code < 0x20))
            {
                issues.Add($"Коды символа '{mapping.Character}' должны находиться в диапазоне 0x20–0xFF.");
            }

            foreach (var code in mapping.Codes)
            {
                if (!codes.Add(code))
                {
                    issues.Add($"Код 0x{code:X2} назначен нескольким символам.");
                }
            }

            if (!mapping.Codes.Contains(mapping.PreferredCode))
            {
                issues.Add($"Предпочтительный код символа '{mapping.Character}' отсутствует в списке кодов.");
            }
        }

        return issues.Distinct(StringComparer.Ordinal).ToArray();
    }

    public static CharacterMapPreview Preview(
        CommonGXTManager manager,
        CharacterMapProfile profile,
        CharacterMapApplyMode applyMode)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(profile);

        var issues = Validate(profile).ToList();
        if (issues.Count > 0)
        {
            return new CharacterMapPreview(applyMode, 0, 0, issues);
        }

        var dictionary = profile.ToCharacterDictionary();
        var changedEntries = 0;
        var changedBytes = 0;
        var changes = new List<string>();
        if (applyMode == CharacterMapApplyMode.Interpret)
        {
            var decodeMap = CharacterMapCodec.CreateDecodeMap(dictionary);
            foreach (var entry in manager.GXTEntries)
            {
                var missingCodes = CharacterMapCodec.FindUnmappedExtendedCodes(entry.Value, decodeMap);
                foreach (var code in missingCodes)
                {
                    issues.Add($"Код 0x{code:X2} используется в GXT, но не назначен профилю.");
                }

                var current = manager.ConvertBytesToText(entry.Value);
                var interpreted = CharacterMapCodec.Decode(entry.Value, dictionary);
                if (!string.Equals(current, interpreted, StringComparison.Ordinal))
                {
                    changedEntries++;
                    changes.Add(
                        $"{entry.DatName.TrimEnd('\0')}: \"{Abbreviate(current)}\" → \"{Abbreviate(interpreted)}\"");
                }
            }
        }
        else
        {
            foreach (var entry in manager.GXTEntries)
            {
                var currentText = manager.ConvertBytesToText(entry.Value);
                try
                {
                    var encoded = CharacterMapCodec.Encode(currentText, dictionary);
                    var difference = CountDifferences(entry.Value, encoded);
                    if (difference > 0)
                    {
                        changedEntries++;
                        changedBytes += difference;
                        changes.Add(
                            $"{entry.DatName.TrimEnd('\0')}: изменено байтов {difference}; " +
                            $"{FormatBytes(entry.Value)} → {FormatBytes(encoded)}");
                    }
                }
                catch (InvalidDataException exception)
                {
                    issues.Add(exception.Message);
                }
            }
        }

        return new CharacterMapPreview(
            applyMode,
            changedEntries,
            changedBytes,
            issues.Distinct(StringComparer.Ordinal).ToArray())
        {
            Changes = changes,
        };
    }

    public static void Apply(
        CommonGXTManager manager,
        CharacterMapProfile profile,
        CharacterMapApplyMode applyMode)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(profile);

        var preview = Preview(manager, profile, applyMode);
        if (!preview.CanApply)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, preview.Issues));
        }

        var dictionary = profile.ToCharacterDictionary();
        List<byte[]>? encodedValues = null;
        if (applyMode == CharacterMapApplyMode.Reencode)
        {
            encodedValues = manager.GXTEntries
                .Select(entry => CharacterMapCodec.Encode(
                    manager.ConvertBytesToText(entry.Value),
                    dictionary))
                .ToList();
        }

        manager.CyrillicCharsDictionaryPath = null;
        manager.CyrillicCharsDictionary = dictionary;
        if (encodedValues is null)
        {
            return;
        }

        for (var index = 0; index < manager.GXTEntries.Count; index++)
        {
            manager.GXTEntries[index].Value = encodedValues[index];
        }
    }

    public static IReadOnlyList<string> AnalyzeTexts(
        CharacterMapProfile profile,
        IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(texts);
        var issues = Validate(profile).ToList();
        if (issues.Count > 0)
        {
            return issues;
        }

        var dictionary = profile.ToCharacterDictionary();
        var counts = CountCharacters(texts);

        var encodable = CharacterMapCodec.CreateEncodeMap(dictionary);
        var missing = counts
            .Where(pair => pair.Key > byte.MaxValue && !encodable.ContainsKey(pair.Key) ||
                           char.IsLetter(pair.Key) && pair.Key > 0x7F && !encodable.ContainsKey(pair.Key))
            .OrderBy(pair => pair.Key)
            .Select(pair => $"Нет символа '{pair.Key}' — используется {pair.Value} раз.");
        issues.AddRange(missing);
        return issues;
    }

    public static IReadOnlyDictionary<char, int> CountCharacters(IEnumerable<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var counts = new Dictionary<char, int>();
        foreach (var text in texts)
        {
            foreach (var character in CharacterMapCodec.EnumerateContentCharacters(text))
            {
                counts[character] = counts.GetValueOrDefault(character) + 1;
            }
        }

        return counts;
    }

    private static int CountDifferences(byte[] left, byte[] right)
    {
        var count = Math.Abs(left.Length - right.Length);
        var commonLength = Math.Min(left.Length, right.Length);
        for (var index = 0; index < commonLength; index++)
        {
            if (left[index] != right[index])
            {
                count++;
            }
        }

        return count;
    }

    private static string Abbreviate(string value) => value.Length <= 60 ? value : value[..57] + "…";

    private static string FormatBytes(byte[] value)
    {
        var displayed = value.Where((_, index) => index % 2 == 0).Take(12).ToArray();
        var suffix = value.Length > 24 ? "…" : string.Empty;
        return string.Join(' ', displayed.Select(item => item.ToString("X2", CultureInfo.InvariantCulture))) + suffix;
    }
}

internal static class CharacterMapCodec
{
    public static string Decode(
        byte[] inputBytes,
        IEnumerable<KeyValuePair<int[], char>> dictionary)
    {
        ArgumentNullException.ThrowIfNull(inputBytes);
        var decodeMap = CreateDecodeMap(dictionary);
        var asciiAliases = decodeMap.Keys.Where(code => code < 0x80).ToHashSet();
        var values = inputBytes
            .Where((_, index) => index % 2 == 0)
            .TakeWhile(value => value != 0)
            .ToList();
        var result = new System.Text.StringBuilder(values.Count);

        for (var index = 0; index < values.Count;)
        {
            if (values[index] == (byte)'~')
            {
                var tokenEnd = values.FindIndex(index + 1, value => value == (byte)'~');
                if (tokenEnd >= 0)
                {
                    for (; index <= tokenEnd; index++)
                    {
                        result.Append((char)values[index]);
                    }

                    continue;
                }
            }

            if (!IsWordByte(values[index], decodeMap))
            {
                result.Append(decodeMap.GetValueOrDefault(values[index], (char)values[index]));
                index++;
                continue;
            }

            var wordEnd = index + 1;
            while (wordEnd < values.Count && IsWordByte(values[wordEnd], decodeMap))
            {
                wordEnd++;
            }

            var hasExtendedMappedCode = values
                .Skip(index)
                .Take(wordEnd - index)
                .Any(value => value >= 0x80 && decodeMap.ContainsKey(value));
            var allAsciiLettersAreAliases = values
                .Skip(index)
                .Take(wordEnd - index)
                .Where(IsAsciiLetter)
                .All(asciiAliases.Contains);
            var localizedWord = hasExtendedMappedCode || allAsciiLettersAreAliases;
            while (index < wordEnd)
            {
                var value = values[index++];
                result.Append(localizedWord
                    ? decodeMap.GetValueOrDefault(value, (char)value)
                    : (char)value);
            }
        }

        return result.ToString();
    }

    public static byte[] Encode(
        string inputString,
        IEnumerable<KeyValuePair<int[], char>> dictionary)
    {
        ArgumentNullException.ThrowIfNull(inputString);
        var encodeMap = CreateEncodeMap(dictionary);
        var result = new byte[(inputString.Length + 1) * 2];
        for (var index = 0; index < inputString.Length;)
        {
            if (inputString[index] == '~')
            {
                var tokenEnd = inputString.IndexOf('~', index + 1);
                if (tokenEnd >= 0)
                {
                    for (; index <= tokenEnd; index++)
                    {
                        result[index * 2] = EncodeAscii(inputString[index]);
                    }

                    continue;
                }
            }

            if (!char.IsLetter(inputString[index]))
            {
                result[index * 2] = EncodeCharacter(inputString[index], encodeMap);
                index++;
                continue;
            }

            var wordEnd = index + 1;
            while (wordEnd < inputString.Length && char.IsLetter(inputString[wordEnd]))
            {
                wordEnd++;
            }

            var localizedWord = inputString
                .Skip(index)
                .Take(wordEnd - index)
                .Any(encodeMap.ContainsKey);
            while (index < wordEnd)
            {
                var character = inputString[index];
                result[index * 2] = localizedWord
                    ? EncodeCharacter(character, encodeMap)
                    : EncodeAscii(character);
                index++;
            }
        }

        return result;
    }

    public static Dictionary<byte, char> CreateDecodeMap(
        IEnumerable<KeyValuePair<int[], char>> dictionary) => dictionary
        .SelectMany(pair => pair.Key.Select(code => (Code: checked((byte)code), pair.Value)))
        .ToDictionary(pair => pair.Code, pair => pair.Value);

    public static Dictionary<char, byte> CreateEncodeMap(
        IEnumerable<KeyValuePair<int[], char>> dictionary) => dictionary
        .ToDictionary(pair => pair.Value, pair => checked((byte)pair.Key[0]));

    public static IReadOnlyList<byte> FindUnmappedExtendedCodes(
        byte[] inputBytes,
        IReadOnlyDictionary<byte, char> decodeMap) => inputBytes
        .Where((_, index) => index % 2 == 0)
        .TakeWhile(value => value != 0)
        .Where(value => value >= 0x80 && !decodeMap.ContainsKey(value))
        .Distinct()
        .Order()
        .ToArray();

    public static IEnumerable<char> EnumerateContentCharacters(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '~')
            {
                var tokenEnd = text.IndexOf('~', index + 1);
                if (tokenEnd >= 0)
                {
                    index = tokenEnd;
                    continue;
                }
            }

            yield return text[index];
        }
    }

    private static bool IsWordByte(byte value, Dictionary<byte, char> decodeMap) =>
        IsAsciiLetter(value) || decodeMap.TryGetValue(value, out var character) && char.IsLetter(character);

    private static bool IsAsciiLetter(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z';

    private static byte EncodeCharacter(char character, Dictionary<char, byte> encodeMap)
    {
        if (encodeMap.TryGetValue(character, out var mapped))
        {
            return mapped;
        }

        if (character == '\u2014')
        {
            return (byte)'-';
        }

        return EncodeAscii(character);
    }

    private static byte EncodeAscii(char character)
    {
        if (character > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"Символ '{character}' отсутствует в профиле маппинга.");
        }

        return (byte)character;
    }
}
