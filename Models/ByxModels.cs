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

    public ByxDictionaryItem? Dictionary { get; set; }

    public ByxTxdItem? Txd { get; set; }
}

public sealed class ByxManifestV1
{
    public string Format { get; set; } = string.Empty;

    public int Version { get; set; }

    public string Game { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public ByxGxtItem Gxt { get; set; } = null!;

    public ByxDictionaryItem? Dictionary { get; set; }

    public List<ByxTxdItem> Txd { get; set; } = [];
}

public sealed class ByxGxtItem
{
    public string OriginalFileName { get; set; } = string.Empty;

    public string Entry { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;
}

public sealed class ByxTxdItem
{
    public Guid Id { get; set; }

    public string OriginalFileName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Entry { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public ByxDictionaryItem? CharacterMap { get; set; }

    public bool CharacterMapVerified { get; set; }
}

public sealed class ByxDictionaryItem
{
    public string Entry { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;
}

public sealed class ByxCharacterMapping
{
    public int[] Codes { get; set; } = [];

    public string Character { get; set; } = string.Empty;
}
