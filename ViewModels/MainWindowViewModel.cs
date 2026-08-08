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
    private const string GxtFileFilter =
        "GTA III/Vice City GXT (*.gxt)|*.gxt|Все файлы (*.*)|*.*";
    private const string JsonFileFilter =
        "JSON (*.json)|*.json|Все файлы (*.*)|*.*";
    private const string DictionaryFileFilter =
        "Словарь символов (*.txt)|*.txt|Все файлы (*.*)|*.*";

    private readonly GxtManagerFactory _managerFactory;
    private readonly IDialogService _dialogs;

    private CommonGXTManager? _manager;
    private GXTType _loadedType = GXTType.None;
    private readonly List<ComparisonDocument> _comparisonDocuments = [];

    public MainWindowViewModel(GxtManagerFactory managerFactory, IDialogService dialogs)
    {
        _managerFactory = managerFactory;
        _dialogs = dialogs;

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

    public ICollectionView EntriesView { get; }

    public IReadOnlyList<SearchColumnOption> SearchColumns { get; }

    public bool IsComparisonLoaded => ComparisonColumns.Count > 0;

    [ObservableProperty]
    private string gxtPath = string.Empty;

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
    private bool isDocumentLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditEntryCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteEntryCommand))]
    private GxtEntryRow? selectedEntry;

    [ObservableProperty]
    private string documentType = "Файл не открыт";

    [ObservableProperty]
    private string statusText = "Откройте GXT-файл GTA III или Vice City";

    public void OpenFromCommandLine(string path)
    {
        LoadDocument(path);
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
        var path = _dialogs.OpenFile("Открыть GXT-файл", GxtFileFilter);
        if (path is not null)
        {
            LoadDocument(path);
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
        if (dictionaryPath is not null)
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

            var comparisonManager = _managerFactory.Open(path);
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
        var selected = SelectedEntry;
        var dictionaryPath = _manager?.CyrillicCharsDictionaryPath;

        LoadDocument(
            GxtPath,
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
        RefreshEntries();
        StatusText = $"Удалён ключ {selected.Name}";
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void Save()
    {
        if (_manager is null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(GxtPath) ?? Environment.CurrentDirectory;
        var suggestedPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(GxtPath)}_modified.gxt");
        var targetPath = _dialogs.SaveFile("Сохранить GXT-файл", GxtFileFilter, suggestedPath);

        if (targetPath is null)
        {
            return;
        }

        try
        {
            _manager.SaveGXTChanges(targetPath);
            StatusText = $"Сохранено: {targetPath}";
        }
        catch (Exception exception)
        {
            _dialogs.ShowError($"Не удалось сохранить файл.\n\n{exception.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanUseDocument))]
    private void ExportJson()
    {
        var directory = Path.GetDirectoryName(GxtPath) ?? Environment.CurrentDirectory;
        var suggestedPath = Path.Combine(
            directory,
            $"{Path.GetFileNameWithoutExtension(GxtPath)}.json");
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
                GxtPath,
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

            var sourceManager = _managerFactory.Open(path, _manager.CyrillicCharsDictionaryPath);
            var missingEntries = sourceManager.GXTEntries
                .Except(_manager.GXTEntries, new GXTEntryEqualityComparer())
                .ToList();

            foreach (var entry in missingEntries)
            {
                var text = sourceManager.ConvertBytesToText(entry.Value);
                var table = (entry as GTAVC.GXTEntry)?.TableName;
                _manager.AddGXTEntry(entry.DatName.GetClearName(), text, table);
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

            if (_comparisonDocuments.Count > 0 &&
                (!string.Equals(path, GxtPath, StringComparison.OrdinalIgnoreCase) ||
                 _loadedType != type))
            {
                ClearComparisons();
            }

            _manager = manager;
            _loadedType = type;
            GxtPath = path;
            DocumentType = type == GXTType.GtaIII ? "GTA III" : "GTA Vice City";
            IsDocumentLoaded = true;

            RefreshEntries(selectedName, selectedTable);
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

    private static EntryIdentity GetEntryIdentity(GXTBase entry) =>
        new(
            entry.DatName.GetClearName(),
            (entry as GTAVC.GXTEntry)?.TableName);

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

    private bool CanUseSelection() => CanUseDocument() && SelectedEntry is not null;

    private readonly record struct EntryIdentity(string Name, string? Table);

    private sealed record ComparisonDocument(
        string Path,
        Dictionary<EntryIdentity, string> Texts);
}
