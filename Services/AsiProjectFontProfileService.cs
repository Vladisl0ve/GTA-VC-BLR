using System.IO;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public interface IAsiProjectFontProfileService
{
    void Synchronize(EditorProject project, InstallerProfile? installerProfile = null);

    void UpdateEffectiveProfiles(
        EditorProject project,
        CharacterMapProfile characterMap,
        FontMetricsProfile? fontMetrics);

    string GetStatus(EditorProject project, ILocalizationService localization);
}

public sealed class AsiProjectFontProfileService(
    IAsiFontProfileReader? reader = null) : IAsiProjectFontProfileService
{
    private readonly IAsiFontProfileReader _reader = reader ?? new AsiFontProfileReader();

    public void Synchronize(EditorProject project, InstallerProfile? installerProfile = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        var profile = installerProfile ?? project.InstallerProfile;
        var mainAsiAssets = profile?.Assets
            .Where(asset => asset.Role == InstallerAssetRole.MainAsi)
            .ToArray() ?? [];
        if (mainAsiAssets.Length > 1)
        {
            throw new InvalidDataException("The installer profile contains more than one Main ASI asset.");
        }

        if (mainAsiAssets.Length == 0)
        {
            var hadBinding = project.AsiFontProfileBinding is not null;
            project.AsiFontProfileBinding = null;
            project.AsiFontProfileState = null;
            project.IsDirty |= hadBinding;
            return;
        }

        var mainAsi = mainAsiAssets[0];
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(mainAsi.Data));
        AsiFontProfileReadResult readResult;
        try
        {
            readResult = _reader.Read(mainAsi.Data, mainAsi.OriginalFileName);
        }
        catch (InvalidDataException exception)
        {
            var changed = project.AsiFontProfileBinding is not null;
            project.AsiFontProfileBinding = null;
            project.AsiFontProfileState = new AsiFontProfileState
            {
                AsiAssetId = mainAsi.Id,
                AsiSha256 = hash,
                RecognitionLevel = AsiRecognitionLevel.Unknown,
                Diagnostics =
                [
                    new AsiDiagnostic(
                        "ASI_INVALID",
                        AsiDiagnosticSeverity.Error,
                        exception.Message),
                ],
            };
            project.IsDirty |= changed;
            return;
        }

        hash = readResult.Build?.FileSha256 ?? hash;
        var state = new AsiFontProfileState
        {
            AsiAssetId = mainAsi.Id,
            AsiSha256 = hash,
            RecognitionLevel = readResult.RecognitionLevel,
            Build = readResult.Build,
            BaseCharacterMap = readResult.CharacterMap?.Clone(),
            BaseFontMetrics = readResult.FontMetrics?.Clone(),
            Diagnostics = readResult.Diagnostics.ToArray(),
            PhysicalPatches = readResult.PhysicalPatches.ToArray(),
            ExecutableTargets = readResult.ExecutableTargets.ToArray(),
        };

        if (!readResult.HasUsableProfile)
        {
            var changed = project.AsiFontProfileBinding is not null;
            project.AsiFontProfileBinding = null;
            project.AsiFontProfileState = state;
            project.IsDirty |= changed;
            return;
        }

        var oldBinding = project.AsiFontProfileBinding;
        AsiFontProfileBinding binding;
        CharacterMapProfile? effectiveMap;
        FontMetricsProfile? effectiveMetrics;
        if (oldBinding is null)
        {
            binding = AsiFontProfileBindingService.Create(
                mainAsi.Id,
                hash,
                readResult.CharacterMap,
                readResult.FontMetrics,
                project.CharacterMap,
                project.FontMetrics);
            effectiveMap = readResult.CharacterMap is null
                ? project.CharacterMap?.Clone()
                : (project.CharacterMap ?? readResult.CharacterMap).Clone();
            effectiveMetrics = readResult.FontMetrics is null
                ? project.FontMetrics?.Clone()
                : (project.FontMetrics ?? readResult.FontMetrics).Clone();
        }
        else
        {
            var sourceUnchanged = oldBinding.AsiAssetId == mainAsi.Id &&
                                  string.Equals(
                                      oldBinding.AsiSha256,
                                      hash,
                                      StringComparison.OrdinalIgnoreCase);
            if (sourceUnchanged)
            {
                VerifyBaseFingerprint(
                    oldBinding.BaseCharacterMapFingerprint,
                    readResult.CharacterMap is null
                        ? null
                        : FontProfileFingerprint.Compute(readResult.CharacterMap),
                    "character map");
                VerifyBaseFingerprint(
                    oldBinding.BaseFontMetricsFingerprint,
                    readResult.FontMetrics is null
                        ? null
                        : FontProfileFingerprint.Compute(readResult.FontMetrics),
                    "font metrics");
                binding = oldBinding.Clone();
            }
            else
            {
                binding = AsiFontProfileBindingService.Rebase(
                    oldBinding,
                    mainAsi.Id,
                    hash,
                    readResult.CharacterMap,
                    readResult.FontMetrics);
            }

            var applied = AsiFontProfileBindingService.Apply(
                binding,
                readResult.CharacterMap,
                readResult.FontMetrics);
            effectiveMap = applied.CharacterMap ?? project.CharacterMap?.Clone();
            effectiveMetrics = applied.FontMetrics ?? project.FontMetrics?.Clone();
            if (sourceUnchanged)
            {
                if (readResult.CharacterMap is not null)
                {
                    VerifyEffectiveCache(project.CharacterMap, applied.CharacterMap, "character map");
                    if (effectiveMap is not null && project.CharacterMap is not null)
                    {
                        effectiveMap.IsVerified = project.CharacterMap.IsVerified;
                    }
                }

                if (readResult.FontMetrics is not null)
                {
                    VerifyEffectiveCache(project.FontMetrics, applied.FontMetrics, "font metrics");
                }
            }
        }

        ApplyState(project, binding, state, effectiveMap, effectiveMetrics);
        project.IsDirty |= oldBinding is null ||
                           oldBinding.AsiAssetId != binding.AsiAssetId ||
                           !string.Equals(oldBinding.AsiSha256, binding.AsiSha256, StringComparison.Ordinal);
    }

    public void UpdateEffectiveProfiles(
        EditorProject project,
        CharacterMapProfile characterMap,
        FontMetricsProfile? fontMetrics)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(characterMap);
        var map = characterMap.Clone();
        var metrics = fontMetrics?.Clone();
        AsiFontProfileBinding? binding = null;
        if (project.AsiFontProfileState is { } state &&
            project.AsiFontProfileBinding is not null &&
            state.RecognitionLevel != AsiRecognitionLevel.Unknown)
        {
            binding = AsiFontProfileBindingService.Create(
                state.AsiAssetId,
                state.AsiSha256,
                state.BaseCharacterMap,
                state.BaseFontMetrics,
                map,
                metrics);
        }

        project.CharacterMap = map;
        project.FontMetrics = metrics;
        project.AsiFontProfileBinding = binding;
        project.UsesCustomDictionary = true;
        project.GxtManager.CharacterMap = map.Clone();
    }

    public string GetStatus(EditorProject project, ILocalizationService localization)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(localization);
        var state = project.AsiFontProfileState;
        if (state is null)
        {
            return localization.Get("Asi.Status.None");
        }

        var shortHash = state.AsiSha256[..Math.Min(12, state.AsiSha256.Length)];
        if (state.RecognitionLevel == AsiRecognitionLevel.Unknown)
        {
            return localization.Format("Asi.Status.Unknown", shortHash);
        }

        var binding = project.AsiFontProfileBinding;
        var mappingCount = binding?.CharacterMapPatches.Count ?? 0;
        var metricCount = (binding?.FontAdvancePatches.Count ?? 0) +
                          (binding?.FontContextOverridePatches.Count ?? 0);
        var version = state.Build?.Version ?? state.Build?.BuildTag ?? "?";
        return localization.Format(
            "Asi.Status.Recognized",
            version,
            shortHash,
            state.RecognitionLevel,
            mappingCount,
            metricCount);
    }

    private static void ApplyState(
        EditorProject project,
        AsiFontProfileBinding binding,
        AsiFontProfileState state,
        CharacterMapProfile? characterMap,
        FontMetricsProfile? fontMetrics)
    {
        project.AsiFontProfileBinding = binding;
        project.AsiFontProfileState = state;
        project.CharacterMap = characterMap;
        project.FontMetrics = fontMetrics;
        if (characterMap is not null)
        {
            project.UsesCustomDictionary = true;
            project.GxtManager.CharacterMap = characterMap.Clone();
        }
    }

    private static void VerifyBaseFingerprint(string? stored, string? actual, string description)
    {
        if (!string.Equals(stored, actual, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The linked ASI {description} fingerprint does not match the saved binding.");
        }
    }

    private static void VerifyEffectiveCache(
        CharacterMapProfile? cached,
        CharacterMapProfile? computed,
        string description)
    {
        if (cached is null && computed is null)
        {
            return;
        }

        if (cached is null || computed is null ||
            !string.Equals(
                FontProfileFingerprint.Compute(cached),
                FontProfileFingerprint.Compute(computed),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The saved effective {description} does not match the linked ASI and its overrides.");
        }
    }

    private static void VerifyEffectiveCache(
        FontMetricsProfile? cached,
        FontMetricsProfile? computed,
        string description)
    {
        if (cached is null && computed is null)
        {
            return;
        }

        if (cached is null || computed is null ||
            !string.Equals(
                FontProfileFingerprint.Compute(cached),
                FontProfileFingerprint.Compute(computed),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"The saved effective {description} does not match the linked ASI and its overrides.");
        }
    }
}
