using System.Buffers.Binary;
using System.IO;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public static class InstallerProfileValidator
{
    public const int MaximumAssets = 512;
    public const int MaximumGameTxdAssets = 23;
    public const long MaximumPayloadSize = 1024L * 1024 * 1024;
    public const string MainAsiDestination = "BelarusianLanguage.asi";
    public const string Gta3ImgDestination = "MODELS\\gta3.img";
    public const string Gta3DirDestination = "MODELS\\gta3.dir";

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

    private static readonly string[] RequiredModelsArchiveDestinations =
    [
        Gta3ImgDestination,
        Gta3DirDestination,
    ];

    private static readonly IReadOnlyList<string> ReadOnlySilentPatchDestinations =
        Array.AsReadOnly(RequiredSilentPatchDestinations);

    private static readonly IReadOnlyList<string> ReadOnlyModelsArchiveDestinations =
        Array.AsReadOnly(RequiredModelsArchiveDestinations);

    public static IReadOnlyList<string> SilentPatchDestinations => ReadOnlySilentPatchDestinations;

    public static IReadOnlyList<string> ModelsArchiveDestinations => ReadOnlyModelsArchiveDestinations;

    private static readonly HashSet<string> ReservedDestinations = new(
        ["TEXT\\BELARUS.GXT", "MODELS\\FONTS.TXD", "BelarusianLanguage.ini"],
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
                asset.Role is not InstallerAssetRole.MainAsi and
                not InstallerAssetRole.SilentPatch and
                not InstallerAssetRole.ModelsArchive and
                not InstallerAssetRole.GameTxd))
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

        ValidateModelsArchiveComplete(profile);
        ValidateGameTxdAssets(profile);
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

        ValidateModelsArchiveForStorage(profile);
        ValidateGameTxdAssets(profile);
    }

    public static string GetGameTxdDestination(string fileName)
    {
        if (!IsFileNameOnly(fileName) ||
            !Path.GetExtension(fileName).Equals(".txd", StringComparison.OrdinalIgnoreCase))
        {
            Throw("Installer.Validation.GameTxd");
        }

        return "txd\\" + fileName;
    }

    public static bool IsGameTxdDestination(string destination)
    {
        var segments = destination.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2 &&
               segments[0].Equals("txd", StringComparison.OrdinalIgnoreCase) &&
               Path.GetExtension(segments[1]).Equals(".txd", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(segments[1], Path.GetFileName(segments[1]), StringComparison.Ordinal);
    }

    public static bool IsModelsArchiveDestination(string destination) =>
        destination.Equals(Gta3ImgDestination, StringComparison.OrdinalIgnoreCase) ||
        destination.Equals(Gta3DirDestination, StringComparison.OrdinalIgnoreCase);

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

    private static void ValidateModelsArchiveComplete(InstallerProfile profile)
    {
        var archives = profile.Assets
            .Where(asset => asset.Role == InstallerAssetRole.ModelsArchive)
            .Select(asset => asset.DestinationPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (archives.Count != RequiredModelsArchiveDestinations.Length ||
            RequiredModelsArchiveDestinations.Any(path => !archives.Contains(path)))
        {
            Throw("Installer.Validation.ModelsArchive");
        }
    }

    private static void ValidateModelsArchiveForStorage(InstallerProfile profile)
    {
        if (profile.Assets.Any(asset =>
                asset.Role == InstallerAssetRole.ModelsArchive &&
                !IsModelsArchiveDestination(asset.DestinationPath)))
        {
            Throw("Installer.Validation.ModelsArchive");
        }
    }

    private static void ValidateGameTxdAssets(InstallerProfile profile)
    {
        var gameTxd = profile.Assets
            .Where(asset => asset.Role == InstallerAssetRole.GameTxd)
            .ToList();
        if (gameTxd.Count > MaximumGameTxdAssets)
        {
            Throw("Installer.Validation.GameTxdCount", MaximumGameTxdAssets);
        }

        if (gameTxd.Any(asset => !IsGameTxdDestination(asset.DestinationPath)))
        {
            Throw("Installer.Validation.GameTxd");
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
