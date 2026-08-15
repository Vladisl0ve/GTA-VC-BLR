using System.IO;
using GTA_3_GXT_Editor.Utils;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.GTAVC
{
    public class GXTManager : CommonGXTManager
    {
        private readonly List<string> _emptyBlockKeySetsList = new List<string>();

        private List<GXTBase> _gxtEntries;
        private CharacterMapProfile _characterMap;
        private ViceCityTextEncodingProfile? _builtInTextEncoding;
        private GxtLanguage _language;

        public override GxtLanguage Language => _language;
        public override string? CharacterMapPath { get; set; }
        public override List<GXTBase> GXTEntries { get => _gxtEntries; }
        public override CharacterMapProfile CharacterMap
        {
            get => _characterMap.Clone();
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _characterMap = value.Clone();
                _builtInTextEncoding = null;
            }
        }

        public GXTManager(
            string gxtPath,
            string? dictionaryPath = null,
            GxtLanguage language = GxtLanguage.Auto)
        {
            CharacterMapPath = dictionaryPath;
            _characterMap = new CharacterMapProfile();
            _gxtEntries = ReadGXTFile(gxtPath);

            if (CharacterMapPath == null)
            {
                _builtInTextEncoding = ViceCityTextEncodingProfile.Detect(
                    gxtPath,
                    _gxtEntries,
                    language);
                _characterMap = CharacterMapProfile.FromDictionary(
                    _builtInTextEncoding.ToCharacterDictionary());
                _language = _builtInTextEncoding.Language;
            }
            else
            {
                _characterMap = CharacterMapFileSerializer.Load(CharacterMapPath);
                _language = language == GxtLanguage.Auto
                    ? GxtLanguageDetector.DetectFromName(gxtPath)
                    : language;
            }
        }

        internal GXTManager(
            Stream stream,
            string sourceName,
            GxtLanguage language,
            CharacterMapProfile? characterMap)
        {
            CharacterMapPath = null;
            _characterMap = new CharacterMapProfile();
            _gxtEntries = ReadGXT(stream, sourceName);

            if (characterMap is null)
            {
                _builtInTextEncoding = ViceCityTextEncodingProfile.Detect(
                    sourceName,
                    _gxtEntries,
                    language);
                _characterMap = CharacterMapProfile.FromDictionary(
                    _builtInTextEncoding.ToCharacterDictionary());
                _language = _builtInTextEncoding.Language;
            }
            else
            {
                _builtInTextEncoding = null;
                _characterMap = characterMap.Clone();
                _language = language == GxtLanguage.Auto
                    ? GxtLanguageDetector.DetectFromName(sourceName)
                    : language;
            }
        }

        private GXTManager(
            string? dictionaryPath,
            string? sourceName,
            IEnumerable<string> sourceTexts,
            GxtLanguage language)
        {
            CharacterMapPath = dictionaryPath;
            _gxtEntries = [];
            _language = language == GxtLanguage.Auto
                ? GxtLanguageDetector.DetectForText(sourceName, sourceTexts)
                : language;

            if (CharacterMapPath is null)
            {
                _builtInTextEncoding = ViceCityTextEncodingProfile.DetectForText(
                    sourceName,
                    sourceTexts,
                    _language);
                _characterMap = CharacterMapProfile.FromDictionary(
                    _builtInTextEncoding.ToCharacterDictionary());
            }
            else
            {
                _characterMap = CharacterMapFileSerializer.Load(CharacterMapPath);
            }
        }

        internal static GXTManager Create(
            string? dictionaryPath,
            string? sourceName,
            IEnumerable<string> sourceTexts,
            GxtLanguage language) =>
            new(dictionaryPath, sourceName, sourceTexts, language);

        public override string ConvertBytesToText(byte[] inputBytes) =>
            _builtInTextEncoding?.Decode(inputBytes) ?? base.ConvertBytesToText(inputBytes);

        public override byte[] ConvertTextToBytes(string inputString) =>
            _builtInTextEncoding?.Encode(inputString) ?? base.ConvertTextToBytes(inputString);


        public override void AddGXTEntry(string newDatName, string newDatValue, string? tableName = null)
        {
            _gxtEntries.Add(new GXTEntry
            {
                DatName = newDatName.GetClearName().FillWithZeros(8),
                Value = ConvertTextToBytes(newDatValue),
                TableName = NormalizeTableName(tableName),
            });
        }

        public override void EditGXTEntry(
            string datName,
            string newDatValue,
            string? currentTableName = null,
            string? newTableName = null)
        {
            var editIndex = _gxtEntries.FindIndex(entry =>
                entry.DatName.GetClearName() == datName.GetClearName() &&
                entry is GXTEntry viceCityEntry &&
                viceCityEntry.TableName.GetClearName() == NormalizeTableName(currentTableName).GetClearName());
            if (editIndex < 0)
            {
                throw new KeyNotFoundException($"Ключ '{datName}' не найден.");
            }

            _gxtEntries[editIndex].Value = ConvertTextToBytes(newDatValue);
            ((GXTEntry)_gxtEntries[editIndex]).TableName = NormalizeTableName(
                newTableName ?? currentTableName);
        }

        public override void RemoveGXTEntry(string datName, string? tableName = null)
        {
            var normalizedTableName = NormalizeTableName(tableName).GetClearName();
            var removeIndex = _gxtEntries.FindIndex(entry =>
                entry.DatName.GetClearName() == datName.GetClearName() &&
                entry is GXTEntry viceCityEntry &&
                viceCityEntry.TableName.GetClearName() == normalizedTableName);
            if (removeIndex < 0)
            {
                throw new KeyNotFoundException($"Ключ '{datName}' не найден.");
            }

            _gxtEntries.RemoveAt(removeIndex);
        }

        private static string NormalizeTableName(string? tableName)
        {
            return (string.IsNullOrWhiteSpace(tableName) ? "MAIN" : tableName.GetClearName())
                .FillWithZeros(8);
        }

        public override List<GXTBase> ReadGXT(Stream stream, string sourceName)
        {
            var fsStream = stream;
            ArgumentNullException.ThrowIfNull(fsStream);
            if (!fsStream.CanRead || !fsStream.CanSeek)
            {
                throw new ArgumentException("Поток GXT должен поддерживать чтение и позиционирование.", nameof(stream));
            }

            _emptyBlockKeySetsList.Clear();
            var startPosition = fsStream.Position;
                //Читаем идентификатор блока - "TABL"
                string tablIdentifier = fsStream.ReadString(4);
                if (!string.Equals(tablIdentifier, "TABL", StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Файл '{Path.GetFileName(sourceName)}' повреждён или не является GXT файлом игры 'Grand Theft Auto: Vice City'.");
                }

                //Читаем полный размер блока "TABL"
                int tablBlockSize = fsStream.ReadInt();
                if (tablBlockSize <= 0 || tablBlockSize % 12 != 0)
                {
                    throw new InvalidDataException("Некорректный размер блока TABL.");
                }

                //Читаем наборы ключей
                List<KeyValuePair<int, string>> keySets = new List<KeyValuePair<int, string>>();
                int readedBytes = 0;
                while (readedBytes < tablBlockSize)
                {
                    //Читаем название набора ключей
                    string keySetName = fsStream.ReadString(8);

                    //Читаем cдвиг набора ключей
                    int keySetOffset = fsStream.ReadInt();

                    //Записываем в словарь для дальшейшего использования
                    keySets.Add(new KeyValuePair<int, string>(keySetOffset, keySetName));
                    readedBytes += 12;
                }

                //Проходимся по всем наборам ключей
                List<GXTEntry> localGXTEntries = new List<GXTEntry>();
                for (int keySetIndex = 0; keySetIndex < keySets.Count; keySetIndex++)
                {
                    //В некоторых GXT файлах после блока набора ключей стоят 2 пустых байта. 
                    //Зачем они там и влияют ли на работоспособность - неизвестно, но на всякий случай я их тоже записываю в список
                    //При сохранении GXT эти байты будут дописаны к соответствующим блокам
                    if (keySetIndex > 0 && fsStream.Position != keySets[keySetIndex].Key)
                    {
                        //Console.WriteLine($"Обнаружен пустой блок длиною в {keySets[keySetIndex].Key - fsStream.Position} после набора ключей '{keySets[keySetIndex - 1].Value}'.");
                        _emptyBlockKeySetsList.Add(keySets[keySetIndex - 1].Value);
                    }

                    //Двигаемся к началу блока ключей
                    fsStream.Seek(startPosition + keySets[keySetIndex].Key, SeekOrigin.Begin);

                    //Устанавливаем сдвиг для текущего набора ключей
                    var gxtEntriesShift = localGXTEntries.Count;

                    //Читаем название набора ключей
                    //Примечание: Для набора "MAIN" название в файл не записывается - он всегда идёт первым
                    string keySetName;
                    if (keySetIndex != 0)
                    {
                        keySetName = fsStream.ReadString(8);
                    }
                    else
                    {
                        keySetName = string.Empty;
                    }

                    //Читаем идентификатор блока - "TKEY"
                    string tKeyIdentifier = fsStream.ReadString(4);
                    if (!string.Equals(tKeyIdentifier, "TKEY", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"Файл '{Path.GetFileName(sourceName)}' повреждён или не является GXT файлом игры 'Grand Theft Auto: Vice City'.");
                    }

                    //Читаем полный размер блока "TKEY"
                    int tKeyBlockSize = fsStream.ReadInt();
                    if (tKeyBlockSize < 0 || tKeyBlockSize % 12 != 0)
                    {
                        throw new InvalidDataException("Некорректный размер блока TKEY.");
                    }

                    //Считываем все названия текстовых данных и их сдвиги
                    List<KeyValuePair<int, string>> valueOffsets = new List<KeyValuePair<int, string>>();
                    readedBytes = 0;
                    while (readedBytes < tKeyBlockSize)
                    {
                        //Читаем cдвиг текстовых данных
                        int tDatOffset = fsStream.ReadInt();

                        //Читаем название текстовых данных
                        string tDatName = fsStream.ReadString(8);

                        //Записываем в список для дальшейшего использования
                        valueOffsets.Add(new KeyValuePair<int, string>(tDatOffset, tDatName));

                        readedBytes += 12;
                    }

                    //Читаем идентификатор блока - "TDAT"
                    var tDatIdentifier = fsStream.ReadString(4);
                    if (!string.Equals(tDatIdentifier, "TDAT", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException($"Файл '{Path.GetFileName(sourceName)}' повреждён или не является GXT файлом игры 'Grand Theft Auto: Vice City'.");
                    }

                    //Читаем полный размер блока "TDAT"
                    int tDatBlockSize = fsStream.ReadInt();

                    //Создаём отсортированный список сдвигов текстовых данных текущего набора ключей
                    var orderedOffsets = valueOffsets.Select(x => x.Key).OrderBy(x => x).ToList();

                    //Считываем все текстовые данные
                    readedBytes = 0;
                    for (var orderedOffsetsIndex = 0; orderedOffsetsIndex < orderedOffsets.Count; orderedOffsetsIndex++)
                    {
                        //Инициализируем переменную длины считываемого блока текстовых данных
                        int readLength = 0;

                        //Высчитываем длину считываемого блока текстовых данных
                        if (orderedOffsetsIndex != orderedOffsets.Count - 1)
                        {
                            readLength = orderedOffsets[orderedOffsetsIndex + 1] - orderedOffsets[orderedOffsetsIndex];
                        }
                        else
                        {
                            readLength = tDatBlockSize - readedBytes;
                        }

                        //Высчитываем позицию, в которой находится блок текстовых данных
                        var tDatPosition = startPosition + 16 + (keySetIndex == 0 ? 0 : 8) + keySets[keySetIndex].Key + tKeyBlockSize + readedBytes;

                        //Позиция должна соответствовать текущей позиции считывания в файле
                        if (tDatPosition != fsStream.Position)
                        {
                            throw new InvalidDataException($"В файле '{Path.GetFileName(sourceName)}' обнаружена неверная последовательность данных.");
                        }

                        //Записываем в элемент блок текстовых данных
                        var valueName = valueOffsets.First(x => x.Key == orderedOffsets[orderedOffsetsIndex]).Value;
                        var valueBlock = fsStream.ReadBytes(readLength);

                        localGXTEntries.Add(new GXTEntry { DatName = valueName, Value = valueBlock, TableName = keySets[keySetIndex].Value });

                        //Проверяем правильность блока текстовых данных. Два нулевых байта должны быть строго в конце блока
                        if (!localGXTEntries.Last().Value.GXTValueIsValid())
                        {
                            throw new InvalidDataException($"Файл '{Path.GetFileName(sourceName)}' повреждён или не является GXT файлом игры 'Grand Theft Auto: Vice City'.");
                        }

                        readedBytes += readLength;
                    }
                }

                if (fsStream.Position == fsStream.Length)
                {
                    return localGXTEntries.Cast<GXTBase>().ToList();
                }
            throw new InvalidDataException("Ошибка при чтении GXT-файла.");
        }

        public override void WriteGXT(Stream stream)
        {
            var fsStream = stream;
            ArgumentNullException.ThrowIfNull(fsStream);
            if (!fsStream.CanWrite)
            {
                throw new ArgumentException("Поток GXT должен поддерживать запись.", nameof(stream));
            }

            var nextOffset = 0;
                //Получаем список названий всех наборов ключей (таблиц) и сортируем по алфавиту
                var gxtKeysSetNames = _gxtEntries
                    .OfType<GXTEntry>()
                    .Select(entry => entry.TableName)
                    .Distinct()
                    .OrderBy(name => name, new ASCIIStringComparer())
                    .ToList();

                //Перемещаем набор "MAIN" в начало
                gxtKeysSetNames.Remove("MAIN\0\0\0\0");
                gxtKeysSetNames.Insert(0, "MAIN\0\0\0\0");

                //Невозможно сразу начать формировать файл, нужно сначала сформировать блоки наборов ключей. Делаем это в памяти
                MemoryStream[] tableMemoryStreams = new MemoryStream[gxtKeysSetNames.Count];
                for (int keySetIndex = 0; keySetIndex < gxtKeysSetNames.Count; keySetIndex++)
                {
                    //Набор ключей начинается с названия набора, за исключением первого набора (он же "Main")
                    tableMemoryStreams[keySetIndex] = new MemoryStream();
                    if (keySetIndex != 0)
                    {
                        tableMemoryStreams[keySetIndex].WriteString(gxtKeysSetNames[keySetIndex]);
                    }

                    //Далее записывается идентификатор "TKEY"
                    tableMemoryStreams[keySetIndex].WriteString("TKEY");

                    //Получаем список всех элементов, относящихся к текущему набору ключей (таблице) и сортируем им по алфавиту
                    var keySetEntries = _gxtEntries
                        .OfType<GXTEntry>()
                        .Where(entry => entry.TableName == gxtKeysSetNames[keySetIndex])
                        .OrderBy(entry => entry.DatName, new ASCIIStringComparer())
                        .ToList<GXTBase>();

                    //Записываем размер блока всех названия текстовых данных и их сдвигов
                    //Сдвиг - 4 байта, Название - 8 байт. Каждая запись 12 байт
                    tableMemoryStreams[keySetIndex].WriteInt(keySetEntries.Count * 12);

                    //Записываем все все названия текстовых данных и их сдвиги
                    //Первый сдвиг нулевой, остальные увеличиваются на длину предыдущего блока текстовых данных
                    nextOffset = 0;
                    for (int keySetEntryIndex = 0; keySetEntryIndex < keySetEntries.Count; keySetEntryIndex++)
                    {
                        //Сначала записываем сдвиг
                        tableMemoryStreams[keySetIndex].WriteInt(nextOffset);

                        //Затем записываем название текстовых данных
                        tableMemoryStreams[keySetIndex].WriteString(keySetEntries[keySetEntryIndex].DatName);

                        //Увеличиваем сдвиг на длину блока текстовых данных
                        nextOffset += keySetEntries[keySetEntryIndex].Value.Length;
                    }

                    //Далее записываем идентификатор "TDAT"
                    tableMemoryStreams[keySetIndex].WriteString("TDAT");

                    //Записываем полную длину всех блоков текстовых данных
                    tableMemoryStreams[keySetIndex].WriteInt(keySetEntries.Sum(x => x.Value.Length));

                    //Записываем все блоки текстовых данных
                    for (int tableEntryIndex = 0; tableEntryIndex < keySetEntries.Count; tableEntryIndex++)
                    {
                        tableMemoryStreams[keySetIndex].WriteBytes(keySetEntries[tableEntryIndex].Value);
                    }

                    //DEBUG: Дописываем пустые блоки для соответствия оригинальному файлу
                    if (_emptyBlockKeySetsList.Contains(gxtKeysSetNames[keySetIndex]))
                    {
                        tableMemoryStreams[keySetIndex].WriteBytes(new byte[] { 0, 0 });
                    }
                }

                //Теперь можно формировать файл

                //Файл начинается с идентификатора "TABL"
                fsStream.WriteString("TABL");

                //Далее записывается полный размер блока "TABL"
                //Сдвиг набора ключей - 4 байта, Название набора ключей - 8 байт. Каждая запись 12 байт 
                fsStream.WriteInt(gxtKeysSetNames.Count * 12);

                //Первый набор ключей ("MAIN") идёт сразу за блоком "TABL"
                //То есть надо просуммировать длину идентификатора (4 байта), длину размера (4 байта) и сам полный размер блока "TABL"
                nextOffset = gxtKeysSetNames.Count * 12 + 8;
                for (int tableIndex = 0; tableIndex < gxtKeysSetNames.Count; tableIndex++)
                {
                    //Записываем название набора ключей
                    fsStream.WriteString(gxtKeysSetNames[tableIndex]);

                    //Записываем сдвиг набора ключей
                    fsStream.WriteInt(nextOffset);

                    //Следующий сдвиг смещается на всю длину текущего блока набора ключей 
                    nextOffset += (int)tableMemoryStreams[tableIndex].Length;
                }

                //И наконец далее последовательно записываются все блоки наборов ключей
                for (int tableMemoryStreamsIndex = 0; tableMemoryStreamsIndex < tableMemoryStreams.Length; tableMemoryStreamsIndex++)
                {
                    tableMemoryStreams[tableMemoryStreamsIndex].WriteTo(fsStream);
                }
        }
    }
}
