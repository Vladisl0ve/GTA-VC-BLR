using GTA_GXT_Editor.Models;

namespace GTA_GXT_Editor.Services;

public interface IDialogService
{
    string? OpenFile(string title, string filter);

    IReadOnlyList<string> OpenFiles(string title, string filter);

    string? SaveFile(string title, string filter, string suggestedPath);

    bool Confirm(string message, string title);

    void ShowInfo(string message, string? title = null);

    void ShowError(string message, string? title = null);

    EntryEditorResult? EditEntry(EntryEditorRequest request);

    UnsavedChangesChoice ConfirmUnsavedChanges() => UnsavedChangesChoice.Discard;

    CharacterMapEditorResult? EditCharacterMap(CharacterMapEditorRequest request) => null;

    InstallerProfileEditorResult? EditInstallerProfile(InstallerProfileEditorRequest request) => null;
}

public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel,
}
