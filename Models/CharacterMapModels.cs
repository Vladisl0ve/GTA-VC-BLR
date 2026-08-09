using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Services;

namespace GTA_GXT_Editor.Models;

public sealed class CharacterMapProfile
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public bool IsVerified { get; set; }

    public List<CharacterMapEntry> Mappings { get; init; } = [];

    public CharacterMapProfile Clone() => new()
    {
        Version = Version,
        IsVerified = IsVerified,
        Mappings = Mappings.Select(mapping => mapping.Clone()).ToList(),
    };

    public Dictionary<int[], char> ToCharacterDictionary() => Mappings.ToDictionary(
        mapping => mapping.Codes
            .OrderBy(code => code == mapping.PreferredCode ? 0 : 1)
            .ThenBy(code => code)
            .Select(code => (int)code)
            .ToArray(),
        mapping => mapping.Character);

    public IReadOnlyDictionary<byte, char> ToDecodeMap() => Mappings
        .SelectMany(mapping => mapping.Codes.Select(code => (Code: code, mapping.Character)))
        .ToDictionary(pair => pair.Code, pair => pair.Character);

    public IReadOnlyDictionary<char, byte> ToEncodeMap() => Mappings.ToDictionary(
        mapping => mapping.Character,
        mapping => mapping.PreferredCode);

    public static CharacterMapProfile FromDictionary(
        IEnumerable<KeyValuePair<int[], char>> dictionary,
        bool isVerified = false)
    {
        ArgumentNullException.ThrowIfNull(dictionary);

        return new CharacterMapProfile
        {
            IsVerified = isVerified,
            Mappings = dictionary.Select(pair =>
            {
                var codes = pair.Key
                    .Select(code => checked((byte)code))
                    .Distinct()
                    .ToList();
                return new CharacterMapEntry
                {
                    Character = pair.Value,
                    Codes = codes,
                    PreferredCode = codes[0],
                };
            }).ToList(),
        };
    }
}

public sealed class CharacterMapEntry
{
    public required char Character { get; init; }

    public required List<byte> Codes { get; init; }

    public required byte PreferredCode { get; set; }

    public CharacterMapEntry Clone() => new()
    {
        Character = Character,
        Codes = Codes.ToList(),
        PreferredCode = PreferredCode,
    };
}

public enum CharacterMapApplyMode
{
    Interpret,
    Reencode,
}

public sealed record CharacterMapEditorRequest(
    TxdAttachment Attachment,
    GXTType GameType,
    CharacterMapProfile Profile,
    IReadOnlyList<string> CurrentTexts,
    IReadOnlyList<byte[]> RawValues,
    GxtLanguage Language);

public sealed record CharacterMapEditorResult(
    CharacterMapProfile Profile,
    CharacterMapApplyMode ApplyMode);

public sealed record CharacterMapPreview(
    CharacterMapApplyMode ApplyMode,
    int ChangedEntryCount,
    int ChangedByteCount,
    IReadOnlyList<string> Issues)
{
    public bool CanApply => Issues.Count == 0;

    public IReadOnlyList<string> Changes { get; init; } = [];
}

public static class CharacterMapPresets
{
    public static CharacterMapProfile Belarusian =>
        BundledCharacterMapProvider.BelarusianViceCity;
}
