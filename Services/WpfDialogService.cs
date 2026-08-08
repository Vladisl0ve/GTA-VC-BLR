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

    public string? SaveFile(string title, string filter, string suggestedPath)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            AddExtension = true,
            DefaultExt = ".gxt",
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
}
