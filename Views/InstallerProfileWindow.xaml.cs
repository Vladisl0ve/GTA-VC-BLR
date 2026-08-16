using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using Microsoft.Win32;

namespace GTA_GXT_Editor.Views;

public partial class InstallerProfileWindow : Window
{
    private readonly InstallerProfile _profile;
    private readonly ILocalizationService _localization;
    private readonly string _suggestedDirectory;
    private readonly ObservableCollection<InstallerAssetRow> _assets;

    public InstallerProfileWindow(
        InstallerProfileEditorRequest request,
        ILocalizationService localization)
    {
        InitializeComponent();
        _localization = localization;
        _suggestedDirectory = Directory.Exists(request.SuggestedDirectory)
            ? request.SuggestedDirectory
            : Environment.CurrentDirectory;
        _profile = request.Profile.Clone();
        _assets = new ObservableCollection<InstallerAssetRow>(
            _profile.Assets.Select(asset => new InstallerAssetRow(asset, localization)));
        AssetsGrid.ItemsSource = _assets;
        NameTextBox.Text = _profile.Name;
        VersionTextBox.Text = _profile.Version;
        PublisherTextBox.Text = _profile.Publisher;
        OutputNameTextBox.Text = _profile.OutputFileName;
    }

    public InstallerProfileEditorResult? Result { get; private set; }

    private void SelectMainAsi_OnClick(object sender, RoutedEventArgs e) =>
        SelectSingleBinary(InstallerAssetRole.MainAsi, "BelarusianLanguage.asi", "ASI (*.asi)|*.asi|All files (*.*)|*.*");

    private void SelectAsiLoader_OnClick(object sender, RoutedEventArgs e) =>
        SelectSingleBinary(InstallerAssetRole.AsiLoader, "dinput8.dll", "DLL (*.dll)|*.dll|All files (*.*)|*.*");

    private void AddSilentPatchFiles_OnClick(object sender, RoutedEventArgs e) =>
        AddSelectedFiles(InstallerAssetRole.SilentPatch, initialDirectory: _suggestedDirectory);

    private void AddFiles_OnClick(object sender, RoutedEventArgs e) =>
        AddSelectedFiles(InstallerAssetRole.Additional, initialDirectory: _suggestedDirectory);

    private void AddSilentPatchFolder_OnClick(object sender, RoutedEventArgs e) =>
        AddSelectedFolder(InstallerAssetRole.SilentPatch);

    private void AddFolder_OnClick(object sender, RoutedEventArgs e) =>
        AddSelectedFolder(InstallerAssetRole.Additional);

    private void SelectSingleBinary(InstallerAssetRole role, string destination, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = _localization.Get("Installer.Editor.SelectFile"),
            Filter = filter,
            InitialDirectory = _suggestedDirectory,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var data = File.ReadAllBytes(dialog.FileName);
        if (!InstallerProfileValidator.IsX86PeImage(data))
        {
            MessageBox.Show(
                this,
                _localization.Format("Installer.Validation.X86", Path.GetFileName(dialog.FileName)),
                _localization.Get("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        for (var index = _assets.Count - 1; index >= 0; index--)
        {
            if (_assets[index].Asset.Role == role)
            {
                _assets.RemoveAt(index);
            }
        }

        AddAsset(dialog.FileName, destination, role);
    }

    private void AddSelectedFiles(InstallerAssetRole role, string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = _localization.Get("Installer.Editor.SelectFiles"),
            Filter = "All files (*.*)|*.*",
            InitialDirectory = initialDirectory,
            CheckFileExists = true,
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var path in dialog.FileNames.Where(IsPayloadCandidate))
        {
            if (!TryAddAsset(path, Path.GetFileName(path), role))
            {
                break;
            }
        }
    }

    private void AddSelectedFolder(InstallerAssetRole role)
    {
        var dialog = new OpenFolderDialog
        {
            Title = _localization.Get("Installer.Editor.SelectFolder"),
            InitialDirectory = _suggestedDirectory,
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        foreach (var path in Directory.EnumerateFiles(dialog.FolderName, "*", enumerationOptions)
                     .Where(IsPayloadCandidate))
        {
            if (!TryAddAsset(path, Path.GetRelativePath(dialog.FolderName, path), role))
            {
                break;
            }
        }
    }

    private void AddAsset(string sourcePath, string destination, InstallerAssetRole role) =>
        _ = TryAddAsset(sourcePath, destination, role);

    private bool TryAddAsset(string sourcePath, string destination, InstallerAssetRole role)
    {
        if (_assets.Count >= InstallerProfileValidator.MaximumAssets)
        {
            ShowValidationError("Installer.Validation.AssetCount");
            return false;
        }

        var sourceLength = new FileInfo(sourcePath).Length;
        if (sourceLength > InstallerProfileValidator.MaximumPayloadSize -
            _assets.Sum(row => row.Asset.Data.LongLength))
        {
            ShowValidationError("Installer.Validation.PayloadSize");
            return false;
        }

        var asset = new InstallerAsset
        {
            Id = Guid.NewGuid(),
            Role = role,
            OriginalFileName = Path.GetFileName(sourcePath),
            DestinationPath = destination,
            Data = File.ReadAllBytes(sourcePath),
        };
        _assets.Add(new InstallerAssetRow(asset, _localization));
        return true;
    }

    private void ShowValidationError(string resourceKey) => MessageBox.Show(
        this,
        _localization.Get(resourceKey),
        _localization.Get("Common.Error"),
        MessageBoxButton.OK,
        MessageBoxImage.Error);

    private void RemoveAsset_OnClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in AssetsGrid.SelectedItems.OfType<InstallerAssetRow>().ToArray())
        {
            _assets.Remove(row);
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        AssetsGrid.CommitEdit();
        AssetsGrid.CommitEdit();
        _profile.Name = NameTextBox.Text;
        _profile.Version = VersionTextBox.Text;
        _profile.Publisher = PublisherTextBox.Text;
        _profile.OutputFileName = OutputNameTextBox.Text;
        _profile.Assets = _assets.Select(row => row.Asset.Clone()).ToList();
        try
        {
            InstallerProfileValidator.Validate(_profile);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                _localization.Get("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        Result = new InstallerProfileEditorResult(_profile.Clone());
        DialogResult = true;
    }

    private static bool IsPayloadCandidate(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);
        return !name.StartsWith("README", StringComparison.OrdinalIgnoreCase) &&
               !name.Contains("SHA256", StringComparison.OrdinalIgnoreCase) &&
               !extension.Equals(".md", StringComparison.OrdinalIgnoreCase) &&
               !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) &&
               !extension.Equals(".7z", StringComparison.OrdinalIgnoreCase) &&
               !extension.Equals(".rar", StringComparison.OrdinalIgnoreCase) &&
               !extension.Equals(".c", StringComparison.OrdinalIgnoreCase) &&
               !extension.Equals(".cpp", StringComparison.OrdinalIgnoreCase) &&
               !(name.StartsWith("APPLY_", StringComparison.OrdinalIgnoreCase) &&
                 extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase)) &&
               !(name.StartsWith("CLEAN_", StringComparison.OrdinalIgnoreCase) &&
                 extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase));
    }

    public sealed class InstallerAssetRow
    {
        private readonly ILocalizationService _localization;

        public InstallerAssetRow(InstallerAsset asset, ILocalizationService localization)
        {
            Asset = asset;
            _localization = localization;
        }

        public InstallerAsset Asset { get; }

        public string RoleText => _localization.Get($"Installer.Role.{Asset.Role}");

        public string OriginalFileName => Asset.OriginalFileName;

        public string DestinationPath
        {
            get => Asset.DestinationPath;
            set => Asset.DestinationPath = value;
        }

        public string SizeText => Asset.Data.LongLength >= 1024 * 1024
            ? $"{Asset.Data.LongLength / 1024d / 1024d:F1} MB"
            : $"{Math.Max(1, Asset.Data.LongLength / 1024d):F0} KB";
    }
}
