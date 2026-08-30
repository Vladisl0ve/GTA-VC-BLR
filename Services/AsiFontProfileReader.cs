using System.Buffers.Binary;
using System.IO;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public interface IAsiFontProfileReader
{
    AsiFontProfileReadResult Read(ReadOnlyMemory<byte> data, string sourceName);
}

public sealed class AsiFontProfileReader : IAsiFontProfileReader
{
    public const int MaximumAsiSize = 64 * 1024 * 1024;
    public const int MaximumMetadataSize = 1024 * 1024;

    private const int MetricPayloadSize = 2 * FontMetricsTable.MetricCount * sizeof(ushort);
    private const int CanonicalMetricOffset = 0x485E;
    private const int CanonicalSparseOffset = 0x6470;
    private const int CanonicalSparseCount = 63;
    private const string CanonicalFileSha256 =
        "17D98CC31D63067EB9040A44453D2EB09C983ED19C2AFAB276A227B2FBA523A7";
    private const string CanonicalMetricSha256 =
        "50E8D5CDC3C7904875FC014CB343BDEEC0D8C9CF57C234F4B5AB53B60DABC1EA";
    private const string CanonicalSparseSha256 =
        "76042A424B19D4DAA528286C85BD0239A432863B319BC46BC5C7DC3B2529EE34";
    private const string MetadataFormat = "gta-vc-blr-asi-metadata";
    private const string CanonicalMappingProfileId = "belarusian-vc-current";

    private static readonly byte[] BuildMarker = "BelarusianLanguage "u8.ToArray();
    private static readonly byte[] MetadataMarker = "GTA_VC_BLR_ASI_META\0"u8.ToArray();
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public AsiFontProfileReadResult Read(ReadOnlyMemory<byte> data, string sourceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        if (data.Length is < 64 or > MaximumAsiSize)
        {
            throw new InvalidDataException(
                $"ASI '{sourceName}' must be between 64 bytes and {MaximumAsiSize} bytes.");
        }

        var bytes = data.ToArray();
        var pe = ParsePe(bytes, sourceName, out var sections);
        var fileSha256 = Convert.ToHexString(SHA256.HashData(bytes));
        var markerMatches = FindMatches(bytes, sections, BuildMarker);
        var build = markerMatches.Count == 1
            ? ParseBuildInfo(bytes, markerMatches[0], fileSha256)
            : null;

        if (string.Equals(fileSha256, CanonicalFileSha256, StringComparison.Ordinal))
        {
            return ReadCanonical(bytes, pe, build, fileSha256);
        }

        var metadataMatches = FindMatches(bytes, sections, MetadataMarker);
        if (metadataMatches.Count > 1)
        {
            throw new InvalidDataException("The ASI contains more than one metadata block.");
        }

        if (metadataMatches.Count == 1)
        {
            return ReadMetadata(bytes, pe, sections, build, fileSha256, metadataMatches[0]);
        }

        if (markerMatches.Count == 1 &&
            TryReadStructuralMetrics(bytes, markerMatches[0], out var structuralMetrics))
        {
            return new AsiFontProfileReadResult(
                AsiRecognitionLevel.KnownFamily,
                pe,
                build,
                structuralMetrics,
                null,
                new AsiMappingAssociation(null, null, false),
                [],
                [],
                [
                    new AsiDiagnostic(
                        "KnownFamilyStructuralMatch",
                        AsiDiagnosticSeverity.Warning,
                        "Base font rows were read structurally; runtime contexts and character mapping are unknown."),
                    new AsiDiagnostic(
                        "ExternalMappingRequired",
                        AsiDiagnosticSeverity.Warning,
                        "This ASI does not prove a Unicode character mapping."),
                ]);
        }

        var diagnostics = new List<AsiDiagnostic>();
        if (markerMatches.Count > 1)
        {
            diagnostics.Add(new AsiDiagnostic(
                "AmbiguousBuildMarker",
                AsiDiagnosticSeverity.Warning,
                "The ASI contains multiple BelarusianLanguage build markers."));
        }
        else
        {
            diagnostics.Add(new AsiDiagnostic(
                "UnknownAsiBuild",
                AsiDiagnosticSeverity.Warning,
                "The ASI is a valid x86 PE DLL but its font profile is not recognized."));
        }

        return new AsiFontProfileReadResult(
            AsiRecognitionLevel.Unknown,
            pe,
            build,
            null,
            null,
            new AsiMappingAssociation(null, null, false),
            [],
            [],
            diagnostics);
    }

    private static AsiFontProfileReadResult ReadCanonical(
        byte[] bytes,
        AsiPeInfo pe,
        AsiBuildInfo? build,
        string fileSha256)
    {
        if (bytes.Length != 26112 || build?.Product != "BelarusianLanguage" || build.Version != "1.2.34")
        {
            throw new InvalidDataException("The canonical ASI fingerprint conflicts with its PE/build identity.");
        }

        var metricPayload = SliceChecked(bytes, CanonicalMetricOffset, MetricPayloadSize, "metric payload");
        if (!HashMatches(metricPayload, CanonicalMetricSha256))
        {
            throw new InvalidDataException("The canonical ASI metric payload hash is invalid.");
        }

        var sparsePayload = SliceChecked(
            bytes,
            CanonicalSparseOffset,
            CanonicalSparseCount * 4,
            "sparse metric table");
        if (!HashMatches(sparsePayload, CanonicalSparseSha256))
        {
            throw new InvalidDataException("The canonical ASI sparse metric table hash is invalid.");
        }

        var preset = FontMetricsPresets.BelarusianViceCity;
        var metrics = DecodeMetrics(metricPayload, preset.Overrides);
        var mapping = CharacterMapPresets.Belarusian;
        mapping.IsVerified = true;
        var physicalPatches = new List<AsiPhysicalMetricPatch>(CanonicalSparseCount);
        for (var index = 0; index < CanonicalSparseCount; index++)
        {
            var offset = index * 4;
            var metricIndex = BinaryPrimitives.ReadUInt16LittleEndian(sparsePayload.Slice(offset, 2));
            var advance = BinaryPrimitives.ReadUInt16LittleEndian(sparsePayload.Slice(offset + 2, 2));
            if (metricIndex >= FontMetricsTable.MetricCount || advance > FontMetricsValidator.MaximumAdvance)
            {
                throw new InvalidDataException("The canonical ASI sparse metric table contains an invalid pair.");
            }

            physicalPatches.Add(new AsiPhysicalMetricPatch(
                FontTextureKind.Font1,
                metricIndex,
                advance));
        }

        var mappingHash = FontProfileFingerprint.Compute(mapping);
        return new AsiFontProfileReadResult(
            AsiRecognitionLevel.ExactKnownBinary,
            pe,
            build,
            metrics,
            mapping,
            new AsiMappingAssociation(CanonicalMappingProfileId, mappingHash, true),
            physicalPatches,
            [
                new(0x504D6947, 0x614000, 0x296BD8, 0x57F820, 0x14FFE0),
                new(0x48982736, 0x696000, 0x295BE0, 0x57E828, 0x14FED0),
            ],
            [
                new AsiDiagnostic(
                    "ExactKnownBuild",
                    AsiDiagnosticSeverity.Information,
                    $"Recognized canonical BelarusianLanguage 1.2.34 ({fileSha256})."),
            ]);
    }

    private static AsiFontProfileReadResult ReadMetadata(
        byte[] bytes,
        AsiPeInfo pe,
        IReadOnlyList<SectionRange> sections,
        AsiBuildInfo? build,
        string fileSha256,
        MarkerMatch metadataMatch)
    {
        var lengthOffset = metadataMatch.End;
        if (lengthOffset > metadataMatch.Section.End - sizeof(uint))
        {
            throw new InvalidDataException("The ASI metadata length is outside its PE section.");
        }

        var jsonLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(lengthOffset, sizeof(uint)));
        if (jsonLength is 0 or > MaximumMetadataSize ||
            jsonLength > metadataMatch.Section.End - (lengthOffset + sizeof(uint)))
        {
            throw new InvalidDataException("The ASI metadata JSON length is invalid.");
        }

        var jsonOffset = lengthOffset + sizeof(uint);
        string json;
        try
        {
            json = StrictUtf8.GetString(bytes, jsonOffset, checked((int)jsonLength));
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("The ASI metadata JSON is not valid UTF-8.", exception);
        }

        AsiMetadataDocument document;
        try
        {
            document = JsonSerializer.Deserialize<AsiMetadataDocument>(json, MetadataJsonOptions)
                ?? throw new InvalidDataException("The ASI metadata JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The ASI metadata JSON is invalid.", exception);
        }

        ValidateMetadataDocument(document);
        var payloadHash = NormalizeHash(document.FontMetrics!.PayloadSha256);
        var payloadMatches = FindMetricPayloadByHash(bytes, sections, payloadHash);
        if (payloadMatches.Count != 1)
        {
            throw new InvalidDataException(
                payloadMatches.Count == 0
                    ? "The ASI metadata metric payload was not found."
                    : "The ASI metadata metric payload is ambiguous.");
        }

        var contextOverrides = document.Contexts.Select(ParseContextOverride).ToList();
        var metrics = DecodeMetrics(
            bytes.AsSpan(payloadMatches[0], MetricPayloadSize),
            contextOverrides);
        CharacterMapProfile? mapping = null;
        var mappingResolved = false;
        string? mappingHash = null;
        if (document.Mapping is not null)
        {
            mappingHash = NormalizeHash(document.Mapping.ProfileSha256);
            var preset = CharacterMapPresets.Belarusian;
            var presetHash = FontProfileFingerprint.Compute(preset);
            if (string.Equals(document.Mapping.ProfileId, CanonicalMappingProfileId, StringComparison.Ordinal) &&
                string.Equals(mappingHash, presetHash, StringComparison.Ordinal))
            {
                preset.IsVerified = true;
                mapping = preset;
                mappingResolved = true;
            }
        }

        var diagnostics = new List<AsiDiagnostic>
        {
            new(
                "VersionedMetadata",
                AsiDiagnosticSeverity.Information,
                $"Read ASI metadata schema {document.Version}."),
        };
        if (document.Mapping is not null && !mappingResolved)
        {
            diagnostics.Add(new AsiDiagnostic(
                "ExternalMappingRequired",
                AsiDiagnosticSeverity.Warning,
                "The ASI mapping association does not match a bundled profile."));
        }

        var targets = document.RuntimeTargets.Select(ParseRuntimeTarget).ToArray();
        foreach (var target in targets)
        {
            ValidateRuntimeTarget(target, pe);
        }
        var metadataBuild = build ?? new AsiBuildInfo(
            document.Product,
            document.PluginVersion,
            null,
            fileSha256);
        return new AsiFontProfileReadResult(
            AsiRecognitionLevel.VersionedMetadata,
            pe,
            metadataBuild,
            metrics,
            mapping,
            new AsiMappingAssociation(
                document.Mapping?.ProfileId,
                mappingHash,
                mappingResolved),
            [],
            targets,
            diagnostics);
    }

    private static AsiPeInfo ParsePe(
        byte[] bytes,
        string sourceName,
        out IReadOnlyList<SectionRange> sectionRanges)
    {
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
            var headers = reader.PEHeaders;
            var header = headers.PEHeader
                ?? throw new InvalidDataException($"ASI '{sourceName}' has no PE optional header.");
            if (header.Magic != PEMagic.PE32 || headers.CoffHeader.Machine != Machine.I386 ||
                (headers.CoffHeader.Characteristics & Characteristics.Dll) == 0)
            {
                throw new InvalidDataException($"ASI '{sourceName}' must be an x86 PE32 DLL.");
            }

            var sections = new List<AsiPeSectionInfo>(headers.SectionHeaders.Length);
            var ranges = new List<SectionRange>(headers.SectionHeaders.Length);
            foreach (var section in headers.SectionHeaders)
            {
                if (section.PointerToRawData < 0 || section.SizeOfRawData < 0 ||
                    section.PointerToRawData > bytes.Length - section.SizeOfRawData)
                {
                    throw new InvalidDataException($"ASI '{sourceName}' contains an out-of-bounds PE section.");
                }

                var info = new AsiPeSectionInfo(
                    section.Name,
                    section.VirtualAddress,
                    section.VirtualSize,
                    section.PointerToRawData,
                    section.SizeOfRawData,
                    (uint)section.SectionCharacteristics);
                sections.Add(info);
                if (section.SizeOfRawData > 0)
                {
                    ranges.Add(new SectionRange(
                        info,
                        section.PointerToRawData,
                        checked(section.PointerToRawData + section.SizeOfRawData)));
                }
            }

            var ordered = ranges.OrderBy(item => item.Start).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                if (ordered[index].Start < ordered[index - 1].End)
                {
                    throw new InvalidDataException($"ASI '{sourceName}' contains overlapping PE sections.");
                }
            }

            sectionRanges = ranges;
            return new AsiPeInfo(
                (ushort)headers.CoffHeader.Machine,
                header.AddressOfEntryPoint,
                header.ImageBase,
                header.SizeOfImage,
                sections);
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException($"ASI '{sourceName}' is not a valid PE image.", exception);
        }
    }

    private static List<MarkerMatch> FindMatches(
        byte[] bytes,
        IReadOnlyList<SectionRange> sections,
        ReadOnlySpan<byte> marker)
    {
        var result = new List<MarkerMatch>();
        foreach (var section in sections)
        {
            var searchOffset = section.Start;
            while (searchOffset <= section.End - marker.Length)
            {
                var relative = bytes.AsSpan(searchOffset, section.End - searchOffset).IndexOf(marker);
                if (relative < 0)
                {
                    break;
                }

                var offset = searchOffset + relative;
                result.Add(new MarkerMatch(offset, checked(offset + marker.Length), section));
                searchOffset = offset + 1;
            }
        }

        return result;
    }

    private static AsiBuildInfo? ParseBuildInfo(
        byte[] bytes,
        MarkerMatch marker,
        string fileSha256)
    {
        var nul = Array.IndexOf(
            bytes,
            (byte)0,
            marker.End,
            Math.Min(256, marker.Section.End - marker.End));
        if (nul < 0)
        {
            return null;
        }

        var text = Encoding.ASCII.GetString(bytes, marker.Offset, nul - marker.Offset).Trim();
        if (!text.StartsWith("BelarusianLanguage ", StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = text["BelarusianLanguage ".Length..];
        var bracket = remainder.IndexOf(" [", StringComparison.Ordinal);
        var version = bracket < 0 ? remainder.Trim() : remainder[..bracket].Trim();
        var tag = bracket < 0 ? null : remainder[(bracket + 1)..].Trim();
        return string.IsNullOrWhiteSpace(version)
            ? null
            : new AsiBuildInfo("BelarusianLanguage", version, tag, fileSha256);
    }

    private static bool TryReadStructuralMetrics(
        byte[] bytes,
        MarkerMatch marker,
        out FontMetricsProfile metrics)
    {
        metrics = null!;
        var nul = Array.IndexOf(
            bytes,
            (byte)0,
            marker.End,
            Math.Min(256, marker.Section.End - marker.End));
        if (nul < 0 || nul + 1 > marker.Section.End - MetricPayloadSize)
        {
            return false;
        }

        var payload = bytes.AsSpan(nul + 1, MetricPayloadSize);
        if (!IsPlausibleMetricPayload(payload))
        {
            return false;
        }

        metrics = DecodeMetrics(payload, []);
        return true;
    }

    private static List<int> FindMetricPayloadByHash(
        byte[] bytes,
        IReadOnlyList<SectionRange> sections,
        string expectedHash)
    {
        var result = new List<int>();
        foreach (var section in sections)
        {
            for (var offset = section.Start; offset <= section.End - MetricPayloadSize; offset += 2)
            {
                var candidate = bytes.AsSpan(offset, MetricPayloadSize);
                if (!IsPlausibleMetricPayload(candidate) || !HashMatches(candidate, expectedHash))
                {
                    continue;
                }

                result.Add(offset);
                if (result.Count > 1)
                {
                    return result;
                }
            }
        }

        return result;
    }

    private static bool IsPlausibleMetricPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != MetricPayloadSize)
        {
            return false;
        }

        for (var offset = 0; offset < payload.Length; offset += sizeof(ushort))
        {
            if (BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset, sizeof(ushort))) >
                FontMetricsValidator.MaximumAdvance)
            {
                return false;
            }
        }

        return true;
    }

    private static FontMetricsProfile DecodeMetrics(
        ReadOnlySpan<byte> payload,
        IEnumerable<FontMetricOverride> overrides)
    {
        if (payload.Length != MetricPayloadSize)
        {
            throw new InvalidDataException("An ASI metric payload must contain exactly 840 bytes.");
        }

        var values = new ushort[2 * FontMetricsTable.MetricCount];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = BinaryPrimitives.ReadUInt16LittleEndian(
                payload.Slice(index * sizeof(ushort), sizeof(ushort)));
        }

        var profile = new FontMetricsProfile
        {
            Font2 = new FontMetricsTable
            {
                Advances = values[..FontMetricsTable.MetricCount],
            },
            Font1 = new FontMetricsTable
            {
                Advances = values[FontMetricsTable.MetricCount..],
            },
            Overrides = overrides.Select(item => item.Clone()).ToList(),
        };
        var issues = FontMetricsValidator.Validate(profile);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, issues));
        }

        return profile;
    }

    private static void ValidateMetadataDocument(AsiMetadataDocument document)
    {
        if (!string.Equals(document.Format, MetadataFormat, StringComparison.Ordinal) ||
            document.Version != 1 ||
            !string.Equals(document.Product, "BelarusianLanguage", StringComparison.Ordinal) ||
            document.FontMetrics is null ||
            document.FontMetrics.MetricCount != FontMetricsTable.MetricCount ||
            !string.Equals(document.FontMetrics.CodeBase, "0x20", StringComparison.OrdinalIgnoreCase) ||
            document.FontMetrics.RowOrder.Count != 2 ||
            !string.Equals(document.FontMetrics.RowOrder[0], "font2", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(document.FontMetrics.RowOrder[1], "font1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The ASI metadata schema or font row contract is unsupported.");
        }

        _ = NormalizeHash(document.FontMetrics.PayloadSha256);
        if (document.Mapping is not null)
        {
            if (string.IsNullOrWhiteSpace(document.Mapping.ProfileId))
            {
                throw new InvalidDataException("The ASI metadata mapping profile id is missing.");
            }

            _ = NormalizeHash(document.Mapping.ProfileSha256);
        }
    }

    private static FontMetricOverride ParseContextOverride(AsiMetadataContext item)
    {
        if (!Enum.TryParse<FontRenderContext>(item.Context, ignoreCase: true, out var context) ||
            !Enum.TryParse<FontTextureKind>(item.Font, ignoreCase: true, out var font))
        {
            throw new InvalidDataException("The ASI metadata contains an unknown font context or row.");
        }

        var code = ParseByteCode(item.Code);
        return new FontMetricOverride
        {
            Context = context,
            Font = font,
            Code = code,
            Advance = item.Advance,
        };
    }

    private static AsiExecutableTarget ParseRuntimeTarget(AsiMetadataRuntimeTarget item) => new(
        ParseUInt32(item.TimeDateStamp),
        ParseUInt32(item.SizeOfImage),
        ParseOptionalUInt32(item.FontMetricsRva),
        ParseOptionalUInt32(item.FontStateRva),
        ParseOptionalUInt32(item.HookRva));

    private static void ValidateRuntimeTarget(AsiExecutableTarget target, AsiPeInfo pe)
    {
        if (target.TimeDateStamp == 0 || target.SizeOfImage == 0 ||
            EnumerateRvas(target).Any(rva => rva == 0 || rva >= target.SizeOfImage))
        {
            throw new InvalidDataException(
                "The ASI metadata contains an invalid executable target or RVA.");
        }

        // The target describes gta-vc.exe rather than the plugin itself. The plugin PE
        // image is still parsed independently; keep this use explicit to avoid ever
        // treating target RVAs as offsets in the ASI file.
        _ = pe.SizeOfImage;
    }

    private static IEnumerable<uint> EnumerateRvas(AsiExecutableTarget target)
    {
        if (target.FontMetricsRva is { } metrics)
        {
            yield return metrics;
        }

        if (target.FontStateRva is { } state)
        {
            yield return state;
        }

        if (target.HookRva is { } hook)
        {
            yield return hook;
        }
    }

    private static byte ParseByteCode(string value)
    {
        var parsed = ParseUInt32(value);
        if (parsed > byte.MaxValue)
        {
            throw new InvalidDataException($"ASI metadata code '{value}' is outside the byte range.");
        }

        return (byte)parsed;
    }

    private static uint? ParseOptionalUInt32(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseUInt32(value);

    private static uint ParseUInt32(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("ASI metadata contains an empty numeric value.");
        }

        var text = value.Trim();
        var style = System.Globalization.NumberStyles.Integer;
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            text = text[2..];
            style = System.Globalization.NumberStyles.HexNumber;
        }

        if (!uint.TryParse(text, style, System.Globalization.CultureInfo.InvariantCulture, out var result))
        {
            throw new InvalidDataException($"ASI metadata numeric value '{value}' is invalid.");
        }

        return result;
    }

    private static string NormalizeHash(string value)
    {
        var result = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (result.Length != 64 || !result.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("ASI metadata contains an invalid SHA-256 value.");
        }

        return result;
    }

    private static ReadOnlySpan<byte> SliceChecked(byte[] bytes, int offset, int length, string name)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new InvalidDataException($"The ASI {name} lies outside the file.");
        }

        return bytes.AsSpan(offset, length);
    }

    private static bool HashMatches(ReadOnlySpan<byte> data, string expected) =>
        Convert.ToHexString(SHA256.HashData(data)).Equals(expected, StringComparison.Ordinal);

    private sealed record SectionRange(AsiPeSectionInfo Info, int Start, int End);

    private sealed record MarkerMatch(int Offset, int End, SectionRange Section);

    private sealed class AsiMetadataDocument
    {
        public string Format { get; init; } = string.Empty;
        public int Version { get; init; }
        public string Product { get; init; } = string.Empty;
        public string? PluginVersion { get; init; }
        public AsiMetadataFontMetrics? FontMetrics { get; init; }
        public AsiMetadataMapping? Mapping { get; init; }
        public List<AsiMetadataContext> Contexts { get; init; } = [];
        public List<AsiMetadataRuntimeTarget> RuntimeTargets { get; init; } = [];
    }

    private sealed class AsiMetadataFontMetrics
    {
        public List<string> RowOrder { get; init; } = [];
        public int MetricCount { get; init; }
        public string CodeBase { get; init; } = string.Empty;
        public string PayloadSha256 { get; init; } = string.Empty;
    }

    private sealed class AsiMetadataMapping
    {
        public string ProfileId { get; init; } = string.Empty;
        public string ProfileSha256 { get; init; } = string.Empty;
    }

    private sealed class AsiMetadataContext
    {
        public string Context { get; init; } = string.Empty;
        public string Font { get; init; } = string.Empty;
        public string Code { get; init; } = string.Empty;
        public ushort Advance { get; init; }
    }

    private sealed class AsiMetadataRuntimeTarget
    {
        public string TimeDateStamp { get; init; } = string.Empty;
        public string SizeOfImage { get; init; } = string.Empty;
        public string? FontMetricsRva { get; init; }
        public string? FontStateRva { get; init; }
        public string? HookRva { get; init; }
    }
}
