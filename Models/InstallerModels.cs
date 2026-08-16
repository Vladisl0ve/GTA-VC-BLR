namespace GTA_GXT_Editor.Models;

public enum InstallerAssetRole
{
    MainAsi,
    AsiLoader,
    SilentPatch,
    Additional,
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

public sealed class InstallerProfile
{
    public Guid ProductId { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "Grand Theft Auto: Vice City — Беларусізатар";

    public string Version { get; set; } = "1.0.0";

    public string Publisher { get; set; } = string.Empty;

    public string OutputFileName { get; set; } = "GTA_VC_Belarusian_Setup.exe";

    public List<InstallerAsset> Assets { get; set; } = [];

    public InstallerProfile Clone() => new()
    {
        ProductId = ProductId,
        Name = Name,
        Version = Version,
        Publisher = Publisher,
        OutputFileName = OutputFileName,
        Assets = Assets.Select(asset => asset.Clone()).ToList(),
    };
}

public enum InstallerProfileEditorMode
{
    Configure,
    Export,
}

public sealed record InstallerProfileEditorRequest(
    InstallerProfile Profile,
    string SuggestedDirectory,
    InstallerProfileEditorMode Mode);

public sealed record InstallerProfileEditorResult(InstallerProfile Profile);
