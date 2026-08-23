using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using Microsoft.Win32;

namespace GTA_GXT_Editor.Views;

public partial class InstallerProfileWindow : Window
{
    private readonly InstallerProfile _profile;
    private readonly ILocalizationService _localization;
    private readonly string _suggestedDirectory;
    private readonly InstallerProfileEditorMode _mode;
    private readonly ObservableCollection<InstallerAssetRow> _assets;
    private readonly ObservableCollection<InstallerModRow> _mods;
    private InstallerReleaseDocument? _readMeEnglish;
    private InstallerReleaseDocument? _readMeBelarusian;

    public InstallerProfileWindow(
        InstallerProfileEditorRequest request,
        ILocalizationService localization)
    {
        InitializeComponent();
        _localization = localization;
        _suggestedDirectory = Directory.Exists(request.SuggestedDirectory)
            ? request.SuggestedDirectory
            : Environment.CurrentDirectory;
        _mode = request.Mode;
        _profile = request.Profile.Clone();
        _assets = new ObservableCollection<InstallerAssetRow>(
            _profile.Assets.Select(asset => new InstallerAssetRow(asset, localization)));
        _mods = new ObservableCollection<InstallerModRow>(
            _profile.Mods.Select(mod => new InstallerModRow(mod, localization)));
        AssetsGrid.ItemsSource = _assets;
        ModsGrid.ItemsSource = _mods;
        NameTextBox.Text = _profile.Name;
        VersionTextBox.Text = _profile.Version;
        PublisherTextBox.Text = _profile.Publisher;
        OutputNameTextBox.Text = _profile.OutputFileName;
        _readMeEnglish = _profile.ReleaseReadMeEnglish?.Clone();
        _readMeBelarusian = _profile.ReleaseReadMeBelarusian?.Clone();
        RefreshReadMeStatus();
    }

    public InstallerProfileEditorResult? Result { get; private set; }

    private void SelectMainAsi_OnClick(object sender, RoutedEventArgs e) =>
        SelectSingleBinary(
            InstallerAssetRole.MainAsi,
            InstallerProfileValidator.MainAsiDestination,
            "ASI (*.asi)|*.asi|All files (*.*)|*.*");

    private void SelectModelsArchive_OnClick(object sender, RoutedEventArgs e) =>
        SelectModelsArchive();

    private void SelectGameTxd_OnClick(object sender, RoutedEventArgs e) =>
        SelectGameTxdFiles();

    private void SelectSilentPatchFolder_OnClick(object sender, RoutedEventArgs e) =>
        SelectSilentPatchFolder();

    private void SelectEnglishReadMe_OnClick(object sender, RoutedEventArgs e) =>
        SelectReleaseReadMe(isEnglish: true);

    private void SelectBelarusianReadMe_OnClick(object sender, RoutedEventArgs e) =>
        SelectReleaseReadMe(isEnglish: false);

    private void ClearEnglishReadMe_OnClick(object sender, RoutedEventArgs e)
    {
        _readMeEnglish = null;
        RefreshReadMeStatus();
    }

    private void ClearBelarusianReadMe_OnClick(object sender, RoutedEventArgs e)
    {
        _readMeBelarusian = null;
        RefreshReadMeStatus();
    }

    private void AddMod_OnClick(object sender, RoutedEventArgs e)
    {
        var archiveDialog = new OpenFileDialog
        {
            Title = _localization.Get("Installer.Mod.SelectArchive"),
            Filter = _localization.Get("Filter.ModArchive"),
            InitialDirectory = _suggestedDirectory,
            CheckFileExists = true,
        };
        if (archiveDialog.ShowDialog(this) != true)
        {
            return;
        }

        var propertiesDialog = new InstallerModPropertiesWindow(
            Path.GetFileNameWithoutExtension(archiveDialog.FileName),
            isRequired: false,
            isNew: true,
            localization: _localization)
        {
            Owner = this,
        };
        if (propertiesDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var mod = InstallerModArchiveReader.Read(
                archiveDialog.FileName,
                propertiesDialog.ModName,
                propertiesDialog.IsRequired,
                CreateWorkingProfile(applyTextFields: false));
            _mods.Add(new InstallerModRow(mod, _localization));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or ArgumentException)
        {
            ShowException(exception);
        }
    }

    private void EditMod_OnClick(object sender, RoutedEventArgs e) => EditSelectedMod();

    private void ModsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e) => EditSelectedMod();

    private void EditSelectedMod()
    {
        if (ModsGrid.SelectedItem is not InstallerModRow selected)
        {
            return;
        }

        var dialog = new InstallerModPropertiesWindow(
            selected.Mod.Name,
            selected.Mod.IsRequired,
            isNew: false,
            localization: _localization)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var replacement = selected.Mod.Clone();
        replacement.Name = dialog.ModName;
        replacement.IsRequired = dialog.IsRequired;
        var candidate = CreateWorkingProfile(applyTextFields: false);
        var candidateIndex = candidate.Mods.FindIndex(mod => mod.Id == replacement.Id);
        candidate.Mods[candidateIndex] = replacement.Clone();
        try
        {
            InstallerProfileValidator.ValidatePayloadForStorage(candidate);
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            ShowException(exception);
            return;
        }

        var index = _mods.IndexOf(selected);
        _mods[index] = new InstallerModRow(replacement, _localization);
        ModsGrid.SelectedIndex = index;
    }

    private void RemoveMod_OnClick(object sender, RoutedEventArgs e)
    {
        if (ModsGrid.SelectedItem is InstallerModRow selected)
        {
            _mods.Remove(selected);
        }
    }

    private void SelectReleaseReadMe(bool isEnglish)
    {
        var dialog = new OpenFileDialog
        {
            Title = _localization.Get("Installer.Editor.SelectReleaseReadMeFile"),
            Filter = _localization.Get("Filter.ReleaseReadMe"),
            InitialDirectory = _suggestedDirectory,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!Path.GetExtension(dialog.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase))
        {
            ShowValidationError("Installer.Validation.ReleaseReadMeType");
            return;
        }

        var data = File.ReadAllBytes(dialog.FileName);
        if (data.Length == 0 || data.LongLength > InstallerProfileValidator.MaximumReleaseDocumentSize)
        {
            MessageBox.Show(
                this,
                _localization.Format(
                    "Installer.Validation.ReleaseReadMeSize",
                    Path.GetFileName(dialog.FileName)),
                _localization.Get("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        var document = new InstallerReleaseDocument
        {
            Id = Guid.NewGuid(),
            OriginalFileName = Path.GetFileName(dialog.FileName),
            Data = data,
        };
        if (isEnglish)
        {
            _readMeEnglish = document;
        }
        else
        {
            _readMeBelarusian = document;
        }

        RefreshReadMeStatus();
    }

    private void RefreshReadMeStatus()
    {
        EnglishReadMeStatus.Text = FormatReadMeStatus(_readMeEnglish);
        BelarusianReadMeStatus.Text = FormatReadMeStatus(_readMeBelarusian);
    }

    private string FormatReadMeStatus(InstallerReleaseDocument? document) =>
        document is null
            ? _localization.Get("Installer.Editor.ReleaseReadMeMissing")
            : _localization.Format(
                "Installer.Editor.ReleaseReadMeSelected",
                document.OriginalFileName,
                FormatSize(document.Data.LongLength));

    private static string FormatSize(long length) => length >= 1024 * 1024
        ? $"{length / 1024d / 1024d:F1} MB"
        : $"{Math.Max(1, length / 1024d):F0} KB";

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

        if (_assets.Count(row => row.Asset.Role != InstallerAssetRole.SilentPatch) +
            sources.Length + _mods.Sum(mod => mod.Mod.Files.Count) >
            InstallerProfileValidator.MaximumAssets)
        {
            ShowValidationError("Installer.Validation.AssetCount");
            return;
        }

        var retainedSize = GetStoredLengthExcludingRole(InstallerAssetRole.SilentPatch);
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
        foreach (var binary in replacement.Where(asset =>
                     InstallerProfileValidator.RequiresX86Validation(asset.DestinationPath)))
        {
            if (!InstallerProfileValidator.IsX86PeImage(binary.Data))
            {
                MessageBox.Show(
                    this,
                    _localization.Format("Installer.Validation.X86", binary.OriginalFileName),
                    _localization.Get("Common.Error"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }

        RemoveAssetsByRole(InstallerAssetRole.SilentPatch);
        foreach (var asset in replacement)
        {
            _assets.Add(new InstallerAssetRow(asset, _localization));
        }
    }

    private void SelectModelsArchive()
    {
        var dialog = new OpenFileDialog
        {
            Title = _localization.Get("Installer.Editor.SelectFile"),
            Filter = "IMG (gta3.img)|gta3.img|All files (*.*)|*.*",
            FileName = "gta3.img",
            InitialDirectory = _suggestedDirectory,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var imgPath = dialog.FileName;
        var directory = Path.GetDirectoryName(imgPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            ShowValidationError("Installer.Validation.ModelsArchive");
            return;
        }

        var dirPath = Path.Combine(directory, "gta3.dir");
        if (!File.Exists(dirPath))
        {
            ShowValidationError("Installer.Validation.ModelsArchiveMissingDir");
            return;
        }

        if (_assets.Count(row => row.Asset.Role != InstallerAssetRole.ModelsArchive) +
            2 + _mods.Sum(mod => mod.Mod.Files.Count) > InstallerProfileValidator.MaximumAssets)
        {
            ShowValidationError("Installer.Validation.AssetCount");
            return;
        }

        var retainedSize = GetStoredLengthExcludingRole(InstallerAssetRole.ModelsArchive);
        if (new FileInfo(imgPath).Length + new FileInfo(dirPath).Length >
            InstallerProfileValidator.MaximumPayloadSize - retainedSize)
        {
            ShowValidationError("Installer.Validation.PayloadSize");
            return;
        }

        RemoveAssetsByRole(InstallerAssetRole.ModelsArchive);
        AddAsset(imgPath, InstallerProfileValidator.Gta3ImgDestination, InstallerAssetRole.ModelsArchive);
        AddAsset(dirPath, InstallerProfileValidator.Gta3DirDestination, InstallerAssetRole.ModelsArchive);
    }

    private void SelectGameTxdFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = _localization.Get("Installer.Editor.SelectFiles"),
            Filter = "TXD (*.txd)|*.txd|All files (*.*)|*.*",
            InitialDirectory = _suggestedDirectory,
            CheckFileExists = true,
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != true || dialog.FileNames.Length == 0)
        {
            return;
        }

        if (dialog.FileNames.Any(path =>
                !Path.GetExtension(path).Equals(".txd", StringComparison.OrdinalIgnoreCase)))
        {
            ShowValidationError("Installer.Validation.GameTxd");
            return;
        }

        var selectedFiles = dialog.FileNames
            .Select(path => new
            {
                SourcePath = path,
                Destination = InstallerProfileValidator.GetGameTxdDestination(Path.GetFileName(path)),
            })
            .GroupBy(item => item.Destination, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

        var incomingDestinations = selectedFiles
            .Select(item => item.Destination)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var retained = _assets
            .Where(row => !incomingDestinations.Contains(row.Asset.DestinationPath))
            .ToList();
        if (retained.Count(row => row.Asset.Role == InstallerAssetRole.GameTxd) + selectedFiles.Length >
            InstallerProfileValidator.MaximumGameTxdAssets)
        {
            MessageBox.Show(
                this,
                _localization.Format(
                    "Installer.Validation.GameTxdCount",
                    InstallerProfileValidator.MaximumGameTxdAssets),
                _localization.Get("Common.Error"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (retained.Count + selectedFiles.Length + _mods.Sum(mod => mod.Mod.Files.Count) >
            InstallerProfileValidator.MaximumAssets)
        {
            ShowValidationError("Installer.Validation.AssetCount");
            return;
        }

        if (selectedFiles.Sum(item => new FileInfo(item.SourcePath).Length) >
            InstallerProfileValidator.MaximumPayloadSize -
            (retained.Sum(row => row.Asset.Data.LongLength) + GetNonAssetStoredLength()))
        {
            ShowValidationError("Installer.Validation.PayloadSize");
            return;
        }

        for (var index = _assets.Count - 1; index >= 0; index--)
        {
            if (incomingDestinations.Contains(_assets[index].Asset.DestinationPath))
            {
                _assets.RemoveAt(index);
            }
        }

        foreach (var item in selectedFiles)
        {
            AddAsset(item.SourcePath, item.Destination, InstallerAssetRole.GameTxd);
        }
    }

    private static string ToSourcePath(string root, string destination) =>
        Path.Combine(root, destination.Replace('\\', Path.DirectorySeparatorChar));

    private void AddAsset(string sourcePath, string destination, InstallerAssetRole role) =>
        _ = TryAddAsset(sourcePath, destination, role);

    private bool TryAddAsset(string sourcePath, string destination, InstallerAssetRole role)
    {
        if (_assets.Count + _mods.Sum(mod => mod.Mod.Files.Count) >= InstallerProfileValidator.MaximumAssets)
        {
            ShowValidationError("Installer.Validation.AssetCount");
            return false;
        }

        var sourceLength = new FileInfo(sourcePath).Length;
        if (sourceLength > InstallerProfileValidator.MaximumPayloadSize - GetStoredLength())
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
        var row = new InstallerAssetRow(asset, _localization);
        _assets.Add(row);
        try
        {
            InstallerProfileValidator.ValidatePayloadForStorage(CreateWorkingProfile(applyTextFields: false));
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
        {
            _assets.Remove(row);
            ShowException(exception);
            return false;
        }
    }

    private void ShowValidationError(string resourceKey) => MessageBox.Show(
        this,
        _localization.Get(resourceKey),
        _localization.Get("Common.Error"),
        MessageBoxButton.OK,
        MessageBoxImage.Error);

    private void ShowException(Exception exception) => MessageBox.Show(
        this,
        exception.Message,
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

        if (selected.Any(row => row.Asset.Role == InstallerAssetRole.ModelsArchive))
        {
            RemoveAssetsByRole(InstallerAssetRole.ModelsArchive);
        }

        foreach (var row in selected.Where(row =>
                     row.Asset.Role is not InstallerAssetRole.SilentPatch and
                     not InstallerAssetRole.ModelsArchive))
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
        var candidate = CreateWorkingProfile(applyTextFields: true);
        try
        {
            if (_mode == InstallerProfileEditorMode.ExportReleaseZip)
            {
                InstallerProfileValidator.ValidateForReleaseZip(candidate);
            }
            else if (_mode == InstallerProfileEditorMode.Export)
            {
                InstallerProfileValidator.Validate(candidate);
            }
            else
            {
                InstallerProfileValidator.ValidateForStorage(candidate);
            }
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

        Result = new InstallerProfileEditorResult(candidate.Clone());
        DialogResult = true;
    }

    private InstallerProfile CreateWorkingProfile(bool applyTextFields)
    {
        var candidate = _profile.Clone();
        if (applyTextFields)
        {
            candidate.Name = NameTextBox.Text;
            candidate.Version = VersionTextBox.Text;
            candidate.Publisher = PublisherTextBox.Text;
            candidate.OutputFileName = OutputNameTextBox.Text;
        }

        candidate.Assets = _assets.Select(row => row.Asset.Clone()).ToList();
        candidate.Mods = _mods.Select(row => row.Mod.Clone()).ToList();
        candidate.ReleaseReadMeEnglish = _readMeEnglish?.Clone();
        candidate.ReleaseReadMeBelarusian = _readMeBelarusian?.Clone();
        return candidate;
    }

    private long GetStoredLengthExcludingRole(InstallerAssetRole role) =>
        _assets.Where(row => row.Asset.Role != role).Sum(row => row.Asset.Data.LongLength) +
        GetNonAssetStoredLength();

    private long GetStoredLength() =>
        _assets.Sum(row => row.Asset.Data.LongLength) + GetNonAssetStoredLength();

    private long GetNonAssetStoredLength() =>
        _mods.Sum(mod => mod.Mod.Files.Sum(file => file.Data.LongLength)) +
        (_readMeEnglish?.Data.LongLength ?? 0) +
        (_readMeBelarusian?.Data.LongLength ?? 0);

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

        public string SizeText => FormatSize(Asset.Data.LongLength);
    }

    public sealed class InstallerModRow
    {
        private readonly ILocalizationService _localization;

        public InstallerModRow(InstallerMod mod, ILocalizationService localization)
        {
            Mod = mod;
            _localization = localization;
        }

        public InstallerMod Mod { get; }

        public string Name => Mod.Name;

        public string TypeText => _localization.Get(
            Mod.IsRequired ? "Installer.Mod.Required" : "Installer.Mod.Optional");

        public int FileCount => Mod.Files.Count;

        public string SizeText => FormatSize(Mod.Files.Sum(file => file.Data.LongLength));
    }
}
