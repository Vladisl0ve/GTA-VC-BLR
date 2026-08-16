using System.Windows;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.ViewModels;

namespace GTA_GXT_Editor.Views;

public partial class TxdViewerWindow : Window, IDisposable
{
    private readonly TxdViewerViewModel _viewModel;
    private bool _disposed;

    public TxdViewerWindow(
        CharacterMapEditorRequest request,
        IDialogService dialogs,
        ILocalizationService? localization = null)
    {
        InitializeComponent();
        _viewModel = new TxdViewerViewModel(request, dialogs, localization);
        _viewModel.ApplySucceeded += ViewModel_OnApplySucceeded;
        DataContext = _viewModel;
        Closed += (_, _) => Dispose();
    }

    public CharacterMapEditorResult? Result { get; private set; }

    private void ViewModel_OnApplySucceeded(object? sender, EventArgs e)
    {
        Result = _viewModel.Result;
        DialogResult = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _viewModel.ApplySucceeded -= ViewModel_OnApplySucceeded;
        _viewModel.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
