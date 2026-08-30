using System.IO;
using System.Security.Cryptography;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class FontProfileFingerprint
{
    public static string Compute(CharacterMapProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Convert.ToHexString(SHA256.HashData(CharacterMapFileSerializer.Serialize(profile)));
    }

    public static string Compute(FontMetricsProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return Convert.ToHexString(SHA256.HashData(FontMetricsFileSerializer.Serialize(profile)));
    }
}

public static class AsiFontProfileBindingService
{
    public static AsiFontProfileBinding Create(
        Guid assetId,
        string asiSha256,
        CharacterMapProfile? baseCharacterMap,
        FontMetricsProfile? baseFontMetrics,
        CharacterMapProfile? effectiveCharacterMap,
        FontMetricsProfile? effectiveFontMetrics)
    {
        if (assetId == Guid.Empty)
        {
            throw new ArgumentException("The ASI asset id is missing.", nameof(assetId));
        }

        ValidateSha256(asiSha256);
        var characterPatches = baseCharacterMap is null
            ? []
            : CreateCharacterMapPatches(
                baseCharacterMap,
                effectiveCharacterMap ?? baseCharacterMap);
        var (advancePatches, contextPatches) = baseFontMetrics is null
            ? (new List<FontAdvancePatch>(), new List<FontContextOverridePatch>())
            : CreateFontMetricPatches(
                baseFontMetrics,
                effectiveFontMetrics ?? baseFontMetrics);

        return new AsiFontProfileBinding
        {
            AsiAssetId = assetId,
            AsiSha256 = asiSha256.ToUpperInvariant(),
            BaseCharacterMapFingerprint = baseCharacterMap is null
                ? null
                : FontProfileFingerprint.Compute(baseCharacterMap),
            BaseFontMetricsFingerprint = baseFontMetrics is null
                ? null
                : FontProfileFingerprint.Compute(baseFontMetrics),
            CharacterMapPatches = characterPatches,
            FontAdvancePatches = advancePatches,
            FontContextOverridePatches = contextPatches,
        };
    }

    public static AsiFontProfileBinding Rebase(
        AsiFontProfileBinding binding,
        Guid assetId,
        string asiSha256,
        CharacterMapProfile? baseCharacterMap,
        FontMetricsProfile? baseFontMetrics)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Validate(binding);
        ValidateSha256(asiSha256);
        return new AsiFontProfileBinding
        {
            AsiAssetId = assetId,
            AsiSha256 = asiSha256.ToUpperInvariant(),
            BaseCharacterMapFingerprint = baseCharacterMap is null
                ? null
                : FontProfileFingerprint.Compute(baseCharacterMap),
            BaseFontMetricsFingerprint = baseFontMetrics is null
                ? null
                : FontProfileFingerprint.Compute(baseFontMetrics),
            CharacterMapPatches = baseCharacterMap is null
                ? []
                : binding.CharacterMapPatches.Select(item => item.Clone()).ToList(),
            FontAdvancePatches = baseFontMetrics is null
                ? []
                : binding.FontAdvancePatches.Select(item => item.Clone()).ToList(),
            FontContextOverridePatches = baseFontMetrics is null
                ? []
                : binding.FontContextOverridePatches.Select(item => item.Clone()).ToList(),
        };
    }

    public static (CharacterMapProfile? CharacterMap, FontMetricsProfile? FontMetrics) Apply(
        AsiFontProfileBinding binding,
        CharacterMapProfile? baseCharacterMap,
        FontMetricsProfile? baseFontMetrics)
    {
        ArgumentNullException.ThrowIfNull(binding);
        Validate(binding);
        var characterMap = baseCharacterMap is null
            ? null
            : ApplyCharacterMapPatches(baseCharacterMap, binding.CharacterMapPatches);
        var fontMetrics = baseFontMetrics is null
            ? null
            : ApplyFontMetricPatches(
                baseFontMetrics,
                binding.FontAdvancePatches,
                binding.FontContextOverridePatches);
        return (characterMap, fontMetrics);
    }

    public static void Validate(AsiFontProfileBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Version != AsiFontProfileBinding.CurrentVersion)
        {
            throw new InvalidDataException($"ASI font-profile binding version {binding.Version} is unsupported.");
        }

        if (binding.AsiAssetId == Guid.Empty)
        {
            throw new InvalidDataException("The ASI font-profile binding has no asset id.");
        }

        ValidateSha256(binding.AsiSha256);
        ValidateOptionalFingerprint(binding.BaseCharacterMapFingerprint);
        ValidateOptionalFingerprint(binding.BaseFontMetricsFingerprint);
        if (binding.BaseCharacterMapFingerprint is null && binding.CharacterMapPatches.Count != 0)
        {
            throw new InvalidDataException(
                "ASI character-map overrides require a proven base character map.");
        }

        if (binding.BaseFontMetricsFingerprint is null &&
            (binding.FontAdvancePatches.Count != 0 || binding.FontContextOverridePatches.Count != 0))
        {
            throw new InvalidDataException(
                "ASI font-metric overrides require proven base font metrics.");
        }

        var characters = new HashSet<char>();
        foreach (var patch in binding.CharacterMapPatches)
        {
            if (patch is null || char.IsControl(patch.Character) || char.IsSurrogate(patch.Character) ||
                !characters.Add(patch.Character))
            {
                throw new InvalidDataException("The ASI character-map overrides are invalid or duplicated.");
            }

            if (patch.Remove)
            {
                if (patch.Codes.Count != 0 || patch.PreferredCode is not null)
                {
                    throw new InvalidDataException("A removed ASI character-map entry cannot define codes.");
                }

                continue;
            }

            if (patch.Codes.Count == 0 || patch.Codes.Distinct().Count() != patch.Codes.Count ||
                patch.Codes.Any(code => code < 0x20) ||
                patch.PreferredCode is not { } preferred || !patch.Codes.Contains(preferred))
            {
                throw new InvalidDataException("An ASI character-map override has invalid codes.");
            }
        }

        var advances = new HashSet<(FontTextureKind Font, int Index)>();
        foreach (var patch in binding.FontAdvancePatches)
        {
            if (patch is null || !Enum.IsDefined(patch.Font) ||
                patch.MetricIndex is < 0 or >= FontMetricsTable.MetricCount ||
                patch.Advance > FontMetricsValidator.MaximumAdvance ||
                !advances.Add((patch.Font, patch.MetricIndex)))
            {
                throw new InvalidDataException("The ASI font-advance overrides are invalid or duplicated.");
            }
        }

        var contexts = new HashSet<(FontRenderContext Context, FontTextureKind Font, byte Code)>();
        foreach (var patch in binding.FontContextOverridePatches)
        {
            if (patch is null || !Enum.IsDefined(patch.Context) || !Enum.IsDefined(patch.Font) ||
                patch.Code is < FontMetricsValidator.MinimumCode or > FontMetricsValidator.MaximumCode ||
                patch.Advance > FontMetricsValidator.MaximumAdvance ||
                !contexts.Add((patch.Context, patch.Font, patch.Code)))
            {
                throw new InvalidDataException("The ASI context overrides are invalid or duplicated.");
            }
        }
    }

    private static List<CharacterMapPatch> CreateCharacterMapPatches(
        CharacterMapProfile baseProfile,
        CharacterMapProfile effectiveProfile)
    {
        ThrowIfInvalid(baseProfile);
        ThrowIfInvalid(effectiveProfile);
        var baseline = baseProfile.Mappings.ToDictionary(item => item.Character);
        var effective = effectiveProfile.Mappings.ToDictionary(item => item.Character);
        var result = new List<CharacterMapPatch>();
        foreach (var character in baseline.Keys.Union(effective.Keys).Order())
        {
            baseline.TryGetValue(character, out var baseEntry);
            effective.TryGetValue(character, out var effectiveEntry);
            if (effectiveEntry is null)
            {
                result.Add(new CharacterMapPatch { Character = character, Remove = true });
                continue;
            }

            if (baseEntry is not null && EntriesMatch(baseEntry, effectiveEntry))
            {
                continue;
            }

            result.Add(new CharacterMapPatch
            {
                Character = character,
                Codes = effectiveEntry.Codes.ToList(),
                PreferredCode = effectiveEntry.PreferredCode,
            });
        }

        return result;
    }

    private static (List<FontAdvancePatch>, List<FontContextOverridePatch>) CreateFontMetricPatches(
        FontMetricsProfile baseProfile,
        FontMetricsProfile effectiveProfile)
    {
        ThrowIfInvalid(baseProfile);
        ThrowIfInvalid(effectiveProfile);
        var advances = new List<FontAdvancePatch>();
        AddAdvancePatches(FontTextureKind.Font2, baseProfile.Font2, effectiveProfile.Font2, advances);
        AddAdvancePatches(FontTextureKind.Font1, baseProfile.Font1, effectiveProfile.Font1, advances);

        var baseline = baseProfile.Overrides.ToDictionary(GetOverrideKey);
        var effective = effectiveProfile.Overrides.ToDictionary(GetOverrideKey);
        var contexts = new List<FontContextOverridePatch>();
        foreach (var key in baseline.Keys.Union(effective.Keys)
                     .OrderBy(item => item.Context)
                     .ThenBy(item => item.Font)
                     .ThenBy(item => item.Code))
        {
            baseline.TryGetValue(key, out var baseItem);
            effective.TryGetValue(key, out var effectiveItem);
            if (baseItem?.Advance == effectiveItem?.Advance)
            {
                continue;
            }

            contexts.Add(new FontContextOverridePatch
            {
                Context = key.Context,
                Font = key.Font,
                Code = key.Code,
                Advance = effectiveItem?.Advance,
            });
        }

        return (advances, contexts);
    }

    private static CharacterMapProfile ApplyCharacterMapPatches(
        CharacterMapProfile baseProfile,
        IReadOnlyList<CharacterMapPatch> patches)
    {
        var result = baseProfile.Clone();
        var affected = patches.Select(item => item.Character).ToHashSet();
        var replacementCodes = patches
            .Where(item => !item.Remove)
            .SelectMany(item => item.Codes.Select(code => (item.Character, Code: code)))
            .ToArray();
        foreach (var replacement in replacementCodes)
        {
            var owner = result.Mappings.SingleOrDefault(item => item.Codes.Contains(replacement.Code));
            if (owner is not null && owner.Character != replacement.Character && !affected.Contains(owner.Character))
            {
                throw new InvalidDataException(
                    $"ASI mapping override for '{replacement.Character}' conflicts with '{owner.Character}' at code 0x{replacement.Code:X2}.");
            }
        }

        result.Mappings.RemoveAll(item => affected.Contains(item.Character));
        foreach (var patch in patches.Where(item => !item.Remove))
        {
            result.Mappings.Add(new CharacterMapEntry
            {
                Character = patch.Character,
                Codes = patch.Codes.ToList(),
                PreferredCode = patch.PreferredCode!.Value,
            });
        }

        ThrowIfInvalid(result);
        result.IsVerified = false;
        return result;
    }

    private static FontMetricsProfile ApplyFontMetricPatches(
        FontMetricsProfile baseProfile,
        IReadOnlyList<FontAdvancePatch> advancePatches,
        IReadOnlyList<FontContextOverridePatch> contextPatches)
    {
        var result = baseProfile.Clone();
        foreach (var patch in advancePatches)
        {
            var table = patch.Font == FontTextureKind.Font1 ? result.Font1 : result.Font2;
            table.Advances[patch.MetricIndex] = patch.Advance;
        }

        foreach (var patch in contextPatches)
        {
            result.Overrides.RemoveAll(item =>
                item.Context == patch.Context && item.Font == patch.Font && item.Code == patch.Code);
            if (patch.Advance is { } advance)
            {
                result.Overrides.Add(new FontMetricOverride
                {
                    Context = patch.Context,
                    Font = patch.Font,
                    Code = patch.Code,
                    Advance = advance,
                });
            }
        }

        ThrowIfInvalid(result);
        return result;
    }

    private static void AddAdvancePatches(
        FontTextureKind font,
        FontMetricsTable baseline,
        FontMetricsTable effective,
        List<FontAdvancePatch> result)
    {
        for (var index = 0; index < FontMetricsTable.MetricCount; index++)
        {
            if (baseline.Advances[index] == effective.Advances[index])
            {
                continue;
            }

            result.Add(new FontAdvancePatch
            {
                Font = font,
                MetricIndex = index,
                Advance = effective.Advances[index],
            });
        }
    }

    private static (FontRenderContext Context, FontTextureKind Font, byte Code) GetOverrideKey(
        FontMetricOverride item) => (item.Context, item.Font, item.Code);

    private static bool EntriesMatch(CharacterMapEntry left, CharacterMapEntry right) =>
        left.PreferredCode == right.PreferredCode && left.Codes.Order().SequenceEqual(right.Codes.Order());

    private static void ThrowIfInvalid(CharacterMapProfile profile)
    {
        var issues = CharacterMapService.Validate(profile);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, issues));
        }
    }

    private static void ThrowIfInvalid(FontMetricsProfile profile)
    {
        var issues = FontMetricsValidator.Validate(profile);
        if (issues.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, issues));
        }
    }

    private static void ValidateOptionalFingerprint(string? value)
    {
        if (value is not null)
        {
            ValidateSha256(value);
        }
    }

    private static void ValidateSha256(string value)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("An ASI SHA-256 fingerprint is invalid.");
        }
    }
}
