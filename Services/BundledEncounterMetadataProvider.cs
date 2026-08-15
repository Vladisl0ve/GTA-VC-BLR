using System.Collections.ObjectModel;
using System.IO;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public interface IEncounterMetadataProvider
{
    EncounterMetadataIndex? GetIndex(GXTType gameType);
}

public sealed class BundledEncounterMetadataProvider : IEncounterMetadataProvider
{
    private const string ViceCityResourceName =
        "GTA_GXT_Editor.Assets.ViceCity.encounter-order.metadata.json";

    private static readonly Lazy<EncounterMetadataIndex> ViceCityIndex = new(
        LoadViceCity,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public EncounterMetadataIndex? GetIndex(GXTType gameType) =>
        gameType == GXTType.GtaViceCity ? ViceCityIndex.Value : null;

    private static EncounterMetadataIndex LoadViceCity()
    {
        using var stream = typeof(BundledEncounterMetadataProvider).Assembly
            .GetManifestResourceStream(ViceCityResourceName)
            ?? throw new InvalidDataException(
                $"Встроенные encounter metadata '{ViceCityResourceName}' не найдены.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var metadata = ProjectMetadataJsonSerializer.Deserialize(
            buffer.ToArray(),
            GXTType.GtaViceCity);
        return EncounterMetadataIndex.Create(metadata);
    }
}

public sealed class EncounterMetadataIndex
{
    private EncounterMetadataIndex(
        IReadOnlyDictionary<EncounterMetadataIdentity, ProjectEntryMetadata> entries,
        IReadOnlyDictionary<string, EncounterMetadataBlockIndex> blocks,
        IReadOnlyList<EncounterMetadataBlockIndex> orderedBlocks)
    {
        Entries = entries;
        Blocks = blocks;
        OrderedBlocks = orderedBlocks;
    }

    public IReadOnlyDictionary<EncounterMetadataIdentity, ProjectEntryMetadata> Entries { get; }

    public IReadOnlyDictionary<string, EncounterMetadataBlockIndex> Blocks { get; }

    public IReadOnlyList<EncounterMetadataBlockIndex> OrderedBlocks { get; }

    public static EncounterMetadataIndex Create(ProjectMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var entries = metadata.Entries.ToDictionary(
            entry => new EncounterMetadataIdentity(entry.Table, entry.Key));
        var orderedBlocks = metadata.Blocks
            .Select((block, index) => new EncounterMetadataBlockIndex(block, index))
            .ToArray();
        var blocks = orderedBlocks.ToDictionary(block => block.Block.Id, StringComparer.Ordinal);

        return new EncounterMetadataIndex(
            new ReadOnlyDictionary<EncounterMetadataIdentity, ProjectEntryMetadata>(entries),
            new ReadOnlyDictionary<string, EncounterMetadataBlockIndex>(blocks),
            Array.AsReadOnly(orderedBlocks));
    }
}

public readonly record struct EncounterMetadataIdentity(string? Table, string Key);

public sealed record EncounterMetadataBlockIndex(ProjectMetadataBlock Block, int Sequence);
