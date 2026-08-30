namespace GTA_GXT_Editor.Models;

public enum AsiRecognitionLevel
{
    Unknown,
    KnownFamily,
    VersionedMetadata,
    ExactKnownBinary,
}

public enum AsiDiagnosticSeverity
{
    Information,
    Warning,
    Error,
}

public sealed record AsiDiagnostic(
    string Code,
    AsiDiagnosticSeverity Severity,
    string Message);

public sealed record AsiPeSectionInfo(
    string Name,
    int VirtualAddress,
    int VirtualSize,
    int RawOffset,
    int RawSize,
    uint Characteristics);

public sealed record AsiPeInfo(
    ushort Machine,
    int EntryPointRva,
    ulong ImageBase,
    int SizeOfImage,
    IReadOnlyList<AsiPeSectionInfo> Sections);

public sealed record AsiBuildInfo(
    string Product,
    string? Version,
    string? BuildTag,
    string FileSha256);

public sealed record AsiPhysicalMetricPatch(
    FontTextureKind Font,
    int MetricIndex,
    ushort Advance);

public sealed record AsiExecutableTarget(
    uint TimeDateStamp,
    uint SizeOfImage,
    uint? FontMetricsRva,
    uint? FontStateRva,
    uint? HookRva);

public sealed record AsiMappingAssociation(
    string? ProfileId,
    string? ProfileSha256,
    bool IsResolved);

public sealed record AsiFontProfileReadResult(
    AsiRecognitionLevel RecognitionLevel,
    AsiPeInfo Pe,
    AsiBuildInfo? Build,
    FontMetricsProfile? FontMetrics,
    CharacterMapProfile? CharacterMap,
    AsiMappingAssociation Mapping,
    IReadOnlyList<AsiPhysicalMetricPatch> PhysicalPatches,
    IReadOnlyList<AsiExecutableTarget> ExecutableTargets,
    IReadOnlyList<AsiDiagnostic> Diagnostics)
{
    public bool HasUsableProfile => FontMetrics is not null || CharacterMap is not null;
}

public sealed class AsiFontProfileBinding
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public required Guid AsiAssetId { get; init; }

    public required string AsiSha256 { get; init; }

    public string? BaseCharacterMapFingerprint { get; init; }

    public string? BaseFontMetricsFingerprint { get; init; }

    public List<CharacterMapPatch> CharacterMapPatches { get; init; } = [];

    public List<FontAdvancePatch> FontAdvancePatches { get; init; } = [];

    public List<FontContextOverridePatch> FontContextOverridePatches { get; init; } = [];

    public AsiFontProfileBinding Clone() => new()
    {
        Version = Version,
        AsiAssetId = AsiAssetId,
        AsiSha256 = AsiSha256,
        BaseCharacterMapFingerprint = BaseCharacterMapFingerprint,
        BaseFontMetricsFingerprint = BaseFontMetricsFingerprint,
        CharacterMapPatches = CharacterMapPatches.Select(item => item.Clone()).ToList(),
        FontAdvancePatches = FontAdvancePatches.Select(item => item.Clone()).ToList(),
        FontContextOverridePatches = FontContextOverridePatches
            .Select(item => item.Clone())
            .ToList(),
    };
}

public sealed class CharacterMapPatch
{
    public required char Character { get; init; }

    public bool Remove { get; init; }

    public List<byte> Codes { get; init; } = [];

    public byte? PreferredCode { get; init; }

    public CharacterMapPatch Clone() => new()
    {
        Character = Character,
        Remove = Remove,
        Codes = Codes.ToList(),
        PreferredCode = PreferredCode,
    };
}

public sealed class FontAdvancePatch
{
    public required FontTextureKind Font { get; init; }

    public required int MetricIndex { get; init; }

    public required ushort Advance { get; init; }

    public FontAdvancePatch Clone() => new()
    {
        Font = Font,
        MetricIndex = MetricIndex,
        Advance = Advance,
    };
}

public sealed class FontContextOverridePatch
{
    public required FontRenderContext Context { get; init; }

    public required FontTextureKind Font { get; init; }

    public required byte Code { get; init; }

    // Null removes an override inherited from the ASI base profile.
    public ushort? Advance { get; init; }

    public FontContextOverridePatch Clone() => new()
    {
        Context = Context,
        Font = Font,
        Code = Code,
        Advance = Advance,
    };
}

public sealed class AsiFontProfileState
{
    public required Guid AsiAssetId { get; init; }

    public required string AsiSha256 { get; init; }

    public required AsiRecognitionLevel RecognitionLevel { get; init; }

    public AsiBuildInfo? Build { get; init; }

    public CharacterMapProfile? BaseCharacterMap { get; init; }

    public FontMetricsProfile? BaseFontMetrics { get; init; }

    public IReadOnlyList<AsiDiagnostic> Diagnostics { get; init; } = [];

    public IReadOnlyList<AsiPhysicalMetricPatch> PhysicalPatches { get; init; } = [];

    public IReadOnlyList<AsiExecutableTarget> ExecutableTargets { get; init; } = [];

    public AsiFontProfileState Clone() => new()
    {
        AsiAssetId = AsiAssetId,
        AsiSha256 = AsiSha256,
        RecognitionLevel = RecognitionLevel,
        Build = Build,
        BaseCharacterMap = BaseCharacterMap?.Clone(),
        BaseFontMetrics = BaseFontMetrics?.Clone(),
        Diagnostics = Diagnostics.ToArray(),
        PhysicalPatches = PhysicalPatches.ToArray(),
        ExecutableTargets = ExecutableTargets.ToArray(),
    };
}
