using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.ViewModels;
using GTA_GXT_Editor.Views;

namespace GTA_GXT_Editor;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var dialogs = new WpfDialogService();
        var viewModel = new MainWindowViewModel(new GxtManagerFactory(), dialogs);
        var window = new MainWindow(viewModel);

        MainWindow = window;
        window.Show();

        if (e.Args.FirstOrDefault(File.Exists) is { } startupFile)
        {
            await viewModel.OpenFromCommandLineAsync(startupFile);
        }
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GTA GXT Editor");
            Directory.CreateDirectory(logDirectory);
            File.WriteAllText(Path.Combine(logDirectory, "error.log"), e.Exception.ToString());
        }
        catch
        {
            // Logging must never hide the original application error.
        }

        MessageBox.Show(
            $"Произошла непредвиденная ошибка.\n\n{e.Exception.Message}",
            "GTA GXT Editor",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}
