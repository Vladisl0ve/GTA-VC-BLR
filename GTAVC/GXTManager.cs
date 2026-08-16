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
                throw new KeyNotFoundException(LocalizationProvider.Current.Format("GtaThird.KeyMissing", datName));
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
                throw new KeyNotFoundException(LocalizationProvider.Current.Format("GtaThird.KeyMissing", datName));
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
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanRead || !stream.CanSeek)
            {
                throw new ArgumentException(LocalizationProvider.Current.Get("GtaThird.ReadSeekRequired"), nameof(stream));
            }

            try
            {
                return ReadGxtCore(stream, sourceName);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception exception) when (exception is EndOfStreamException or OverflowException or ArgumentOutOfRangeException)
            {
                throw InvalidFile(sourceName, exception);
            }
        }

        private List<GXTBase> ReadGxtCore(Stream stream, string sourceName)
        {
            var startPosition = stream.Position;
            var endPosition = stream.Length;
            EnsureAvailable(startPosition, endPosition, 8, sourceName);
            if (!string.Equals(stream.ReadString(4), "TABL", StringComparison.Ordinal))
            {
                throw InvalidFile(sourceName);
            }

            var tablBlockSize = stream.ReadInt();
            if (tablBlockSize <= 0 || tablBlockSize % 12 != 0)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("ViceCity.TablSize"));
            }

            var tableDataStart = startPosition + 8L + tablBlockSize;
            EnsureAvailable(tableDataStart, endPosition, 1, sourceName);
            var keySets = new List<(int Offset, string Name)>(tablBlockSize / 12);
            while (stream.Position < tableDataStart)
            {
                var name = stream.ReadString(8);
                var offset = stream.ReadInt();
                keySets.Add((offset, name));
            }

            ValidateTableOffsets(keySets, tableDataStart - startPosition, endPosition - startPosition, sourceName);
            var entries = new List<GXTEntry>();
            var paddedTables = new List<string>();
            for (var tableIndex = 0; tableIndex < keySets.Count; tableIndex++)
            {
                var keySet = keySets[tableIndex];
                var tableStart = startPosition + keySet.Offset;
                var tableEnd = tableIndex + 1 < keySets.Count
                    ? startPosition + keySets[tableIndex + 1].Offset
                    : endPosition;
                stream.Position = tableStart;

                if (tableIndex > 0)
                {
                    EnsureAvailable(stream.Position, tableEnd, 8, sourceName);
                    if (!string.Equals(stream.ReadString(8), keySet.Name, StringComparison.Ordinal))
                    {
                        throw InvalidFile(sourceName);
                    }
                }

                EnsureAvailable(stream.Position, tableEnd, 8, sourceName);
                if (!string.Equals(stream.ReadString(4), "TKEY", StringComparison.Ordinal))
                {
                    throw InvalidFile(sourceName);
                }

                var tKeyBlockSize = stream.ReadInt();
                if (tKeyBlockSize < 0 || tKeyBlockSize % 12 != 0)
                {
                    throw new InvalidDataException(LocalizationProvider.Current.Get("ViceCity.TkeySize"));
                }

                var tKeyEnd = stream.Position + (long)tKeyBlockSize;
                EnsureAvailable(tKeyEnd, tableEnd, 8, sourceName);
                var valueOffsets = new List<(int Offset, string Name)>(tKeyBlockSize / 12);
                while (stream.Position < tKeyEnd)
                {
                    valueOffsets.Add((stream.ReadInt(), stream.ReadString(8)));
                }

                if (stream.Position != tKeyEnd ||
                    !string.Equals(stream.ReadString(4), "TDAT", StringComparison.Ordinal))
                {
                    throw InvalidFile(sourceName);
                }

                var tDatBlockSize = stream.ReadInt();
                if (tDatBlockSize < 0)
                {
                    throw InvalidFile(sourceName);
                }

                var tDatStart = stream.Position;
                var tDatEnd = tDatStart + (long)tDatBlockSize;
                if (tDatEnd > tableEnd)
                {
                    throw InvalidFile(sourceName);
                }

                var orderedValues = ValidateValueOffsets(valueOffsets, tDatBlockSize, sourceName);
                for (var index = 0; index < orderedValues.Count; index++)
                {
                    var current = orderedValues[index];
                    var nextOffset = index + 1 < orderedValues.Count
                        ? orderedValues[index + 1].Offset
                        : tDatBlockSize;
                    stream.Position = tDatStart + current.Offset;
                    var value = stream.ReadBytes(nextOffset - current.Offset);
                    if (!value.GXTValueIsValid())
                    {
                        throw InvalidFile(sourceName);
                    }

                    entries.Add(new GXTEntry
                    {
                        DatName = current.Name,
                        Value = value,
                        TableName = keySet.Name,
                    });
                }

                stream.Position = tDatEnd;
                var paddingLength = tableEnd - tDatEnd;
                if (paddingLength == 2)
                {
                    var padding = stream.ReadBytes(2);
                    if (padding[0] != 0 || padding[1] != 0)
                    {
                        throw InvalidFile(sourceName);
                    }

                    paddedTables.Add(keySet.Name);
                }
                else if (paddingLength != 0)
                {
                    throw InvalidFile(sourceName);
                }
            }

            _emptyBlockKeySetsList.Clear();
            _emptyBlockKeySetsList.AddRange(paddedTables);
            stream.Position = endPosition;
            return entries.Cast<GXTBase>().ToList();
        }

        private static void ValidateTableOffsets(
            List<(int Offset, string Name)> keySets,
            long firstTableOffset,
            long documentLength,
            string sourceName)
        {
            for (var index = 0; index < keySets.Count; index++)
            {
                var offset = keySets[index].Offset;
                if (offset < firstTableOffset || offset >= documentLength ||
                    index == 0 && offset != firstTableOffset ||
                    index > 0 && offset <= keySets[index - 1].Offset)
                {
                    throw InvalidFile(sourceName);
                }
            }
        }

        private static List<(int Offset, string Name)> ValidateValueOffsets(
            List<(int Offset, string Name)> values,
            int dataLength,
            string sourceName)
        {
            if (values.Count == 0)
            {
                if (dataLength != 0)
                {
                    throw InvalidFile(sourceName);
                }

                return values;
            }

            var ordered = values.OrderBy(value => value.Offset).ToList();
            if (ordered[0].Offset != 0)
            {
                throw InvalidFile(sourceName);
            }

            for (var index = 0; index < ordered.Count; index++)
            {
                var offset = ordered[index].Offset;
                if (offset < 0 || offset >= dataLength ||
                    index > 0 && offset == ordered[index - 1].Offset)
                {
                    throw InvalidFile(sourceName);
                }
            }

            return ordered;
        }

        private static void EnsureAvailable(long position, long end, long count, string sourceName)
        {
            if (position < 0 || position > end || count < 0 || position > end - count)
            {
                throw InvalidFile(sourceName);
            }
        }

        private static InvalidDataException InvalidFile(string sourceName, Exception? innerException = null) =>
            new(
                LocalizationProvider.Current.Format("ViceCity.InvalidFile", Path.GetFileName(sourceName)),
                innerException);

        public override void WriteGXT(Stream stream)
        {
            var fsStream = stream;
            ArgumentNullException.ThrowIfNull(fsStream);
            if (!fsStream.CanWrite)
            {
                throw new ArgumentException(LocalizationProvider.Current.Get("GtaThird.WriteRequired"), nameof(stream));
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
