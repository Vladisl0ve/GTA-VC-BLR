using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public sealed class EditorSession
{
    public EditorProject? Project { get; private set; }

    public CommonGXTManager? Manager => Project?.GxtManager;

    public GXTType LoadedType => Project?.GameType ?? GXTType.None;

    public EncounterMetadataIndex? CanonicalMetadata { get; set; }

    public List<ComparisonDocument> Comparisons { get; } = [];

    public bool IsDirty => Project?.IsDirty == true;

    public void Commit(EditorProject project, bool clearComparisons)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (clearComparisons)
        {
            Comparisons.Clear();
        }

        Project = project;
    }

    public void SetDirty(bool value)
    {
        if (Project is not null)
        {
            Project.IsDirty = value;
        }
    }
}

public sealed record ComparisonDocument(
    string Path,
    Dictionary<GxtEntryIdentity, string> Texts);
