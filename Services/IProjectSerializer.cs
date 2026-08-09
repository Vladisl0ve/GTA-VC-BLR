using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public interface IProjectSerializer
{
    EditorProject Load(string path);

    void Save(string path, EditorProject project);
}
