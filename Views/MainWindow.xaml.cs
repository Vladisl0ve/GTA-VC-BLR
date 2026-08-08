using System.Windows;
using System.Windows.Input;
using GTA_GXT_Editor.ViewModels;

namespace GTA_GXT_Editor.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void EntriesGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel && viewModel.EditEntryCommand.CanExecute(null))
        {
            viewModel.EditEntryCommand.Execute(null);
        }
    }
}
