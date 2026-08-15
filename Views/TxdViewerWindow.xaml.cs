using System.Windows;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.ViewModels;

namespace GTA_GXT_Editor.Views;

public partial class TxdViewerWindow : Window
{
    private readonly TxdViewerViewModel _viewModel;

    public TxdViewerWindow(CharacterMapEditorRequest request, IDialogService dialogs)
    {
        InitializeComponent();
        _viewModel = new TxdViewerViewModel(request, dialogs);
        _viewModel.ApplySucceeded += ViewModel_OnApplySucceeded;
        DataContext = _viewModel;
    }

    public CharacterMapEditorResult? Result { get; private set; }

    private void ViewModel_OnApplySucceeded(object? sender, EventArgs e)
    {
        Result = _viewModel.Result;
        DialogResult = true;
    }
}
