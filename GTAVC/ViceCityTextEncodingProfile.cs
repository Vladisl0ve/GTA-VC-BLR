using System.IO;
using System.Text;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.GTAVC;

internal sealed class ViceCityTextEncodingProfile
{
    private const byte FirstCustomCharacter = 0x80;
    private const byte LastCustomCharacter = 0xBF;

    private readonly IReadOnlyDictionary<byte, char> _cyrillicCharactersByByte;
    private readonly Dictionary<char, byte> _bytesByCyrillicCharacter;
    private readonly IReadOnlyDictionary<byte, char> _latinCharactersByByte;
    private readonly Dictionary<char, byte> _bytesByLatinCharacter;
    private readonly HashSet<byte> _cyrillicAsciiAliases;

    private ViceCityTextEncodingProfile(
        GxtLanguage language,
        string name,
        IEnumerable<(byte Code, char Character)> cyrillicCharacters,
        IEnumerable<(byte Code, char Character)>? latinCharacters = null,
        IEnumerable<(char Character, byte Code)>? encodingAliases = null)
    {
        Language = language;
        Name = name;
        _cyrillicCharactersByByte = cyrillicCharacters.ToDictionary(pair => pair.Code, pair => pair.Character);
        _cyrillicAsciiAliases = _cyrillicCharactersByByte.Keys
            .Where(code => code < FirstCustomCharacter)
            .ToHashSet();

        var bytesByCyrillicCharacter = new Dictionary<char, byte>();
        foreach (var (code, character) in cyrillicCharacters)
        {
            bytesByCyrillicCharacter.TryAdd(character, code);
        }

        if (encodingAliases is not null)
        {
            foreach (var (character, code) in encodingAliases)
            {
                bytesByCyrillicCharacter.TryAdd(character, code);
            }
        }

        _bytesByCyrillicCharacter = bytesByCyrillicCharacter;
        _latinCharactersByByte = (latinCharacters ?? [])
            .ToDictionary(pair => pair.Code, pair => pair.Character);
        _bytesByLatinCharacter = _latinCharactersByByte
            .GroupBy(pair => pair.Value)
            .ToDictionary(group => group.Key, group => group.First().Key);
    }

    public string Name { get; }

    public GxtLanguage Language { get; }

    public static ViceCityTextEncodingProfile Detect(
        string path,
        IEnumerable<GXTBase> entries,
        GxtLanguage language = GxtLanguage.Auto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entries);

        if (language != GxtLanguage.Auto)
        {
            return GetProfile(language);
        }

        Span<int> counts = stackalloc int[byte.MaxValue + 1];
        var characterCount = 0;
        var customCharacterCount = 0;

        foreach (var entry in entries)
        {
            for (var index = 0; index < entry.Value.Length; index += 2)
            {
                var value = entry.Value[index];
                if (value == 0)
                {
                    break;
                }

                counts[value]++;
                characterCount++;
                if (value is >= FirstCustomCharacter and <= LastCustomCharacter)
                {
                    customCharacterCount++;
                }
            }
        }

        if (characterCount == 0 || customCharacterCount * 100 < characterCount * 2)
        {
            return English;
        }

        var sequentialRangeCount = 0;
        var sequentialRangeDistinctCount = 0;
        for (var code = 0xB0; code <= LastCustomCharacter; code++)
        {
            if (counts[code] > 0)
            {
                sequentialRangeCount += counts[code];
                sequentialRangeDistinctCount++;
            }
        }

        var nameLanguage = GxtLanguageDetector.DetectFromName(path);
        if (sequentialRangeDistinctCount >= 8 &&
            sequentialRangeCount * 100 >= characterCount)
        {
            return BelarusianSequential;
        }

        // The current Belarusian patch uses ASCII I/i for І/і, 0x86/0x9D for Ў/ў
        // and the former Щ/щ slots 0x91/0xA8 for Т/т. The older project mapping
        // instead used ASCII T/y for Т/т and 0x91/0xA8 for Ў/ў.
        if (nameLanguage == GxtLanguage.Belarusian)
        {
            var currentBelarusianAliasCount = counts[(byte)'I'] + counts[(byte)'i'];
            var legacyBelarusianTCount = counts[(byte)'T'] + counts[(byte)'y'];
            return currentBelarusianAliasCount >= legacyBelarusianTCount
                ? Belarusian
                : BelarusianLegacy;
        }

        // Current TXD-compatible Belarusian: І/і are ASCII I/i, Т/т use
        // 0x91/0xA8, and ы remains at 0xA9.
        if ((counts[(byte)'I'] + counts[(byte)'i']) * 200 >= characterCount &&
            counts[0xA8] * 200 >= characterCount &&
            counts[0xA9] * 200 >= characterCount)
        {
            return Belarusian;
        }

        // Legacy Belarusian used ASCII T/y for Т/т and stored Ў/ў in 0x91/0xA8.
        // A relatively frequent 0xA8 together with T/y distinguishes that preset
        // from the current Belarusian mapping.
        if ((counts[(byte)'T'] + counts[(byte)'y']) * 200 >= characterCount &&
            counts[0xA8] >= 4 &&
            counts[0xA8] * 200 >= characterCount)
        {
            return BelarusianLegacy;
        }

        if (nameLanguage is GxtLanguage.Russian or GxtLanguage.Ukrainian)
        {
            return GetProfile(nameLanguage);
        }

        // Russian uses 0xA9 for 'ы' and ASCII 'y' for 'т'. Ukrainian instead
        // uses ASCII 'i'/'t' for 'і'/'т' and 0xAF for 'ї'.
        var russianScore = (counts[0xA9] * 4) + counts[(byte)'y'];
        var ukrainianScore = (counts[(byte)'i'] * 2) + (counts[(byte)'t'] * 2) + (counts[0xAF] * 3);
        if (ukrainianScore > russianScore)
        {
            return Ukrainian;
        }

        return Russian;
    }

    public static ViceCityTextEncodingProfile DetectForText(
        string? sourceName,
        IEnumerable<string> texts,
        GxtLanguage language = GxtLanguage.Auto)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var detectedLanguage = language == GxtLanguage.Auto
            ? GxtLanguageDetector.DetectForText(sourceName, texts)
            : language;
        return GetProfile(detectedLanguage);
    }

    private static ViceCityTextEncodingProfile GetProfile(GxtLanguage language) => language switch
    {
        GxtLanguage.English => English,
        GxtLanguage.Belarusian => Belarusian,
        GxtLanguage.Ukrainian => Ukrainian,
        _ => Russian,
    };

    public string Decode(byte[] inputBytes)
    {
        ArgumentNullException.ThrowIfNull(inputBytes);

        var values = new List<byte>((inputBytes.Length + 1) / 2);
        for (var index = 0; index < inputBytes.Length; index += 2)
        {
            if (inputBytes[index] == 0)
            {
                break;
            }

            values.Add(inputBytes[index]);
        }

        if (_cyrillicCharactersByByte.Count == 0)
        {
            return string.Create(values.Count, values, static (characters, source) =>
            {
                for (var index = 0; index < source.Count; index++)
                {
                    characters[index] = (char)source[index];
                }
            });
        }

        var result = new StringBuilder(values.Count);
        for (var index = 0; index < values.Count;)
        {
            if (!IsWordByte(values[index]))
            {
                result.Append((char)values[index++]);
                continue;
            }

            var wordEnd = index + 1;
            while (wordEnd < values.Count && IsWordByte(values[wordEnd]))
            {
                wordEnd++;
            }

            var isCyrillicWord = IsCyrillicWord(values, index, wordEnd);
            while (index < wordEnd)
            {
                var value = values[index++];
                var characters = isCyrillicWord ? _cyrillicCharactersByByte : _latinCharactersByByte;
                result.Append(characters.GetValueOrDefault(value, (char)value));
            }
        }

        return result.ToString();
    }

    public byte[] Encode(string inputString)
    {
        ArgumentNullException.ThrowIfNull(inputString);

        var result = new byte[(inputString.Length + 1) * 2];
        for (var index = 0; index < inputString.Length;)
        {
            if (!char.IsLetter(inputString[index]))
            {
                result[index * 2] = EncodeUnmappedCharacter(inputString[index]);
                index++;
                continue;
            }

            var wordEnd = index + 1;
            while (wordEnd < inputString.Length && char.IsLetter(inputString[wordEnd]))
            {
                wordEnd++;
            }

            var isCyrillicWord = false;
            for (var characterIndex = index; characterIndex < wordEnd; characterIndex++)
            {
                if (_bytesByCyrillicCharacter.ContainsKey(inputString[characterIndex]))
                {
                    isCyrillicWord = true;
                    break;
                }
            }

            while (index < wordEnd)
            {
                var character = inputString[index];
                var bytes = isCyrillicWord ? _bytesByCyrillicCharacter : _bytesByLatinCharacter;
                result[index * 2] = bytes.TryGetValue(character, out var value)
                    ? value
                    : EncodeUnmappedCharacter(character);
                index++;
            }
        }

        return result;
    }

    public Dictionary<int[], char> ToCharacterDictionary() =>
        _cyrillicCharactersByByte
            .GroupBy(pair => pair.Value)
            .ToDictionary(
                group => group.Select(pair => (int)pair.Key).ToArray(),
                group => group.Key);

    private bool IsWordByte(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' ||
        _cyrillicCharactersByByte.ContainsKey(value);

    private bool IsCyrillicWord(List<byte> values, int start, int end)
    {
        var allCharactersAreAliases = true;
        for (var index = start; index < end; index++)
        {
            var value = values[index];
            if (value >= FirstCustomCharacter && _cyrillicCharactersByByte.ContainsKey(value))
            {
                return true;
            }

            allCharactersAreAliases &= _cyrillicAsciiAliases.Contains(value);
        }

        return allCharactersAreAliases;
    }

    private static byte EncodeUnmappedCharacter(char character)
    {
        if (character == '\u2014')
        {
            return (byte)'-';
        }

        if (character > byte.MaxValue)
        {
            throw new InvalidDataException($"Символ '{character}' отсутствует в выбранной кодировке символов.");
        }

        return (byte)character;
    }

    private static ViceCityTextEncodingProfile FromCharacterMap(
        GxtLanguage language,
        string name,
        CharacterMapProfile profile,
        IEnumerable<(byte Code, char Character)>? latinCharacters = null) => new(
            language,
            name,
            profile.Mappings.SelectMany(mapping => mapping.Codes
                .OrderBy(code => code == mapping.PreferredCode ? 0 : 1)
                .ThenBy(code => code)
                .Select(code => (code, mapping.Character))),
            latinCharacters);

    private static CharacterMapProfile CreateLegacyBelarusianMap()
    {
        var profile = BundledCharacterMapProvider.BelarusianViceCity;

        SetCode('Т', (byte)'T');
        SetCode('т', (byte)'y');
        SetCode('І', 0x86);
        SetCode('Ў', 0x91);
        SetCode('і', 0x9D);
        SetCode('ў', 0xA8);
        return profile;

        void SetCode(char character, byte code)
        {
            var mapping = profile.Mappings.Single(item => item.Character == character);
            mapping.Codes.Clear();
            mapping.Codes.Add(code);
            mapping.PreferredCode = code;
        }
    }

    private static readonly ViceCityTextEncodingProfile English = new(
        GxtLanguage.English,
        "English",
        []);

    private static readonly ViceCityTextEncodingProfile Russian = new(
        GxtLanguage.Russian,
        "Русский",
        [
            ((byte)'A', 'А'), (0x80, 'Б'), (0x81, 'В'), ((byte)'B', 'В'), (0x82, 'Г'), (0x83, 'Д'),
            ((byte)'E', 'Е'), (0x84, 'Ж'), (0x85, 'З'), (0x86, 'И'), (0x87, 'Й'), ((byte)'K', 'К'),
            (0x88, 'Л'), (0x89, 'М'), ((byte)'M', 'М'), (0x8A, 'Н'), ((byte)'H', 'Н'), ((byte)'O', 'О'),
            (0x8B, 'П'), ((byte)'P', 'Р'), ((byte)'C', 'С'), ((byte)'T', 'Т'), (0x8C, 'У'), (0x8D, 'Ф'),
            ((byte)'X', 'Х'), (0x8E, 'Ц'), (0x8F, 'Ч'), (0x90, 'Ш'), (0x91, 'Щ'), (0x92, 'Ы'),
            (0x93, 'Ь'), (0x94, 'Э'), (0x95, 'Ю'), (0xAD, 'Я'),
            ((byte)'a', 'а'), (0x97, 'б'), (0x98, 'в'), (0x99, 'г'), (0x9A, 'д'), ((byte)'e', 'е'),
            (0x9B, 'ж'), (0x9C, 'з'), (0x9D, 'и'), (0x9E, 'й'), ((byte)'k', 'к'), (0x9F, 'л'),
            (0xA0, 'м'), (0xA1, 'н'), ((byte)'o', 'о'), (0xA2, 'п'), ((byte)'p', 'р'), ((byte)'c', 'с'),
            ((byte)'y', 'т'), (0xA3, 'у'), (0xA4, 'ф'), ((byte)'x', 'х'), (0xA5, 'ц'), (0xA6, 'ч'),
            (0xA7, 'ш'), (0xA8, 'щ'), (0xAF, 'ъ'), (0xA9, 'ы'), (0xAA, 'ь'), (0xAB, 'э'),
            (0xAC, 'ю'), (0xAE, 'я'),
        ],
        [((byte)'y', 't')],
        [('Ё', (byte)'E'), ('ё', (byte)'e')]);

    private static readonly ViceCityTextEncodingProfile Belarusian = FromCharacterMap(
        GxtLanguage.Belarusian,
        "Беларуская",
        BundledCharacterMapProvider.BelarusianViceCity);

    private static readonly ViceCityTextEncodingProfile BelarusianLegacy = FromCharacterMap(
        GxtLanguage.Belarusian,
        "Беларуская (legacy)",
        CreateLegacyBelarusianMap());

    private static readonly ViceCityTextEncodingProfile BelarusianSequential = new(
        GxtLanguage.Belarusian,
        "Беларуская (0x80–0xBF legacy)",
        ("АБВГДЕЁЖЗІЙКЛМНОПРСТУЎФХЦЧШЫЬЭЮЯ" +
         "абвгдеёжзійклмнопрстуўфхцчшыьэюя")
            .Select((character, index) =>
                (Code: checked((byte)(FirstCustomCharacter + index)), Character: character)));

    private static readonly ViceCityTextEncodingProfile Ukrainian = new(
        GxtLanguage.Ukrainian,
        "Українська",
        [
            ((byte)'A', 'А'), (0x80, 'Б'), (0x81, 'В'), ((byte)'B', 'В'), (0x82, 'Г'), (0x92, 'Ґ'),
            (0x83, 'Д'), ((byte)'E', 'Е'), (0x94, 'Є'), (0x84, 'Ж'), (0x85, 'З'), (0x86, 'И'),
            ((byte)'I', 'І'), (0x96, 'Ї'), (0x87, 'Й'), ((byte)'K', 'К'), (0x88, 'Л'), (0x89, 'М'),
            ((byte)'M', 'М'), (0x8A, 'Н'), ((byte)'H', 'Н'), ((byte)'O', 'О'), (0x8B, 'П'), ((byte)'P', 'Р'),
            ((byte)'C', 'С'), ((byte)'T', 'Т'), (0x8C, 'У'), (0x8D, 'Ф'), ((byte)'X', 'Х'), (0x8E, 'Ц'),
            (0x8F, 'Ч'), (0x90, 'Ш'), (0x91, 'Щ'), (0x93, 'Ь'), (0x95, 'Ю'), (0xAD, 'Я'),
            ((byte)'a', 'а'), (0x97, 'б'), (0x98, 'в'), (0x99, 'г'), (0xA9, 'ґ'), (0x9A, 'д'),
            ((byte)'e', 'е'), (0xAB, 'є'), (0x9B, 'ж'), (0x9C, 'з'), (0x9D, 'и'), ((byte)'i', 'і'),
            (0xAF, 'ї'), (0x9E, 'й'), ((byte)'k', 'к'), (0x9F, 'л'), (0xA0, 'м'), (0xA1, 'н'),
            ((byte)'o', 'о'), (0xA2, 'п'), ((byte)'p', 'р'), ((byte)'c', 'с'), ((byte)'t', 'т'),
            (0xA3, 'у'), (0xA4, 'ф'), ((byte)'x', 'х'), (0xA5, 'ц'), (0xA6, 'ч'), (0xA7, 'ш'),
            (0xA8, 'щ'), (0xAA, 'ь'), (0xAC, 'ю'), (0xAE, 'я'),
        ],
        [((byte)'y', 't')]);
}
