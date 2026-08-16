using System.IO;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public interface ICharacterMapWorkflow
{
    TxdAttachment LoadAttachment(string path, Guid? existingId = null);

    Task<TxdAttachment> LoadAttachmentAsync(
        string path,
        Guid? existingId,
        CancellationToken cancellationToken);

    void ExportAttachment(string path, TxdAttachment attachment);

    Task ExportAttachmentAsync(
        string path,
        TxdAttachment attachment,
        CancellationToken cancellationToken);

    CharacterMapPreview Preview(
        CommonGXTManager manager,
        CharacterMapProfile profile,
        CharacterMapApplyMode applyMode);

    void Apply(
        CommonGXTManager manager,
        CharacterMapProfile profile,
        CharacterMapApplyMode applyMode);

    CharacterMapProfile Convert(CommonGXTManager manager, string targetPath);

    Task<CharacterMapProfile> ConvertAsync(
        CommonGXTManager manager,
        string targetPath,
        CancellationToken cancellationToken);
}

public sealed class CharacterMapWorkflow(ITxdReader txdReader) : ICharacterMapWorkflow
{
    public TxdAttachment LoadAttachment(string path, Guid? existingId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var data = File.ReadAllBytes(path);
        return new TxdAttachment
        {
            Id = existingId ?? Guid.NewGuid(),
            OriginalFileName = Path.GetFileName(path),
            DisplayName = Path.GetFileNameWithoutExtension(path),
            SourcePath = path,
            Data = data,
            Document = txdReader.Read(data, path),
        };
    }

    public Task<TxdAttachment> LoadAttachmentAsync(
        string path,
        Guid? existingId,
        CancellationToken cancellationToken) =>
        BackgroundOperation.Run(() => LoadAttachment(path, existingId), cancellationToken);

    public void ExportAttachment(string path, TxdAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        File.WriteAllBytes(path, attachment.Data);
    }

    public Task ExportAttachmentAsync(
        string path,
        TxdAttachment attachment,
        CancellationToken cancellationToken) =>
        BackgroundOperation.Run(() => ExportAttachment(path, attachment), cancellationToken);

    public CharacterMapPreview Preview(
        CommonGXTManager manager,
        CharacterMapProfile profile,
        CharacterMapApplyMode applyMode) =>
        CharacterMapService.Preview(manager, profile, applyMode);

    public void Apply(
        CommonGXTManager manager,
        CharacterMapProfile profile,
        CharacterMapApplyMode applyMode) =>
        CharacterMapService.Apply(manager, profile, applyMode);

    public CharacterMapProfile Convert(CommonGXTManager manager, string targetPath)
    {
        ArgumentNullException.ThrowIfNull(manager);
        var sourceProfile = manager.CharacterMap;
        var targetProfile = CharacterMapFileSerializer.Load(targetPath);
        var sourceCharacters = sourceProfile.Mappings
            .Select(mapping => mapping.Character)
            .ToHashSet();
        var targetCharacters = targetProfile.Mappings
            .Select(mapping => mapping.Character)
            .ToHashSet();
        if (sourceProfile.Mappings.Count != targetProfile.Mappings.Count ||
            !sourceCharacters.SetEquals(targetCharacters))
        {
            throw new InvalidDataException(
                LocalizationProvider.Current.Get("CharacterMap.DifferentSets"));
        }

        var targetBytesByCharacter = targetProfile.ToEncodeMap();
        var byteMap = sourceProfile.Mappings
            .SelectMany(mapping => mapping.Codes.Select(code => new
            {
                Source = code,
                Target = targetBytesByCharacter[mapping.Character],
            }))
            .ToDictionary(pair => pair.Source, pair => pair.Target);
        var convertedValues = manager.GXTEntries
            .Select(entry => entry.Value
                .Select(value => byteMap.GetValueOrDefault(value, value))
                .ToArray())
            .ToArray();

        manager.CharacterMapPath = targetPath;
        manager.CharacterMap = targetProfile.Clone();
        for (var index = 0; index < manager.GXTEntries.Count; index++)
        {
            manager.GXTEntries[index].Value = convertedValues[index];
        }

        return targetProfile.Clone();
    }

    public Task<CharacterMapProfile> ConvertAsync(
        CommonGXTManager manager,
        string targetPath,
        CancellationToken cancellationToken) =>
        BackgroundOperation.Run(() => Convert(manager, targetPath), cancellationToken);
}
