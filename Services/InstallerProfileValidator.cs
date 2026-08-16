using System.Buffers.Binary;
using System.IO;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class InstallerProfileValidator
{
    public const int MaximumAssets = 512;
    public const long MaximumPayloadSize = 512L * 1024 * 1024;
    public const string MainAsiDestination = "BelarusianLanguage.asi";

    private static readonly string[] RequiredSilentPatchDestinations =
    [
        "SilentPatchVC.asi",
        "SilentPatchVC.ini",
        "data\\maps\\club\\CLUB.ipl",
        "data\\maps\\hotel\\hotel.IPL",
        "data\\maps\\littleha\\littleha.ipl",
        "data\\maps\\mansion\\mansion.ipl",
        "data\\maps\\oceandn\\oceandN.ipl",
        "data\\maps\\oceandrv\\oceandrv.ipl",
        "data\\maps\\stripclb\\stripclb.ipl",
        "data\\maps\\washints\\washints.ipl",
    ];

    private static readonly IReadOnlyList<string> ReadOnlySilentPatchDestinations =
        Array.AsReadOnly(RequiredSilentPatchDestinations);

    public static IReadOnlyList<string> SilentPatchDestinations => ReadOnlySilentPatchDestinations;

    private static readonly HashSet<string> ReservedDestinations = new(
        ["TEXT\\BELARUS.GXT", "MODELS\\FONTS.TXD"],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ReservedNames = new(
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static void Validate(InstallerProfile profile)
    {
        ValidateCommon(profile);

        if (profile.Assets.Any(asset =>
                asset.Role is not InstallerAssetRole.MainAsi and not InstallerAssetRole.SilentPatch))
        {
            Throw("Installer.Validation.UnsupportedRole");
        }

        var mainAsi = profile.Assets.Where(asset => asset.Role == InstallerAssetRole.MainAsi).ToList();
        if (mainAsi.Count != 1 ||
            !string.Equals(mainAsi[0].DestinationPath, MainAsiDestination, StringComparison.OrdinalIgnoreCase))
        {
            Throw("Installer.Validation.MainAsi");
        }

        var silentPatch = profile.Assets
            .Where(asset => asset.Role == InstallerAssetRole.SilentPatch)
            .Select(asset => asset.DestinationPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (silentPatch.Count != RequiredSilentPatchDestinations.Length ||
            RequiredSilentPatchDestinations.Any(path => !silentPatch.Contains(path)))
        {
            Throw("Installer.Validation.SilentPatch");
        }
    }

    public static void ValidateForStorage(InstallerProfile profile)
    {
        ValidateCommon(profile);

        var mainAsi = profile.Assets.Where(asset => asset.Role == InstallerAssetRole.MainAsi).ToList();
        if (mainAsi.Count > 1 ||
            mainAsi.Count == 1 &&
            !string.Equals(mainAsi[0].DestinationPath, MainAsiDestination, StringComparison.OrdinalIgnoreCase))
        {
            Throw("Installer.Validation.MainAsi");
        }

        var loaders = profile.Assets.Where(asset => asset.Role == InstallerAssetRole.AsiLoader).ToList();
        if (loaders.Count > 1 ||
            loaders.Count == 1 &&
            !string.Equals(loaders[0].DestinationPath, "dinput8.dll", StringComparison.OrdinalIgnoreCase))
        {
            Throw("Installer.Validation.AsiLoader");
        }
    }

    private static void ValidateCommon(InstallerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ProductId == Guid.Empty)
        {
            Throw("Installer.Validation.ProductId");
        }

        ValidateText(profile.Name, 128, "Installer.Validation.Name");
        ValidateText(profile.Version, 64, "Installer.Validation.Version");
        ValidateText(profile.Publisher, 128, "Installer.Validation.Publisher");

        if (!IsFileNameOnly(profile.OutputFileName) ||
            !profile.OutputFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
            ReservedNames.Contains(Path.GetFileNameWithoutExtension(profile.OutputFileName)) ||
            profile.OutputFileName.Length > 128)
        {
            Throw("Installer.Validation.OutputName");
        }

        if (profile.Assets.Count > MaximumAssets)
        {
            Throw("Installer.Validation.AssetCount", MaximumAssets);
        }

        var assetIds = new HashSet<Guid>();
        var destinations = new HashSet<string>(ReservedDestinations, StringComparer.OrdinalIgnoreCase);
        long totalLength = 0;
        foreach (var asset in profile.Assets)
        {
            if (!Enum.IsDefined(asset.Role))
            {
                Throw("Installer.Validation.AssetRole");
            }

            if (asset.Id == Guid.Empty || !assetIds.Add(asset.Id))
            {
                Throw("Installer.Validation.AssetId");
            }

            if (!IsFileNameOnly(asset.OriginalFileName))
            {
                Throw("Installer.Validation.SourceName", asset.OriginalFileName);
            }

            var destination = NormalizeDestinationPath(asset.DestinationPath);
            asset.DestinationPath = destination;
            if (!destinations.Add(destination))
            {
                Throw("Installer.Validation.DuplicateTarget", destination);
            }

            ValidateAllowedPayloadName(destination);
            totalLength = checked(totalLength + asset.Data.LongLength);
            if (RequiresX86Validation(destination) && !IsX86PeImage(asset.Data))
            {
                Throw("Installer.Validation.X86", asset.OriginalFileName);
            }
        }

        if (totalLength > MaximumPayloadSize)
        {
            Throw("Installer.Validation.PayloadSize");
        }

    }

    public static string NormalizeDestinationPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 240 || path.Any(char.IsControl))
        {
            Throw("Installer.Validation.TargetPath", path);
        }

        var normalized = path.Replace('/', '\\').Trim();
        if (Path.IsPathRooted(normalized) || normalized.StartsWith('\\') || normalized.Contains(':'))
        {
            Throw("Installer.Validation.TargetPath", path);
        }

        var segments = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            segments.Any(segment =>
                segment is "." or ".." ||
                segment.EndsWith(' ') ||
                segment.EndsWith('.') ||
                segment.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0 ||
                ReservedNames.Contains(Path.GetFileNameWithoutExtension(segment))))
        {
            Throw("Installer.Validation.TargetPath", path);
        }

        return string.Join('\\', segments);
    }

    public static bool IsX86PeImage(ReadOnlySpan<byte> data)
    {
        if (data.Length < 0x40 || data[0] != (byte)'M' || data[1] != (byte)'Z')
        {
            return false;
        }

        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(0x3C, 4));
        if (peOffset < 0 || peOffset > data.Length - 6)
        {
            return false;
        }

        var signature = data.Slice(peOffset, 4);
        return signature.SequenceEqual("PE\0\0"u8) &&
               BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(peOffset + 4, 2)) == 0x014C;
    }

    private static void ValidateText(string value, int maximumLength, string resourceKey)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength || value.Any(char.IsControl))
        {
            Throw(resourceKey);
        }
    }

    private static void ValidateAllowedPayloadName(string destination)
    {
        var fileName = Path.GetFileName(destination);
        var extension = Path.GetExtension(fileName);
        if (fileName.StartsWith("README", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("APPLY_", StringComparison.OrdinalIgnoreCase) &&
            extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            fileName.StartsWith("CLEAN_", StringComparison.OrdinalIgnoreCase) &&
            extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("SHA256", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".7z", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".rar", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".c", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase))
        {
            Throw("Installer.Validation.ExcludedFile", destination);
        }
    }

    private static bool RequiresX86Validation(string destination)
    {
        var extension = Path.GetExtension(destination);
        return extension.Equals(".asi", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".dll", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFileNameOnly(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        string.Equals(value, Path.GetFileName(value), StringComparison.Ordinal) &&
        !value.Any(char.IsControl);

    private static void Throw(string resourceKey, params object?[] arguments) =>
        throw new InvalidDataException(LocalizationProvider.Current.Format(resourceKey, arguments));
}
