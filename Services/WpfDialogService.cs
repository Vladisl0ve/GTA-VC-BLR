using System.IO;
using System.Windows;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Views;
using Microsoft.Win32;

namespace GTA_GXT_Editor.Services;

public sealed class WpfDialogService : IDialogService
{
    private readonly ILocalizationService _localization;

    public WpfDialogService(ILocalizationService? localization = null)
    {
        _localization = localization ?? LocalizationProvider.Current;
    }

    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog(GetOwner()) == true
            ? dialog.FileName
            : null;
    }

    public IReadOnlyList<string> OpenFiles(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = true,
        };

        return dialog.ShowDialog(GetOwner()) == true
            ? dialog.FileNames
            : [];
    }

    public string? SaveFile(string title, string filter, string suggestedPath)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            AddExtension = true,
            DefaultExt = Path.GetExtension(suggestedPath),
            FileName = Path.GetFileName(suggestedPath),
            InitialDirectory = Path.GetDirectoryName(suggestedPath),
        };

        return dialog.ShowDialog(GetOwner()) == true
            ? dialog.FileName
            : null;
    }

    public bool Confirm(string message, string title)
    {
        return MessageBox.Show(
            GetOwner(),
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public void ShowInfo(string message, string? title = null)
    {
        MessageBox.Show(
            GetOwner(),
            message,
            title ?? _localization.Get("App.Name"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public void ShowError(string message, string? title = null)
    {
        MessageBox.Show(
            GetOwner(),
            message,
            title ?? _localization.Get("Common.Error"),
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public EntryEditorResult? EditEntry(EntryEditorRequest request)
    {
        var dialog = new EntryEditorWindow(request, _localization)
        {
            Owner = GetOwner(),
        };

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public UnsavedChangesChoice ConfirmUnsavedChanges()
    {
        var result = MessageBox.Show(
            GetOwner(),
            _localization.Get("Message.UnsavedChanges"),
            _localization.Get("Message.UnsavedChangesTitle"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => UnsavedChangesChoice.Save,
            MessageBoxResult.No => UnsavedChangesChoice.Discard,
            _ => UnsavedChangesChoice.Cancel,
        };
    }

    public CharacterMapEditorResult? EditCharacterMap(CharacterMapEditorRequest request)
    {
        var dialog = new TxdViewerWindow(request, this, _localization)
        {
            Owner = GetOwner(),
        };
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private static Window? GetOwner() => Application.Current.Windows
        .OfType<Window>()
        .FirstOrDefault(window => window.IsActive) ??
        Application.Current.MainWindow;
}
