using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class FontMetricsFileSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter(allowIntegerValues: false),
            new HexByteJsonConverter(),
        },
    };

    public static FontMetricsProfile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Deserialize(File.ReadAllBytes(path));
    }

    public static void Save(string path, FontMetricsProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllBytes(path, Serialize(profile));
    }

    public static byte[] Serialize(FontMetricsProfile profile)
    {
        ThrowIfInvalid(profile);
        var document = new FontMetricsDocument
        {
            Version = profile.Version,
            Font2 = new FontMetricsTableDocument { Advances = profile.Font2.Advances },
            Font1 = new FontMetricsTableDocument { Advances = profile.Font1.Advances },
            Overrides = profile.Overrides
                .OrderBy(item => item.Context)
                .ThenBy(item => item.Font)
                .ThenBy(item => item.Code)
                .Select(item => new FontMetricOverrideDocument
                {
                    Context = item.Context,
                    Font = item.Font,
                    Code = item.Code,
                    Advance = item.Advance,
                })
                .ToList(),
        };

        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }

    public static FontMetricsProfile Deserialize(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var document = JsonSerializer.Deserialize<FontMetricsDocument>(data, JsonOptions)
            ?? throw new InvalidDataException("Font metrics JSON must contain an object.");

        if (document.Font2?.Advances is null || document.Font1?.Advances is null)
        {
            throw new InvalidDataException("Font metrics JSON must contain font2 and font1 advance tables.");
        }

        var overrides = document.Overrides ?? [];
        if (overrides.Any(item => item is null))
        {
            throw new InvalidDataException("Font metrics JSON contains a missing override.");
        }

        var profile = new FontMetricsProfile
        {
            Version = document.Version,
            Font2 = new FontMetricsTable { Advances = document.Font2.Advances },
            Font1 = new FontMetricsTable { Advances = document.Font1.Advances },
            Overrides = overrides
                .Select(item => new FontMetricOverride
                {
                    Context = item!.Context,
                    Font = item.Font,
                    Code = item.Code,
                    Advance = item.Advance,
                })
                .ToList(),
        };

        ThrowIfInvalid(profile);
        return profile;
    }

    private static void ThrowIfInvalid(FontMetricsProfile profile)
    {
        var issues = FontMetricsValidator.Validate(profile);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, issues));
        }
    }

    private sealed class FontMetricsDocument
    {
        public int Version { get; init; }

        public FontMetricsTableDocument? Font2 { get; init; }

        public FontMetricsTableDocument? Font1 { get; init; }

        public List<FontMetricOverrideDocument>? Overrides { get; init; }
    }

    private sealed class FontMetricsTableDocument
    {
        public ushort[]? Advances { get; init; }
    }

    private sealed class FontMetricOverrideDocument
    {
        public FontRenderContext Context { get; init; }

        public FontTextureKind Font { get; init; }

        public byte Code { get; init; }

        public ushort Advance { get; init; }
    }

    private sealed class HexByteJsonConverter : JsonConverter<byte>
    {
        public override byte Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Font metric codes must be hexadecimal strings such as '0x91'.");
            }

            var value = reader.GetString();
            if (value?.Length != 4 ||
                !value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                !byte.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code))
            {
                throw new JsonException($"Font metric code '{value}' must be a two-digit hexadecimal byte such as '0x91'.");
            }

            return code;
        }

        public override void Write(Utf8JsonWriter writer, byte value, JsonSerializerOptions options) =>
            writer.WriteStringValue($"0x{value:X2}");
    }
}
