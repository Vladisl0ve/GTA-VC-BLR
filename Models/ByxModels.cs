using System.Text.Json.Serialization;

namespace GTA_GXT_Editor.Models;

public sealed class ByxManifestHeader
{
    public string Format { get; set; } = string.Empty;

    public int Version { get; set; }
}

public sealed class ByxManifest
{
    public string Format { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Game { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public ByxGxtItem Gxt { get; set; } = null!;

    public ByxTxdItem? Txd { get; set; }

    public ByxCharacterMapItem? CharacterMap { get; set; }

    public ByxArchiveItem? FontMetrics { get; set; }

    public ByxArchiveItem Metadata { get; set; } = null!;

    public ByxInstallerItem? Installer { get; set; }
}

public sealed class ByxGxtItem
{
    public string OriginalFileName { get; set; } = string.Empty;

    public string Entry { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;
}

public sealed class ByxTxdItem
{
    public string OriginalFileName { get; set; } = string.Empty;

    public string Entry { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;
}

public class ByxArchiveItem
{
    public string Entry { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;
}

public sealed class ByxCharacterMapItem : ByxArchiveItem
{
    public bool IsVerified { get; set; }
}

public sealed class ByxInstallerItem : ByxArchiveItem
{
    public List<ByxInstallerAssetItem> Assets { get; set; } = [];
}

public sealed class ByxInstallerAssetItem : ByxArchiveItem
{
    public Guid Id { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;
}

public sealed class ByxInstallerProfileDocument
{
    public const int CurrentVersion = 3;
    public const int LegacyVersion = 2;

    public string Format { get; set; } = "BYX_INSTALLER_PROFILE";

    public int Version { get; set; } = CurrentVersion;

    public Guid ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string InstallerVersion { get; set; } = string.Empty;

    public string Publisher { get; set; } = string.Empty;

    public string OutputFileName { get; set; } = string.Empty;

    public List<ByxInstallerProfileAsset> Assets { get; set; } = [];

    public List<ByxInstallerProfileMod> Mods { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ByxInstallerReleaseDocument? ReleaseReadMeEnglish { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ByxInstallerReleaseDocument? ReleaseReadMeBelarusian { get; set; }
}

public sealed class ByxInstallerProfileMod
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsRequired { get; set; }

    public List<ByxInstallerProfileModFile> Files { get; set; } = [];
}

public sealed class ByxInstallerProfileModFile
{
    public Guid Id { get; set; }

    public string DestinationPath { get; set; } = string.Empty;
}

public sealed class ByxInstallerReleaseDocument
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;
}

public sealed class ByxInstallerProfileAsset
{
    public Guid Id { get; set; }

    public InstallerAssetRole Role { get; set; }

    public string DestinationPath { get; set; } = string.Empty;
}

public sealed class ProjectMetadata
{
    public const int CurrentVersion = 1;

    public string Format { get; set; } = "GXT_ENTRY_METADATA";

    public int Version { get; set; } = CurrentVersion;

    public List<ProjectMetadataBlock> Blocks { get; set; } = [];

    public List<ProjectEntryMetadata> Entries { get; set; } = [];
}

public sealed class ProjectMetadataBlock
{
    public string Id { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int Order { get; set; }
}

public sealed class ProjectEntryMetadata
{
    public string? Table { get; set; }

    public string Key { get; set; } = string.Empty;

    public string? Comment { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsReviewed { get; set; }

    public List<ProjectEntryOccurrence> Occurrences { get; set; } = [];
}

public sealed class ProjectEntryOccurrence
{
    public string BlockId { get; set; } = string.Empty;

    public int Order { get; set; }

    public string? Context { get; set; }
}
