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
        SelectSingleBinary(
            InstallerAssetRole.MainAsi,
            InstallerProfileValidator.MainAsiDestination,
            "ASI (*.asi)|*.asi|All files (*.*)|*.*");

    private void SelectSilentPatchFolder_OnClick(object sender, RoutedEventArgs e) =>
        SelectSilentPatchFolder();

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

    private void SelectSilentPatchFolder()
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

        var sources = InstallerProfileValidator.SilentPatchDestinations
            .Select(destination => new
            {
                Destination = destination,
                SourcePath = ToSourcePath(dialog.FolderName, destination),
            })
            .ToArray();
        var missing = sources
            .Where(source => !File.Exists(source.SourcePath))
            .Select(source => source.Destination)
            .ToArray();
        if (missing.Length > 0)
        {
            MessageBox.Show(
                this,
                _localization.Format(
                    "Installer.Validation.SilentPatchFolder",
                    string.Join(Environment.NewLine, missing)),
                _localization.Get("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var retainedSize = _assets
            .Where(row => row.Asset.Role != InstallerAssetRole.SilentPatch)
            .Sum(row => row.Asset.Data.LongLength);
        if (sources.Sum(source => new FileInfo(source.SourcePath).Length) >
            InstallerProfileValidator.MaximumPayloadSize - retainedSize)
        {
            ShowValidationError("Installer.Validation.PayloadSize");
            return;
        }

        var replacement = sources
            .Select(source =>
            {
                return new InstallerAsset
                {
                    Id = Guid.NewGuid(),
                    Role = InstallerAssetRole.SilentPatch,
                    OriginalFileName = Path.GetFileName(source.SourcePath),
                    DestinationPath = source.Destination,
                    Data = File.ReadAllBytes(source.SourcePath),
                };
            })
            .ToArray();
        var silentPatchAsi = replacement.Single(asset =>
            asset.DestinationPath.Equals("SilentPatchVC.asi", StringComparison.OrdinalIgnoreCase));
        if (!InstallerProfileValidator.IsX86PeImage(silentPatchAsi.Data))
        {
            MessageBox.Show(
                this,
                _localization.Format("Installer.Validation.X86", silentPatchAsi.OriginalFileName),
                _localization.Get("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        RemoveAssetsByRole(InstallerAssetRole.SilentPatch);
        foreach (var asset in replacement)
        {
            _assets.Add(new InstallerAssetRow(asset, _localization));
        }
    }

    private static string ToSourcePath(string root, string destination) =>
        Path.Combine(root, destination.Replace('\\', Path.DirectorySeparatorChar));

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
        var selected = AssetsGrid.SelectedItems.OfType<InstallerAssetRow>().ToArray();
        if (selected.Any(row => row.Asset.Role == InstallerAssetRole.SilentPatch))
        {
            RemoveAssetsByRole(InstallerAssetRole.SilentPatch);
        }

        foreach (var row in selected.Where(row => row.Asset.Role != InstallerAssetRole.SilentPatch))
        {
            _assets.Remove(row);
        }
    }

    private void RemoveAssetsByRole(InstallerAssetRole role)
    {
        for (var index = _assets.Count - 1; index >= 0; index--)
        {
            if (_assets[index].Asset.Role == role)
            {
                _assets.RemoveAt(index);
            }
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
