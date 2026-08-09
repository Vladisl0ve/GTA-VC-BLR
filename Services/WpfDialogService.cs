using System.IO;
using System.Windows;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Views;
using Microsoft.Win32;

namespace GTA_GXT_Editor.Services;

public sealed class WpfDialogService : IDialogService
{
    public string? OpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog(Application.Current.MainWindow) == true
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

        return dialog.ShowDialog(Application.Current.MainWindow) == true
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

        return dialog.ShowDialog(Application.Current.MainWindow) == true
            ? dialog.FileName
            : null;
    }

    public bool Confirm(string message, string title)
    {
        return MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public void ShowInfo(string message, string title = "GTA GXT Editor")
    {
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    public void ShowError(string message, string title = "Ошибка")
    {
        MessageBox.Show(
            Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public EntryEditorResult? EditEntry(EntryEditorRequest request)
    {
        var dialog = new EntryEditorWindow(request)
        {
            Owner = Application.Current.MainWindow,
        };

        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    public UnsavedChangesChoice ConfirmUnsavedChanges()
    {
        var result = MessageBox.Show(
            Application.Current.MainWindow,
            "В проекте есть несохранённые изменения. Сохранить их?",
            "Несохранённые изменения",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => UnsavedChangesChoice.Save,
            MessageBoxResult.No => UnsavedChangesChoice.Discard,
            _ => UnsavedChangesChoice.Cancel,
        };
    }

    public void ShowTxdViewer(TxdViewerRequest request)
    {
        var dialog = new TxdViewerWindow(request)
        {
            Owner = Application.Current.MainWindow,
        };
        dialog.ShowDialog();
    }
}
