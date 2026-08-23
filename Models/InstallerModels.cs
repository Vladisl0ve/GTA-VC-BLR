namespace GTA_GXT_Editor.Models;

public enum InstallerAssetRole
{
    MainAsi,
    AsiLoader,
    SilentPatch,
    Additional,
    ModelsArchive,
    GameTxd,
}

public sealed class InstallerAsset
{
    public required Guid Id { get; init; }

    public required InstallerAssetRole Role { get; init; }

    public required string OriginalFileName { get; init; }

    public required string DestinationPath { get; set; }

    public required byte[] Data { get; init; }

    public InstallerAsset Clone() => new()
    {
        Id = Id,
        Role = Role,
        OriginalFileName = OriginalFileName,
        DestinationPath = DestinationPath,
        Data = [.. Data],
    };
}

public sealed class InstallerReleaseDocument
{
    public required Guid Id { get; init; }

    public required string OriginalFileName { get; init; }

    public required byte[] Data { get; init; }

    public InstallerReleaseDocument Clone() => new()
    {
        Id = Id,
        OriginalFileName = OriginalFileName,
        Data = [.. Data],
    };
}

public sealed class InstallerProfile
{
    public Guid ProductId { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Grand Theft Auto: Vice City — Беларусізатар";

    public string Version { get; set; } = "1.0.0";

    public string Publisher { get; set; } = string.Empty;

    public string OutputFileName { get; set; } = "GTA_VC_Belarusian_Setup.exe";

    public List<InstallerAsset> Assets { get; set; } = [];

    public InstallerReleaseDocument? ReleaseReadMeEnglish { get; set; }

    public InstallerReleaseDocument? ReleaseReadMeBelarusian { get; set; }

    public InstallerProfile Clone() => new()
    {
        ProductId = ProductId,
        Name = Name,
        Version = Version,
        Publisher = Publisher,
        OutputFileName = OutputFileName,
        Assets = Assets.Select(asset => asset.Clone()).ToList(),
        ReleaseReadMeEnglish = ReleaseReadMeEnglish?.Clone(),
        ReleaseReadMeBelarusian = ReleaseReadMeBelarusian?.Clone(),
    };

    public IEnumerable<InstallerReleaseDocument> EnumerateReleaseDocuments()
    {
        if (ReleaseReadMeEnglish is not null)
        {
            yield return ReleaseReadMeEnglish;
        }

        if (ReleaseReadMeBelarusian is not null)
        {
            yield return ReleaseReadMeBelarusian;
        }
    }
}

public enum InstallerProfileEditorMode
{
    Configure,
    Export,
    ExportReleaseZip,
}

public sealed record InstallerProfileEditorRequest(
    InstallerProfile Profile,
    string SuggestedDirectory,
    InstallerProfileEditorMode Mode);

public sealed record InstallerProfileEditorResult(InstallerProfile Profile);
