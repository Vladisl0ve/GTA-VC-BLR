using System.Windows;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.ViewModels;
using Microsoft.Win32;

namespace GTA_GXT_Editor.Views;

public partial class TxdViewerWindow : Window
{
    private readonly TxdViewerViewModel _viewModel;

    public TxdViewerWindow(CharacterMapEditorRequest request)
    {
        InitializeComponent();
        _viewModel = new TxdViewerViewModel(request);
        DataContext = _viewModel;
    }

    public CharacterMapEditorResult? Result { get; private set; }

    private void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Импортировать маппинг символов",
            Filter = "Маппинг (*.json;*.txt)|*.json;*.txt|JSON (*.json)|*.json|Словарь (*.txt)|*.txt",
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _viewModel.ReplaceProfile(CharacterMapFileSerializer.Load(dialog.FileName));
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Ошибка импорта", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Экспортировать маппинг символов",
            Filter = "Маппинг JSON (*.gxtmap.json)|*.gxtmap.json|JSON (*.json)|*.json",
            FileName = "characters.gxtmap.json",
            AddExtension = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            CharacterMapFileSerializer.Save(dialog.FileName, _viewModel.Profile);
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Ошибка экспорта", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Result = _viewModel.CreateResult();
            DialogResult = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Проверка профиля", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
