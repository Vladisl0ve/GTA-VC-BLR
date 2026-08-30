using System.IO;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public interface IDocumentWorkflow
{
    EditorProject CreateSnapshot(EditorProject project);

    Task<EditorProject> CreateSnapshotAsync(
        EditorProject project,
        CancellationToken cancellationToken);

    GXTType DetectType(string path);

    EditorProject OpenGxt(
        string path,
        string? characterMapPath = null,
        GxtLanguage language = GxtLanguage.Auto);

    EditorProject OpenProject(string path);

    Task<EditorProject> OpenGxtAsync(
        string path,
        string? characterMapPath,
        GxtLanguage language,
        CancellationToken cancellationToken);

    Task<EditorProject> OpenProjectAsync(string path, CancellationToken cancellationToken);

    CommonGXTManager OpenRelatedGxt(string path, EditorSession session);

    Task<CommonGXTManager> OpenRelatedGxtAsync(
        string path,
        EditorSession session,
        CancellationToken cancellationToken);

    void SaveGxt(string path, EditorProject project);

    void SaveProject(string path, EditorProject project);

    Task SaveGxtAsync(
        string path,
        EditorProject project,
        CancellationToken cancellationToken);

    Task SaveProjectAsync(
        string path,
        EditorProject project,
        CancellationToken cancellationToken);
}

public sealed class DocumentWorkflow(
    GxtManagerFactory managerFactory,
    IProjectSerializer projectSerializer) : IDocumentWorkflow
{
    public EditorProject CreateSnapshot(EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        using var gxtStream = new MemoryStream();
        project.GxtManager.WriteGXT(gxtStream);
        var appliedCharacterMap = project.UsesCustomDictionary
            ? project.GxtManager.CharacterMap
            : null;
        var manager = managerFactory.Open(
            gxtStream.ToArray(),
            project.GameType,
            project.GxtSourceName,
            project.GxtManager.Language,
            appliedCharacterMap);
        var attachment = project.AttachedTxd is null
            ? null
            : new TxdAttachment
            {
                Id = project.AttachedTxd.Id,
                OriginalFileName = project.AttachedTxd.OriginalFileName,
                DisplayName = project.AttachedTxd.DisplayName,
                SourcePath = project.AttachedTxd.SourcePath,
                Data = project.AttachedTxd.Data.ToArray(),
                Document = project.AttachedTxd.Document,
            };
        var metadata = ProjectMetadataJsonSerializer.Deserialize(
            ProjectMetadataJsonSerializer.Serialize(project.Metadata, project.GameType),
            project.GameType);

        return new EditorProject
        {
            ProjectPath = project.ProjectPath,
            GxtSourceName = project.GxtSourceName,
            GxtSourcePath = project.GxtSourcePath,
            GameType = project.GameType,
            GxtManager = manager,
            UsesCustomDictionary = project.UsesCustomDictionary,
            AttachedTxd = attachment,
            CharacterMap = project.CharacterMap?.Clone(),
            FontMetrics = project.FontMetrics?.Clone(),
            AsiFontProfileBinding = project.AsiFontProfileBinding?.Clone(),
            AsiFontProfileState = project.AsiFontProfileState?.Clone(),
            Metadata = metadata,
            InstallerProfile = project.InstallerProfile?.Clone(),
            IsDirty = project.IsDirty,
        };
    }

    public Task<EditorProject> CreateSnapshotAsync(
        EditorProject project,
        CancellationToken cancellationToken) =>
        BackgroundOperation.Run(() => CreateSnapshot(project), cancellationToken);

    public GXTType DetectType(string path) => managerFactory.DetectType(path);

    public EditorProject OpenGxt(
        string path,
        string? characterMapPath = null,
        GxtLanguage language = GxtLanguage.Auto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                LocalizationProvider.Current.Format("Document.FileUnavailable", path),
                path);
        }

        var manager = managerFactory.Open(path, characterMapPath, language);
        return new EditorProject
        {
            ProjectPath = null,
            GxtSourceName = Path.GetFileName(path),
            GxtSourcePath = path,
            GameType = managerFactory.DetectType(path),
            GxtManager = manager,
            UsesCustomDictionary = characterMapPath is not null,
            AttachedTxd = null,
            CharacterMap = null,
            IsDirty = false,
        };
    }

    public EditorProject OpenProject(string path) => projectSerializer.Load(path);

    public Task<EditorProject> OpenGxtAsync(
        string path,
        string? characterMapPath,
        GxtLanguage language,
        CancellationToken cancellationToken) =>
        BackgroundOperation.Run(
            () => OpenGxt(path, characterMapPath, language),
            cancellationToken);

    public Task<EditorProject> OpenProjectAsync(
        string path,
        CancellationToken cancellationToken) =>
        BackgroundOperation.Run(() => OpenProject(path), cancellationToken);

    public CommonGXTManager OpenRelatedGxt(string path, EditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var currentManager = session.Manager;
        var manager = managerFactory.Open(
            path,
            currentManager?.CharacterMapPath,
            currentManager?.Language ?? GxtLanguage.Auto);
        if (session.Project?.CharacterMap is not null)
        {
            manager.CharacterMap = session.Project.CharacterMap.Clone();
        }
        else if (session.Project?.UsesCustomDictionary == true &&
                 string.IsNullOrWhiteSpace(currentManager?.CharacterMapPath) &&
                 currentManager is not null)
        {
            manager.CharacterMap = currentManager.CharacterMap.Clone();
        }

        return manager;
    }

    public Task<CommonGXTManager> OpenRelatedGxtAsync(
        string path,
        EditorSession session,
        CancellationToken cancellationToken) =>
        BackgroundOperation.Run(() => OpenRelatedGxt(path, session), cancellationToken);

    public void SaveGxt(string path, EditorProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.GxtManager.SaveGXTChanges(path);
    }

    public void SaveProject(string path, EditorProject project) =>
        projectSerializer.Save(path, project);

    public Task SaveGxtAsync(
        string path,
        EditorProject project,
        CancellationToken cancellationToken) =>
        BackgroundOperation.Run(() => SaveGxt(path, project), cancellationToken);

    public Task SaveProjectAsync(
        string path,
        EditorProject project,
        CancellationToken cancellationToken) =>
        BackgroundOperation.Run(() => SaveProject(path, project), cancellationToken);
}
