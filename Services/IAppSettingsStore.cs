namespace GTA_GXT_Editor.Services;

public interface IAppSettingsStore
{
    string? LoadUiLanguage();

    void SaveUiLanguage(string cultureName);
}
