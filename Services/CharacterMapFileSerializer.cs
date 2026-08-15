using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.Services;

public static class CharacterMapFileSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static CharacterMapProfile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            return CharacterMapProfile.FromDictionary(path.LoadCyrillicCharsDictionary());
        }

        return Deserialize(File.ReadAllBytes(path));
    }

    public static void Save(string path, CharacterMapProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, Serialize(profile));
    }

    public static byte[] Serialize(CharacterMapProfile profile)
    {
        var issues = CharacterMapService.Validate(profile);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, issues));
        }

        var document = new CharacterMapDocument
        {
            Version = CharacterMapProfile.CurrentVersion,
            Mappings = profile.Mappings
                .OrderBy(mapping => mapping.PreferredCode)
                .Select(mapping => new CharacterMapDocumentEntry
                {
                    Character = mapping.Character.ToString(),
                    Codes = mapping.Codes.Select(FormatCode).ToList(),
                    PreferredCode = FormatCode(mapping.PreferredCode),
                }).ToList(),
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }

    public static CharacterMapProfile Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var json = JsonDocument.Parse(data);
        if (json.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("JSON маппинга должен быть объектом.");
        }

        CharacterMapProfile profile;
        if (json.RootElement.TryGetProperty("mappings", out _))
        {
            var document = JsonSerializer.Deserialize<CharacterMapDocument>(data, JsonOptions)
                ?? throw new InvalidDataException("JSON маппинга повреждён.");
            profile = new CharacterMapProfile
            {
                Version = document.Version,
                Mappings = document.Mappings.Select(ParseEntry).ToList(),
            };
        }
        else
        {
            var mappings = new List<CharacterMapEntry>();
            foreach (var property in json.RootElement.EnumerateObject())
            {
                if (property.Name.Length != 1 || char.IsSurrogate(property.Name[0]) ||
                    property.Value.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException(
                        "Сокращённый JSON должен иметь формат { \"символ\": \"0x80\" }.");
                }

                var code = ParseCode(property.Value.GetString());
                mappings.Add(new CharacterMapEntry
                {
                    Character = property.Name[0],
                    Codes = [code],
                    PreferredCode = code,
                });
            }

            profile = new CharacterMapProfile { Mappings = mappings };
        }

        var issues = CharacterMapService.Validate(profile);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, issues));
        }

        return profile;
    }

    private static CharacterMapEntry ParseEntry(CharacterMapDocumentEntry entry)
    {
        if (entry.Character.Length != 1 || char.IsSurrogate(entry.Character[0]) ||
            entry.Codes.Count == 0)
        {
            throw new InvalidDataException("Запись JSON маппинга повреждена.");
        }

        return new CharacterMapEntry
        {
            Character = entry.Character[0],
            Codes = entry.Codes.Select(ParseCode).ToList(),
            PreferredCode = ParseCode(entry.PreferredCode),
        };
    }

    private static string FormatCode(byte code) => $"0x{code:X2}";

    private static byte ParseCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("Код символа в JSON не задан.");
        }

        var text = value.Trim();
        var style = NumberStyles.Integer;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
            style = NumberStyles.HexNumber;
        }

        if (!byte.TryParse(text, style, CultureInfo.InvariantCulture, out var code) || code < 0x20)
        {
            throw new InvalidDataException($"Код '{value}' должен находиться в диапазоне 0x20–0xFF.");
        }

        return code;
    }

    private sealed class CharacterMapDocument
    {
        public int Version { get; init; }

        public List<CharacterMapDocumentEntry> Mappings { get; init; } = [];
    }

    private sealed class CharacterMapDocumentEntry
    {
        public string Character { get; init; } = string.Empty;

        public List<string> Codes { get; init; } = [];

        public string PreferredCode { get; init; } = string.Empty;
    }
}
