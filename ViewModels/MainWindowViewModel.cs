using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GTA_3_GXT_Editor.Utils;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const string DocumentFileFilter =
        "GXT или проект BYX (*.gxt;*.byx)|*.gxt;*.byx|GXT (*.gxt)|*.gxt|Проект BYX (*.byx)|*.byx|Все файлы (*.*)|*.*";
    private const string GxtFileFilter =
        "GTA III/Vice City GXT (*.gxt)|*.gxt|Все файлы (*.*)|*.*";
    private const string TxdFileFilter =
        "Vice City TXD (*.txd)|*.txd|Все файлы (*.*)|*.*";
    private const string ByxFileFilter =
        "Проект BYX (*.byx)|*.byx|Все файлы (*.*)|*.*";
    private const string JsonFileFilter =
        "JSON (*.json)|*.json|Все файлы (*.*)|*.*";
    private const string DictionaryFileFilter =
        "Словарь символов (*.txt)|*.txt|Все файлы (*.*)|*.*";

    private readonly GxtManagerFactory _managerFactory;
    private readonly IDialogService _dialogs;
    private readonly ITxdReader _txdReader;
    private readonly IProjectSerializer _projectSerializer;

    private CommonGXTManager? _manager;
    private EditorProject? _project;
    private GXTType _loadedType = GXTType.None;
    private readonly List<ComparisonDocument> _comparisonDocuments = [];

    public MainWindowViewModel(
        GxtManagerFactory managerFactory,
        IDialogService dialogs,
        ITxdReader? txdReader = null,
        IProjectSerializer? projectSerializer = null)
    {
        _managerFactory = managerFactory;
        _dialogs = dialogs;
        _txdReader = txdReader ?? new TxdReader();
        _projectSerializer = projectSerializer ?? new ByxProjectSerializer(managerFactory, _txdReader);

        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = FilterEntry;

        SearchColumns =
        [
            SearchColumnOption.All,
            new SearchColumnOption(SearchColumn.Name, "Ключ"),
            new SearchColumnOption(SearchColumn.Text, "Текст"),
            new SearchColumnOption(SearchColumn.Comparison, "Файлы сравнения"),
            new SearchColumnOption(SearchColumn.Table, "Таблица"),
        ];
        selectedSearchColumn = SearchColumns[0];
    }

    public ObservableCollection<GxtEntryRow> Entries { get; } = [];

    public ObservableCollection<GxtComparisonColumn> ComparisonColumns { get; } = [];

    public ObservableCollection<TxdAttachment> TxdAttachments { get; } = [];

    public ICollectionView EntriesView { get; }

    public IReadOnlyList<SearchColumnOption> SearchColumns { get; }

    public bool IsComparisonLoaded => ComparisonColumns.Count > 0;

    public bool HasTxdAttachments => TxdAttachments.Count > 0;

    public bool CanAttachTxd => IsDocumentLoaded && _loadedType == GXTType.GtaViceCity;

    [ObservableProperty]
    private string gxtPath = string.Empty;

    [ObservableProperty]
    private string projectPath = string.Empty;

    [ObservableProperty]
    private string gxtSourceName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool isProjectDirty;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private SearchColumnOption selectedSearchColumn;

    [ObservableProperty]
    private bool caseSensitive;

    [ObservableProperty]
    private bool liveSearch = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportJsonCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenComparisonFileCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddMissingEntriesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConvertDictionaryCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddTxdCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveProjectAsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportGxtCommand))]
    private bool isDocumentLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteEntryCommand))]
    private GxtEntryRow? selectedEntry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveTxdCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportTxdCommand))]
    private TxdAttachment? selectedTxd;

    [ObservableProperty]
    private string documentType = "Файл не открыт";

    [ObservableProperty]
    private string statusText = "Откройте GXT-файл GTA III или Vice City";

    public void OpenFromCommandLine(string path)
    {
        if (TryContinueAfterUnsavedChanges())
        {
            OpenDocument(path);
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (LiveSearch)
        {
            ApplyFilter();
        }
    }

    partial void OnSelectedSearchColumnChanged(SearchColumnOption value)
    {
        if (LiveSearch)
        {
            ApplyFilter();
        }
    }

    partial void OnCaseSensitiveChanged(bool value)
    {
        if (LiveSearch)
        {
            ApplyFilter();
        }
    }

    partial void OnLiveSearchChanged(bool value)
    {
        if (value)
        {
            ApplyFilter();
        }
    }

    [RelayCommand]
    private void OpenFile()
    {
        var path = _dialogs.OpenFile("Открыть GXT или проект BYX", DocumentFileFilter);
        if (path is not null && TryContinueAfterUnsavedChanges())
        {
            OpenDocument(path);
        }
    }

    [RelayCommand]
    private void OpenFileWithDictionary()
    {
        var path = _dialogs.OpenFile("Открыть GXT-файл со словарём", GxtFileFilter);
        if (path is null)
        {
            return;
        }

        var dictionaryPath = _dialogs.OpenFile("Выбрать словарь символов", DictionaryFileFilter);
        if (dictionaryPath is not null && TryContinueAfterUnsavedChanges())
        {
            LoadDocument(path, dictionaryPath);
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void OpenComparisonFile()
    {
        var paths = _dialogs.OpenFiles("Добавить GXT для сравнения", GxtFileFilter);
        if (paths.Count == 0)
        {
            return;
        }

        var loadedCount = 0;
        foreach (var path in paths)
        {
            if (TryAddComparisonFile(path))
            {
                loadedCount++;
            }
        }

        if (loadedCount == 0)
        {
            return;
        }

        OnPropertyChanged(nameof(IsComparisonLoaded));
        var selected = SelectedEntry;
        RefreshEntries(selected?.Name, selected?.RawTableName);
        StatusText = $"Загружено файлов для сравнения: {loadedCount}; всего: {ComparisonColumns.Count}";
    }

    private bool TryAddComparisonFile(string path)
    {
        try
        {
            var type = _managerFactory.DetectType(path);
            if (type != _loadedType)
            {
                _dialogs.ShowError(
                    $"Тип файла '{Path.GetFileName(path)}' не соответствует открытому GXT.");
                return false;
            }

            var comparisonManager = OpenRelatedGxt(path);
            var comparisonTexts = new Dictionary<EntryIdentity, string>();
            foreach (var entry in comparisonManager.GXTEntries)
            {
                comparisonTexts[GetEntryIdentity(entry)] = comparisonManager
                    .ConvertBytesToText(entry.Value)
                    .GetClearName();
            }

            var existingIndex = _comparisonDocuments.FindIndex(document =>
                string.Equals(document.Path, path, StringComparison.OrdinalIgnoreCase));
            var columnName = existingIndex >= 0
                ? ComparisonColumns[existingIndex].Name
                : CreateComparisonColumnName(path);
            var comparisonDocument = new ComparisonDocument(path, comparisonTexts);

            if (existingIndex >= 0)
            {
                _comparisonDocuments[existingIndex] = comparisonDocument;
                ComparisonColumns[existingIndex] = new GxtComparisonColumn(columnName, path);
            }
            else
            {
                _comparisonDocuments.Add(comparisonDocument);
                ComparisonColumns.Add(new GxtComparisonColumn(columnName, path));
            }

            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                $"Не удалось открыть '{Path.GetFileName(path)}' для сравнения.\n\n{exception.Message}",
                "Ошибка открытия");
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void Reload()
    {
        if (!TryContinueAfterUnsavedChanges())
        {
            return;
        }

        if (_project?.ProjectPath is { Length: > 0 } projectPath)
        {
            LoadProject(projectPath);
            return;
        }

        if (_project?.GxtSourcePath is not { Length: > 0 } sourcePath)
        {
            _dialogs.ShowError("Исходный GXT недоступен для перезагрузки.");
            return;
        }

        var selected = SelectedEntry;
        var dictionaryPath = _manager?.CyrillicCharsDictionaryPath;

        ReloadGxtInProject(
            sourcePath,
            dictionaryPath: dictionaryPath,
            selectedName: selected?.Name,
            selectedTable: selected?.RawTableName,
            language: _manager?.Language ?? GxtLanguage.Auto);
    }

    [RelayCommand]
    private void ApplyFilter()
    {
        EntriesView.Refresh();
        UpdateStatus();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        ApplyFilter();
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void AddEntry()
    {
        if (_manager is null)
        {
            return;
        }

        var tables = GetTableOptions();
        var result = _dialogs.EditEntry(new EntryEditorRequest(
            IsAdding: true,
            Name: string.Empty,
            Text: string.Empty,
            RawTableName: tables.Count > 0 ? tables[0].RawName : null,
            Tables: tables));

        if (result is null)
        {
            return;
        }

        if (EntryExists(result.Name, result.RawTableName))
        {
            _dialogs.ShowError($"Ключ '{result.Name}' уже существует в выбранной таблице.");
            return;
        }

        _manager.AddGXTEntry(result.Name, result.Text, result.RawTableName);
        SetDirty(true);
        RefreshEntries(result.Name, result.RawTableName);
        StatusText = $"Добавлен ключ {result.Name}";
    }

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void EditEntry()
    {
        if (_manager is null || SelectedEntry is not { } selected)
        {
            return;
        }

        var result = _dialogs.EditEntry(new EntryEditorRequest(
            IsAdding: false,
            Name: selected.Name,
            Text: selected.Text,
            RawTableName: selected.RawTableName,
            Tables: GetTableOptions()));

        if (result is null)
        {
            return;
        }

        _manager.EditGXTEntry(
            selected.Name,
            result.Text,
            selected.RawTableName,
            result.RawTableName);
        SetDirty(true);
        RefreshEntries(selected.Name, result.RawTableName);
        StatusText = $"Изменён ключ {selected.Name}";
    }

    [RelayCommand(CanExecute = nameof(CanUseSelection))]
    private void DeleteEntry()
    {
        if (_manager is null || SelectedEntry is not { } selected)
        {
            return;
        }

        if (!_dialogs.Confirm(
                $"Удалить ключ '{selected.Name}'? Это действие попадёт в файл только после сохранения.",
                "Удаление ключа"))
        {
            return;
        }

        _manager.RemoveGXTEntry(selected.Name, selected.RawTableName);
        SetDirty(true);
        RefreshEntries();
        StatusText = $"Удалён ключ {selected.Name}";
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void Save()
    {
        _ = SaveCurrentDocument();
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void ExportGxt()
    {
        _ = SaveGxtAs(markProjectSaved: false);
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void SaveProjectAs()
    {
        _ = SaveProject(forceSaveAs: true);
    }

    private bool SaveCurrentDocument()
    {
        if (_project is null || _manager is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_project.ProjectPath) || TxdAttachments.Count > 0)
        {
            return SaveProject(forceSaveAs: false);
        }

        return SaveGxtAs(markProjectSaved: true);
    }

    private bool SaveGxtAs(bool markProjectSaved)
    {
        if (_manager is null || _project is null)
        {
            return false;
        }

        var sourcePath = _project.GxtSourcePath ?? GxtPath;
        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var suggestedPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(_project.GxtSourceName)}_modified.gxt");
        var targetPath = _dialogs.SaveFile("Сохранить GXT-файл", GxtFileFilter, suggestedPath);

        if (targetPath is null)
        {
            return false;
        }

        try
        {
            _manager.SaveGXTChanges(targetPath);
            if (markProjectSaved)
            {
                _project.GxtSourcePath = targetPath;
                _project.GxtSourceName = Path.GetFileName(targetPath);
                GxtPath = targetPath;
                GxtSourceName = _project.GxtSourceName;
                SetDirty(false);
            }

            StatusText = $"Сохранено: {targetPath}";
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось сохранить файл.\n\n{exception.Message}");
            return false;
        }
    }

    private bool SaveProject(bool forceSaveAs)
    {
        if (_project is null)
        {
            return false;
        }

        var targetPath = forceSaveAs ? null : _project.ProjectPath;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var sourcePath = _project.GxtSourcePath ?? GxtPath;
            var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
            var suggestedPath = Path.Combine(
                directory,
                $"{Path.GetFileNameWithoutExtension(_project.GxtSourceName)}.byx");
            targetPath = _dialogs.SaveFile("Сохранить проект BYX", ByxFileFilter, suggestedPath);
        }

        if (targetPath is null)
        {
            return false;
        }

        try
        {
            _projectSerializer.Save(targetPath, _project);
            _project.ProjectPath = targetPath;
            ProjectPath = targetPath;
            SetDirty(false);
            StatusText = $"Проект сохранён: {targetPath}";
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось сохранить проект BYX.\n\n{exception.Message}");
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddTxd))]
    private void AddTxd()
    {
        var path = _dialogs.OpenFile("Добавить TXD GTA Vice City", TxdFileFilter);
        if (path is null)
        {
            return;
        }

        try
        {
            var data = File.ReadAllBytes(path);
            var document = _txdReader.Read(data, path);
            var existing = TxdAttachments.FirstOrDefault(attachment =>
                string.Equals(attachment.SourcePath, path, StringComparison.OrdinalIgnoreCase));
            var attachment = new TxdAttachment
            {
                Id = existing?.Id ?? Guid.NewGuid(),
                OriginalFileName = Path.GetFileName(path),
                DisplayName = existing?.DisplayName ?? CreateTxdDisplayName(path),
                SourcePath = path,
                Data = data,
                Document = document,
            };

            if (existing is null)
            {
                TxdAttachments.Add(attachment);
            }
            else
            {
                var index = TxdAttachments.IndexOf(existing);
                TxdAttachments[index] = attachment;
            }

            SelectedTxd = attachment;
            OnTxdAttachmentsChanged();
            SetDirty(true);
            StatusText = $"Подключён TXD: {attachment.OriginalFileName} — {document.Textures.Count} текстур";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось подключить TXD.\n\n{exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedTxd))]
    private void RemoveTxd()
    {
        if (SelectedTxd is not { } selected ||
            !_dialogs.Confirm($"Удалить '{selected.DisplayName}' из проекта?", "Удаление TXD"))
        {
            return;
        }

        TxdAttachments.Remove(selected);
        SelectedTxd = TxdAttachments.FirstOrDefault();
        OnTxdAttachmentsChanged();
        SetDirty(true);
        StatusText = $"TXD удалён из проекта: {selected.OriginalFileName}";
    }

    [RelayCommand(CanExecute = nameof(CanUseSelectedTxd))]
    private void ExportTxd()
    {
        if (SelectedTxd is not { } selected)
        {
            return;
        }

        var source = selected.SourcePath ?? _project?.ProjectPath ?? Environment.CurrentDirectory;
        var directory = Path.GetDirectoryName(source) ?? Environment.CurrentDirectory;
        var targetPath = _dialogs.SaveFile(
            "Экспортировать TXD",
            TxdFileFilter,
            Path.Combine(directory, selected.OriginalFileName));
        if (targetPath is null)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(targetPath, selected.Data);
            StatusText = $"TXD экспортирован: {targetPath}";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось экспортировать TXD.\n\n{exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanViewTxd))]
    private void ViewTxd()
    {
        if (_manager is null)
        {
            return;
        }

        _dialogs.ShowTxdViewer(new TxdViewerRequest(
            TxdAttachments.ToArray(),
            _manager.GetCharacterMap()));
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void ExportJson()
    {
        var sourcePath = _project?.GxtSourcePath ?? _project?.ProjectPath ?? GxtPath;
        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var sourceName = _project?.GxtSourceName ?? Path.GetFileName(GxtPath);
        var suggestedPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(sourceName)}.json");
        var targetPath = _dialogs.SaveFile(
            "Экспортировать GXT в JSON",
            JsonFileFilter,
            suggestedPath);

        if (targetPath is null)
        {
            return;
        }

        try
        {
            GxtJsonExporter.Export(
                targetPath,
                sourceName,
                DocumentType,
                Entries,
                _manager?.Language ?? GxtLanguage.Auto);
            StatusText = $"Экспортировано в JSON: {targetPath}";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось экспортировать JSON.\n\n{exception.Message}");
        }
    }

    [RelayCommand]
    private void ImportJson()
    {
        ImportJson(useCustomDictionary: false);
    }

    [RelayCommand]
    private void ImportJsonWithDictionary()
    {
        ImportJson(useCustomDictionary: true);
    }

    private void ImportJson(bool useCustomDictionary)
    {
        if (!TryContinueAfterUnsavedChanges())
        {
            return;
        }

        var sourcePath = _dialogs.OpenFile("Преобразовать JSON в GXT", JsonFileFilter);
        if (sourcePath is null)
        {
            return;
        }

        string? dictionaryPath = null;
        if (useCustomDictionary)
        {
            dictionaryPath = _dialogs.OpenFile("Выбрать словарь символов", DictionaryFileFilter);
            if (dictionaryPath is null)
            {
                return;
            }
        }

        var directory = Path.GetDirectoryName(sourcePath) ?? Environment.CurrentDirectory;
        var suggestedPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(sourcePath)}.gxt");
        var targetPath = _dialogs.SaveFile(
            "Сохранить преобразованный GXT",
            GxtFileFilter,
            suggestedPath);
        if (targetPath is null)
        {
            return;
        }

        try
        {
            var result = GxtJsonImporter.Import(sourcePath, targetPath, dictionaryPath);
            if (LoadDocument(
                    targetPath,
                    dictionaryPath: dictionaryPath,
                    language: result.Language))
            {
                var game = result.Type == GXTType.GtaIII ? "GTA III" : "GTA Vice City";
                StatusText = $"JSON преобразован в {game} GXT: {result.EntryCount} ключей";
            }
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось преобразовать JSON в GXT.\n\n{exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void AddMissingEntries()
    {
        if (_manager is null)
        {
            return;
        }

        var gameName = _loadedType == GXTType.GtaIII ? "GTA III" : "GTA Vice City";
        var path = _dialogs.OpenFile(
            $"Добавить отсутствующие ключи из {gameName}",
            $"{gameName} GXT (*.gxt)|*.gxt|Все файлы (*.*)|*.*");
        if (path is null)
        {
            return;
        }

        try
        {
            if (_managerFactory.DetectType(path) != _loadedType)
            {
                _dialogs.ShowError("Тип выбранного GXT-файла не соответствует открытому файлу.");
                return;
            }

            var sourceManager = OpenRelatedGxt(path);
            var missingEntries = sourceManager.GXTEntries
                .Except(_manager.GXTEntries, new GXTEntryEqualityComparer())
                .ToList();

            foreach (var entry in missingEntries)
            {
                var text = sourceManager.ConvertBytesToText(entry.Value);
                var table = (entry as GTAVC.GXTEntry)?.TableName;
                _manager.AddGXTEntry(entry.DatName.GetClearName(), text, table);
            }

            if (missingEntries.Count > 0)
            {
                SetDirty(true);
            }

            RefreshEntries();
            _dialogs.ShowInfo($"Добавлено отсутствующих ключей: {missingEntries.Count}.");
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось добавить ключи.\n\n{exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void ConvertDictionary()
    {
        if (_manager is null)
        {
            return;
        }

        var targetPath = _dialogs.OpenFile(
            "Выбрать целевой словарь символов",
            DictionaryFileFilter);
        if (targetPath is null)
        {
            return;
        }

        try
        {
            var sourceDictionary = _manager.CyrillicCharsDictionary;
            var targetDictionary = targetPath.LoadCyrillicCharsDictionary();

            if (sourceDictionary.Count != targetDictionary.Count ||
                !sourceDictionary.Values.ToHashSet().SetEquals(targetDictionary.Values))
            {
                _dialogs.ShowError(
                    "Словари должны содержать одинаковое количество и одинаковый набор символов.");
                return;
            }

            var targetBytesByCharacter = targetDictionary
                .ToDictionary(pair => pair.Value, pair => checked((byte)pair.Key[0]));
            var byteMap = sourceDictionary
                .SelectMany(pair => pair.Key.Select(index => new
                {
                    Source = checked((byte)index),
                    Target = targetBytesByCharacter[pair.Value],
                }))
                .ToDictionary(pair => pair.Source, pair => pair.Target);

            foreach (var entry in _manager.GXTEntries)
            {
                for (var index = 0; index < entry.Value.Length; index++)
                {
                    if (byteMap.TryGetValue(entry.Value[index], out var replacement))
                    {
                        entry.Value[index] = replacement;
                    }
                }
            }

            _manager.CyrillicCharsDictionaryPath = targetPath;
            _manager.ReloadCyrillicCharsDictionary();
            if (_project is not null)
            {
                _project.UsesCustomDictionary = true;
            }

            SetDirty(true);
            RefreshEntries();
            _dialogs.ShowInfo("Словарь символов успешно преобразован.");
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось преобразовать словарь.\n\n{exception.Message}");
        }
    }

    private bool LoadDocument(
        string path,
        string? dictionaryPath = null,
        string? selectedName = null,
        string? selectedTable = null,
        GxtLanguage language = GxtLanguage.Auto)
    {
        if (!File.Exists(path))
        {
            _dialogs.ShowError($"Файл '{path}' не существует или недоступен.");
            return false;
        }

        try
        {
            var manager = _managerFactory.Open(path, dictionaryPath, language);
            var type = _managerFactory.DetectType(path);
            var project = new EditorProject
            {
                ProjectPath = null,
                GxtSourceName = Path.GetFileName(path),
                GxtSourcePath = path,
                GameType = type,
                GxtManager = manager,
                UsesCustomDictionary = dictionaryPath is not null,
                TxdAttachments = [],
                IsDirty = false,
            };
            CommitProject(project, selectedName, selectedTable, clearTransientState: true);
            StatusText = $"Открыт {Path.GetFileName(path)} — {Entries.Count} ключей";
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                $"Не удалось открыть '{Path.GetFileName(path)}'.\n\n{exception.Message}",
                "Ошибка открытия");
            return false;
        }
    }

    private void RefreshEntries(string? selectedName = null, string? selectedTable = null)
    {
        Entries.Clear();

        if (_manager is null)
        {
            return;
        }

        foreach (var entry in _manager.GXTEntries)
        {
            var rawTable = (entry as GTAVC.GXTEntry)?.TableName;
            var identity = GetEntryIdentity(entry);
            Entries.Add(new GxtEntryRow(
                entry.DatName.GetClearName(),
                _manager.ConvertBytesToText(entry.Value).GetClearName(),
                rawTable?.GetClearName() ?? string.Empty,
                rawTable)
            {
                ComparisonTexts = _comparisonDocuments
                    .Select(document => document.Texts.GetValueOrDefault(identity))
                    .ToArray(),
            });
        }

        EntriesView.Refresh();
        SelectedEntry = Entries.FirstOrDefault(entry =>
            string.Equals(entry.Name, selectedName, StringComparison.Ordinal) &&
            string.Equals(entry.RawTableName, selectedTable, StringComparison.Ordinal));
        UpdateStatus();
    }

    private List<TableOption> GetTableOptions()
    {
        if (_loadedType != GXTType.GtaViceCity || _manager is null)
        {
            return [];
        }

        return _manager.GXTEntries
            .OfType<GTAVC.GXTEntry>()
            .Select(entry => entry.TableName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(table => table, new ASCIIStringComparer())
            .Select(table => new TableOption(table, table.GetClearName()))
            .ToList();
    }

    private bool EntryExists(string name, string? rawTableName)
    {
        return Entries.Any(entry =>
            string.Equals(entry.Name, name, StringComparison.Ordinal) &&
            string.Equals(entry.RawTableName, rawTableName, StringComparison.Ordinal));
    }

    private bool FilterEntry(object item)
    {
        if (item is not GxtEntryRow entry || string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var comparison = CaseSensitive
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        return SelectedSearchColumn.Column switch
        {
            SearchColumn.Name => entry.Name.Contains(SearchText, comparison),
            SearchColumn.Text => entry.Text.Contains(SearchText, comparison),
            SearchColumn.Comparison => entry.ComparisonTexts.Any(
                text => text?.Contains(SearchText, comparison) == true),
            SearchColumn.Table => entry.Table.Contains(SearchText, comparison),
            _ => entry.Name.Contains(SearchText, comparison) ||
                 entry.Text.Contains(SearchText, comparison) ||
                 entry.ComparisonTexts.Any(
                     text => text?.Contains(SearchText, comparison) == true) ||
                 entry.Table.Contains(SearchText, comparison),
        };
    }

    private void ClearComparisons()
    {
        _comparisonDocuments.Clear();
        ComparisonColumns.Clear();
        OnPropertyChanged(nameof(IsComparisonLoaded));
    }

    private string CreateComparisonColumnName(string path)
    {
        var normalizedName = Path.GetFileNameWithoutExtension(path).Trim();
        if (string.IsNullOrEmpty(normalizedName))
        {
            normalizedName = "GXT";
        }

        var usedNames = ComparisonColumns
            .Select(column => column.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!usedNames.Contains(normalizedName))
        {
            return normalizedName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{normalizedName} ({suffix})";
            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private void OpenDocument(string path)
    {
        switch (Path.GetExtension(path).ToLowerInvariant())
        {
            case ".byx":
                LoadProject(path);
                break;
            case ".gxt":
                LoadDocument(path);
                break;
            default:
                _dialogs.ShowError("Поддерживаются только файлы GXT и проекты BYX.");
                break;
        }
    }

    private bool LoadProject(string path)
    {
        try
        {
            var project = _projectSerializer.Load(path);
            CommitProject(project, clearTransientState: true);
            StatusText = $"Открыт проект {Path.GetFileName(path)} — {Entries.Count} ключей, " +
                         $"TXD: {TxdAttachments.Count}";
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError(
                $"Не удалось открыть проект '{Path.GetFileName(path)}'.\n\n{exception.Message}",
                "Ошибка открытия BYX");
            return false;
        }
    }

    private bool ReloadGxtInProject(
        string path,
        string? dictionaryPath,
        string? selectedName,
        string? selectedTable,
        GxtLanguage language)
    {
        try
        {
            var manager = _managerFactory.Open(path, dictionaryPath, language);
            var project = new EditorProject
            {
                ProjectPath = null,
                GxtSourceName = Path.GetFileName(path),
                GxtSourcePath = path,
                GameType = _managerFactory.DetectType(path),
                GxtManager = manager,
                UsesCustomDictionary = dictionaryPath is not null,
                TxdAttachments = [],
                IsDirty = false,
            };
            CommitProject(project, selectedName, selectedTable, clearTransientState: false);
            StatusText = $"Перезагружен {Path.GetFileName(path)}";
            return true;
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось перезагрузить GXT.\n\n{exception.Message}");
            return false;
        }
    }

    private void CommitProject(
        EditorProject project,
        string? selectedName = null,
        string? selectedTable = null,
        bool clearTransientState = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (clearTransientState)
        {
            ClearComparisons();
        }

        var attachments = project.TxdAttachments.ToArray();
        TxdAttachments.Clear();
        foreach (var attachment in attachments)
        {
            TxdAttachments.Add(attachment);
        }

        project.TxdAttachments = TxdAttachments;
        _project = project;
        _manager = project.GxtManager;
        _loadedType = project.GameType;
        GxtSourceName = project.GxtSourceName;
        GxtPath = project.GxtSourcePath ?? project.GxtSourceName;
        ProjectPath = project.ProjectPath ?? string.Empty;
        DocumentType = project.GameType == GXTType.GtaIII ? "GTA III" : "GTA Vice City";
        IsDocumentLoaded = true;
        SetDirty(project.IsDirty);
        SelectedTxd = TxdAttachments.FirstOrDefault();
        OnTxdAttachmentsChanged();
        RefreshEntries(selectedName, selectedTable);
    }

    private static EntryIdentity GetEntryIdentity(GXTBase entry) =>
        new(
            entry.DatName.GetClearName(),
            (entry as GTAVC.GXTEntry)?.TableName);

    private CommonGXTManager OpenRelatedGxt(string path)
    {
        var manager = _managerFactory.Open(
            path,
            _manager?.CyrillicCharsDictionaryPath,
            _manager?.Language ?? GxtLanguage.Auto);
        if (_project?.UsesCustomDictionary == true &&
            string.IsNullOrWhiteSpace(_manager?.CyrillicCharsDictionaryPath) &&
            _manager is not null)
        {
            manager.CyrillicCharsDictionary = _manager.CyrillicCharsDictionary.ToDictionary(
                pair => pair.Key.ToArray(),
                pair => pair.Value);
        }

        return manager;
    }

    private void UpdateStatus()
    {
        if (!IsDocumentLoaded)
        {
            return;
        }

        var visibleCount = EntriesView.Cast<object>().Count();
        StatusText = visibleCount == Entries.Count
            ? $"{DocumentType} — {Entries.Count} ключей"
            : $"Показано {visibleCount} из {Entries.Count} ключей";
    }

    private bool CanUseDocument() => IsDocumentLoaded && _manager is not null;

    private bool CanAddTxd() => CanAttachTxd;

    private bool CanUseSelectedTxd() => SelectedTxd is not null;

    private bool CanViewTxd() => TxdAttachments.Count > 0;

    private bool CanUseSelection() => CanUseDocument() && SelectedEntry is not null;

    public bool CanClose() => TryContinueAfterUnsavedChanges();

    private bool TryContinueAfterUnsavedChanges()
    {
        if (!IsProjectDirty)
        {
            return true;
        }

        return _dialogs.ConfirmUnsavedChanges() switch
        {
            UnsavedChangesChoice.Save => SaveCurrentDocument(),
            UnsavedChangesChoice.Discard => true,
            _ => false,
        };
    }

    private void SetDirty(bool value)
    {
        IsProjectDirty = value;
        if (_project is not null)
        {
            _project.IsDirty = value;
        }
    }

    private void OnTxdAttachmentsChanged()
    {
        OnPropertyChanged(nameof(HasTxdAttachments));
        OnPropertyChanged(nameof(CanAttachTxd));
        ViewTxdCommand.NotifyCanExecuteChanged();
        AddTxdCommand.NotifyCanExecuteChanged();
        RemoveTxdCommand.NotifyCanExecuteChanged();
        ExportTxdCommand.NotifyCanExecuteChanged();
    }

    private string CreateTxdDisplayName(string path)
    {
        var normalizedName = Path.GetFileNameWithoutExtension(path).Trim();
        if (string.IsNullOrEmpty(normalizedName))
        {
            normalizedName = "TXD";
        }

        var usedNames = TxdAttachments
            .Select(attachment => attachment.DisplayName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!usedNames.Contains(normalizedName))
        {
            return normalizedName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{normalizedName} ({suffix})";
            if (!usedNames.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private readonly record struct EntryIdentity(string Name, string? Table);

    private sealed record ComparisonDocument(
        string Path,
        Dictionary<EntryIdentity, string> Texts);
}
