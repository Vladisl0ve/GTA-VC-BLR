using System.IO;
using GTA_3_GXT_Editor.Utils;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.GTAIII
{
    public class GXTManager : CommonGXTManager
    {
        private const string RUSSIAN_CHARS_FILENAME = "russian_chars.txt";
        private const string BELARUSIAN_CHARS_FILENAME = "belarusian_chars.txt";

        private List<GXTBase> _gxtEntries;
        private Dictionary<int[], char> _cyrillicCharsDictionary;
        private GxtLanguage _language;

        public override GxtLanguage Language => _language;
        public override string? CyrillicCharsDictionaryPath { get; set; }
        public override List<GXTBase> GXTEntries { get => _gxtEntries; }
        public override Dictionary<int[], char> CyrillicCharsDictionary { get => _cyrillicCharsDictionary; set => _cyrillicCharsDictionary = value; }

        public GXTManager(
            string gxtPath,
            string? dictionaryPath = null,
            GxtLanguage language = GxtLanguage.Auto)
        {
            CyrillicCharsDictionaryPath = dictionaryPath;
            _gxtEntries = ReadGXTFile(gxtPath);
            var resolvedLanguage = language == GxtLanguage.Auto
                ? GxtLanguageDetector.DetectFromName(gxtPath)
                : language;
            if (resolvedLanguage == GxtLanguage.Auto)
            {
                resolvedLanguage = _gxtEntries.Any(entry => entry.Value
                    .Where((_, index) => index % 2 == 0)
                    .Any(value => value >= 0x80))
                    ? GxtLanguage.Russian
                    : GxtLanguage.English;
            }

            _cyrillicCharsDictionary = LoadCharacterDictionary(dictionaryPath, resolvedLanguage);
            _language = resolvedLanguage;
        }

        private GXTManager(
            string? dictionaryPath,
            string? sourceName,
            IEnumerable<string> sourceTexts,
            GxtLanguage language)
        {
            CyrillicCharsDictionaryPath = dictionaryPath;
            _gxtEntries = [];
            _language = language == GxtLanguage.Auto
                ? GxtLanguageDetector.DetectForText(sourceName, sourceTexts)
                : language;
            _cyrillicCharsDictionary = LoadCharacterDictionary(dictionaryPath, _language);
        }

        internal static GXTManager Create(
            string? dictionaryPath,
            string? sourceName,
            IEnumerable<string> sourceTexts,
            GxtLanguage language) =>
            new(dictionaryPath, sourceName, sourceTexts, language);

        private static Dictionary<int[], char> LoadCharacterDictionary(
            string? dictionaryPath,
            GxtLanguage language)
        {
            var path = dictionaryPath ?? Path.Combine(
                AppContext.BaseDirectory,
                language == GxtLanguage.Belarusian
                    ? BELARUSIAN_CHARS_FILENAME
                    : RUSSIAN_CHARS_FILENAME);
            return path.LoadCyrillicCharsDictionary();
        }


        public override void AddGXTEntry(string newDatName, string newDatValue, string? tableName = null)
        {
            _gxtEntries.Add(new GXTEntry { DatName = newDatName.FillWithZeros(8), Value = ConvertTextToBytes(newDatValue) });
        }

        public override void EditGXTEntry(
            string datName,
            string newDatValue,
            string? currentTableName = null,
            string? newTableName = null)
        {
            var editIndex = _gxtEntries.FindIndex(x => x.DatName.GetClearName() == datName);
            if (editIndex < 0)
            {
                throw new KeyNotFoundException($"Ключ '{datName}' не найден.");
            }

            _gxtEntries[editIndex].Value = ConvertTextToBytes(newDatValue);
        }

        public override void RemoveGXTEntry(string datName, string? tableName = null)
        {
            var removeIndex = _gxtEntries.FindIndex(x => x.DatName.GetClearName() == datName);
            if (removeIndex < 0)
            {
                throw new KeyNotFoundException($"Ключ '{datName}' не найден.");
            }

            _gxtEntries.RemoveAt(removeIndex);
        }

        public override void SaveGXTChanges(string gxtFilePath)
        {
            WriteGXTFile(gxtFilePath);
        }


        public override List<GXTBase> ReadGXTFile(string gxtFilePath)
        {
            List<GXTEntry> localGXTEntries = new List<GXTEntry>();

            using (FileStream fsStream = new FileStream(gxtFilePath, FileMode.Open, FileAccess.Read))
            {
                //TKEY
                string tKeyString = fsStream.ReadString(4);
                if (!string.Equals(tKeyString, "TKEY", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Отсутствует блок TKEY.");
                }

                //Size of TKEY
                int tKeyBlockSize = fsStream.ReadInt();
                if (tKeyBlockSize < 0 || tKeyBlockSize % 12 != 0)
                {
                    throw new InvalidDataException("Некорректный размер блока TKEY.");
                }

                //TKEY Entries
                List<KeyValuePair<int, string>> valueOffsets = new List<KeyValuePair<int, string>>();
                int readedBytes = 0;
                while (readedBytes < tKeyBlockSize)
                {
                    int tDatOffset = fsStream.ReadInt();
                    string tDatName = fsStream.ReadString(8);

                    valueOffsets.Add(new KeyValuePair<int, string>(tDatOffset, tDatName));

                    readedBytes += 12;
                }

                //TDAT
                tKeyString = fsStream.ReadString(4);
                if (!string.Equals(tKeyString, "TDAT", StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Отсутствует блок TDAT.");
                }

                //Size of TDAT
                int tDatBlockSize = fsStream.ReadInt();

                //TDAT Entries                                
                var orderedOffsets = valueOffsets.Select(x => x.Key).OrderBy(x => x).ToList();
                for (var orderedOffsetsIndex = 0; orderedOffsetsIndex < orderedOffsets.Count; orderedOffsetsIndex++)
                {
                    int readLength = 0;

                    if (orderedOffsetsIndex != orderedOffsets.Count - 1)
                    {
                        readLength = orderedOffsets[orderedOffsetsIndex + 1] - orderedOffsets[orderedOffsetsIndex];
                    }
                    else
                    {
                        readLength = (int)fsStream.Length - 16 - tKeyBlockSize - orderedOffsets[orderedOffsetsIndex];
                    }

                    fsStream.Seek(16 + tKeyBlockSize + orderedOffsets[orderedOffsetsIndex], SeekOrigin.Begin);

                    var valueName = valueOffsets.First(x => x.Key == orderedOffsets[orderedOffsetsIndex]).Value;
                    var valueBlock = fsStream.ReadBytes(readLength);

                    localGXTEntries.Add(new GXTEntry { DatName = valueName, Value = valueBlock });
                    if (!valueBlock.GXTValueIsValid())
                    {
                        throw new InvalidDataException("Обнаружено некорректное значение TDAT.");
                    }
                }
                if (tKeyBlockSize + tDatBlockSize + 16 == fsStream.Length)
                {
                    return localGXTEntries.Cast<GXTBase>().ToList();
                }
            }
            throw new InvalidDataException("Ошибка при чтении GXT-файла.");
        }

        public override void WriteGXTFile(string gxtFilePath)
        {
            _gxtEntries = _gxtEntries.OrderBy(x => x.DatName, new ASCIIStringComparer()).ToList();

            using (FileStream fsStream = new FileStream(gxtFilePath, FileMode.Create, FileAccess.Write))
            {
                fsStream.WriteString("TKEY");
                fsStream.WriteInt(12 * _gxtEntries.Count);

                var nextOffset = 0;
                for (int gtxEntryIndex = 0; gtxEntryIndex < _gxtEntries.Count; gtxEntryIndex++)
                {
                    fsStream.WriteInt(nextOffset);
                    fsStream.WriteString(_gxtEntries[gtxEntryIndex].DatName.FillWithZeros(8));

                    nextOffset += _gxtEntries[gtxEntryIndex].Value.Length;
                }

                fsStream.WriteString("TDAT");
                fsStream.WriteInt(_gxtEntries.Sum(x => x.Value.Length));

                for (int gtxEntryIndex = 0; gtxEntryIndex < _gxtEntries.Count; gtxEntryIndex++)
                {
                    fsStream.WriteBytes(_gxtEntries[gtxEntryIndex].Value);
                }
            }
        }
    }
}
