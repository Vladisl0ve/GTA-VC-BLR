using System.Windows;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.ViewModels;

namespace GTA_GXT_Editor.Views;

public partial class TxdViewerWindow : Window
{
    public TxdViewerWindow(TxdViewerRequest request)
    {
        InitializeComponent();
        DataContext = new TxdViewerViewModel(request);
    }
}
