using System.IO;
using System.Text;
using GTA_GXT_Editor.Common;

namespace GTA_GXT_Editor.GTAVC;

internal sealed class ViceCityTextEncodingProfile
{
    private const byte FirstCustomCharacter = 0x80;
    private const byte LastCustomCharacter = 0xAF;

    private readonly IReadOnlyDictionary<byte, char> _cyrillicCharactersByByte;
    private readonly Dictionary<char, byte> _bytesByCyrillicCharacter;
    private readonly IReadOnlyDictionary<byte, char> _latinCharactersByByte;
    private readonly Dictionary<char, byte> _bytesByLatinCharacter;
    private readonly HashSet<byte> _cyrillicAsciiAliases;

    private ViceCityTextEncodingProfile(
        string name,
        IEnumerable<(byte Code, char Character)> cyrillicCharacters,
        IEnumerable<(byte Code, char Character)>? latinCharacters = null,
        IEnumerable<(char Character, byte Code)>? encodingAliases = null)
    {
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

    public static ViceCityTextEncodingProfile Detect(string path, IEnumerable<GXTBase> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entries);

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

        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.Contains("ukrain", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("ukr", StringComparison.OrdinalIgnoreCase))
        {
            return Ukrainian;
        }

        if (fileName.Contains("russian", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("rus", StringComparison.OrdinalIgnoreCase))
        {
            return Russian;
        }

        // Russian uses 0xA9 for 'ы' and ASCII 'y' for 'т'. Ukrainian instead
        // uses ASCII 'i'/'t' for 'і'/'т' and 0xAF for 'ї'. These markers also
        // let renamed localization files be recognized without relying on names.
        var russianScore = (counts[0xA9] * 4) + counts[(byte)'y'];
        var ukrainianScore = (counts[(byte)'i'] * 2) + (counts[(byte)'t'] * 2) + (counts[0xAF] * 3);
        return ukrainianScore > russianScore ? Ukrainian : Russian;
    }

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
        if (character > byte.MaxValue)
        {
            throw new InvalidDataException($"Символ '{character}' отсутствует в выбранной кодировке символов.");
        }

        return (byte)character;
    }

    private static readonly ViceCityTextEncodingProfile English = new("English", []);

    private static readonly ViceCityTextEncodingProfile Russian = new(
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

    private static readonly ViceCityTextEncodingProfile Ukrainian = new(
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
