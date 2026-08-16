using System.IO;
using GTA_3_GXT_Editor.Utils;
using GTA_GXT_Editor.Common;
using GTA_GXT_Editor.Contracts;
using GTA_GXT_Editor.Models;
using GTA_GXT_Editor.Services;
using GTA_GXT_Editor.Utils;

namespace GTA_GXT_Editor.GTAIII
{
    public class GXTManager : CommonGXTManager
    {
        private const string RUSSIAN_CHARS_FILENAME = "russian_chars.txt";

        private List<GXTBase> _gxtEntries;
        private CharacterMapProfile _characterMap;
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
            }
        }

        public GXTManager(
            string gxtPath,
            string? dictionaryPath = null,
            GxtLanguage language = GxtLanguage.Auto)
        {
            CharacterMapPath = dictionaryPath;
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

            _characterMap = LoadCharacterMap(dictionaryPath, resolvedLanguage);
            _language = resolvedLanguage;
        }

        internal GXTManager(
            Stream stream,
            string sourceName,
            GxtLanguage language,
            CharacterMapProfile? characterMap)
        {
            CharacterMapPath = null;
            _gxtEntries = ReadGXT(stream, sourceName);
            var resolvedLanguage = language == GxtLanguage.Auto
                ? GxtLanguageDetector.DetectFromName(sourceName)
                : language;
            if (resolvedLanguage == GxtLanguage.Auto)
            {
                resolvedLanguage = _gxtEntries.Any(entry => entry.Value
                    .Where((_, index) => index % 2 == 0)
                    .Any(value => value >= 0x80))
                    ? GxtLanguage.Russian
                    : GxtLanguage.English;
            }

            _characterMap = characterMap?.Clone() ?? LoadCharacterMap(null, resolvedLanguage);
            _language = resolvedLanguage;
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
            _characterMap = LoadCharacterMap(dictionaryPath, _language);
        }

        internal static GXTManager Create(
            string? dictionaryPath,
            string? sourceName,
            IEnumerable<string> sourceTexts,
            GxtLanguage language) =>
            new(dictionaryPath, sourceName, sourceTexts, language);

        private static CharacterMapProfile LoadCharacterMap(
            string? dictionaryPath,
            GxtLanguage language)
        {
            if (dictionaryPath is not null)
            {
                return CharacterMapFileSerializer.Load(dictionaryPath);
            }

            if (language == GxtLanguage.Belarusian)
            {
                return BundledCharacterMapProvider.BelarusianViceCity;
            }

            var path = Path.Combine(AppContext.BaseDirectory, RUSSIAN_CHARS_FILENAME);
            return CharacterMapFileSerializer.Load(path);
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
                throw new KeyNotFoundException(LocalizationProvider.Current.Format("GtaThird.KeyMissing", datName));
            }

            _gxtEntries[editIndex].Value = ConvertTextToBytes(newDatValue);
        }

        public override void RemoveGXTEntry(string datName, string? tableName = null)
        {
            var removeIndex = _gxtEntries.FindIndex(x => x.DatName.GetClearName() == datName);
            if (removeIndex < 0)
            {
                throw new KeyNotFoundException(LocalizationProvider.Current.Format("GtaThird.KeyMissing", datName));
            }

            _gxtEntries.RemoveAt(removeIndex);
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
                return ReadGxtCore(stream);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception exception) when (exception is EndOfStreamException or OverflowException or ArgumentOutOfRangeException)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("GtaThird.ReadError"), exception);
            }
        }

        private static List<GXTBase> ReadGxtCore(Stream stream)
        {
            var startPosition = stream.Position;
            var endPosition = stream.Length;
            EnsureAvailable(startPosition, endPosition, 8);

            if (!string.Equals(stream.ReadString(4), "TKEY", StringComparison.Ordinal))
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("GtaThird.TkeyMissing"));
            }

            var tKeyBlockSize = stream.ReadInt();
            if (tKeyBlockSize < 0 || tKeyBlockSize % 12 != 0)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("GtaThird.TkeySize"));
            }

            var tKeyEnd = startPosition + 8L + tKeyBlockSize;
            EnsureAvailable(tKeyEnd, endPosition, 8);
            var valueOffsets = new List<(int Offset, string Name)>(tKeyBlockSize / 12);
            while (stream.Position < tKeyEnd)
            {
                valueOffsets.Add((stream.ReadInt(), stream.ReadString(8)));
            }

            if (stream.Position != tKeyEnd ||
                !string.Equals(stream.ReadString(4), "TDAT", StringComparison.Ordinal))
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("GtaThird.TdatMissing"));
            }

            var tDatBlockSize = stream.ReadInt();
            if (tDatBlockSize < 0)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("GtaThird.TdatInvalid"));
            }

            var tDatStart = stream.Position;
            if (tDatStart + (long)tDatBlockSize != endPosition)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("GtaThird.ReadError"));
            }

            var orderedValues = ValidateOffsets(valueOffsets, tDatBlockSize);
            var entries = new List<GXTEntry>(orderedValues.Count);
            for (var index = 0; index < orderedValues.Count; index++)
            {
                var current = orderedValues[index];
                var nextOffset = index + 1 < orderedValues.Count
                    ? orderedValues[index + 1].Offset
                    : tDatBlockSize;
                var readLength = nextOffset - current.Offset;
                stream.Position = tDatStart + current.Offset;
                var value = stream.ReadBytes(readLength);
                if (!value.GXTValueIsValid())
                {
                    throw new InvalidDataException(LocalizationProvider.Current.Get("GtaThird.TdatInvalid"));
                }

                entries.Add(new GXTEntry { DatName = current.Name, Value = value });
            }

            stream.Position = endPosition;
            return entries.Cast<GXTBase>().ToList();
        }

        private static List<(int Offset, string Name)> ValidateOffsets(
            List<(int Offset, string Name)> values,
            int dataLength)
        {
            if (values.Count == 0)
            {
                if (dataLength != 0)
                {
                    throw new InvalidDataException(LocalizationProvider.Current.Get("GtaThird.TdatInvalid"));
                }

                return values;
            }

            var ordered = values.OrderBy(value => value.Offset).ToList();
            if (ordered[0].Offset != 0)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("GtaThird.TdatInvalid"));
            }

            for (var index = 0; index < ordered.Count; index++)
            {
                var offset = ordered[index].Offset;
                if (offset < 0 || offset >= dataLength ||
                    index > 0 && offset == ordered[index - 1].Offset)
                {
                    throw new InvalidDataException(LocalizationProvider.Current.Get("GtaThird.TdatInvalid"));
                }
            }

            return ordered;
        }

        private static void EnsureAvailable(long position, long end, long count)
        {
            if (position < 0 || position > end || count < 0 || position > end - count)
            {
                throw new InvalidDataException(LocalizationProvider.Current.Get("GtaThird.ReadError"));
            }
        }

        public override void WriteGXT(Stream stream)
        {
            var fsStream = stream;
            ArgumentNullException.ThrowIfNull(fsStream);
            if (!fsStream.CanWrite)
            {
                throw new ArgumentException(LocalizationProvider.Current.Get("GtaThird.WriteRequired"), nameof(stream));
            }

            var orderedEntries = _gxtEntries
                .OrderBy(entry => entry.DatName, new ASCIIStringComparer())
                .ToList();
                fsStream.WriteString("TKEY");
                fsStream.WriteInt(12 * orderedEntries.Count);

                var nextOffset = 0;
                for (int gtxEntryIndex = 0; gtxEntryIndex < orderedEntries.Count; gtxEntryIndex++)
                {
                    fsStream.WriteInt(nextOffset);
                    fsStream.WriteString(orderedEntries[gtxEntryIndex].DatName.FillWithZeros(8));

                    nextOffset += orderedEntries[gtxEntryIndex].Value.Length;
                }

                fsStream.WriteString("TDAT");
                fsStream.WriteInt(orderedEntries.Sum(entry => entry.Value.Length));

                for (int gtxEntryIndex = 0; gtxEntryIndex < orderedEntries.Count; gtxEntryIndex++)
                {
                    fsStream.WriteBytes(orderedEntries[gtxEntryIndex].Value);
                }
        }
    }
}
