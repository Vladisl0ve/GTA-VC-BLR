# Languages, encodings, and custom character mappings

**Language:** English · [Беларуская](languages-and-encodings.be.md)

A GXT file does not store an encoding name or Unicode text. Each displayed
character is selected by the low byte of a two-byte value, while the matching
glyph occupies a cell in a TXD font atlas. The same byte can therefore represent
different letters in different translations.

This page documents the editor's built-in language assumptions, automatic
detection, and the ways to replace the `byte ↔ character` table with a custom
profile.

## Three independent concepts

| Concept | What it controls |
| --- | --- |
| Document language | Metadata containing `auto`, `en`, `be`, `ru`, or `uk`. It selects a built-in profile and is stored in JSON/BYX. |
| Built-in encoding | A bundled or compiled `byte ↔ Unicode character` table selected from the language and detection heuristics. |
| GXT + TXD profile | The actual relationship between GXT bytes and cells in the attached `fonts.txd`. A custom profile takes priority over a built-in encoding. |

A language value does not guarantee that its letters exist in the font. In the
other direction, a custom profile may contain characters for a language that has
no value in `GxtLanguage`.

Decoding and encoding use this priority order:

1. a profile stored in BYX, applied in the TXD mapping editor, or explicitly
   selected while opening/importing;
2. a built-in profile selected by an explicit language;
3. a built-in profile selected by automatic detection.

An explicit `language` selects only a built-in encoding. If a custom mapping is
supplied at the same time, the language remains metadata and the character table
comes from the mapping.

## What is built in for each language

| Language | GTA III | GTA: Vice City |
| --- | --- | --- |
| English (`en`) | ASCII remains unchanged. There is no separate English profile: every language except Belarusian loads the Russian `russian_chars.txt` by default. | An empty Cyrillic profile: low bytes `0x01–0xFF` are read directly as `U+0001–U+00FF`, while `0x00` terminates the string. Normal game text remains ASCII. |
| Russian (`ru`) | The bundled `russian_chars.txt`: `0x80–0x9F = А–Я`, `0xA0–0xBF = а–я`. It does not contain `Ё/ё`. | A hybrid 1C layout with Latin ASCII aliases and slots in `0x80–0xAF`, compiled into the application. When writing, `Ё/ё` use the `E/e` aliases; reading those bytes back inside a Cyrillic word produces `Е/е`. |
| Belarusian (`be`) | The same embedded asset as the current Vice City layout is used. It contains `Ё/ё`, `І/і`, and `Ў/ў`. | The current table from `Assets/ViceCity/belarusian.gxtmap.json`, plus two legacy layouts used when reading older files. |
| Ukrainian (`uk`) | The language is recognized and stored as metadata, but there is no Ukrainian table: the editor falls back to the Russian `russian_chars.txt`. `Ґ/ґ`, `Є/є`, `І/і`, and `Ї/ї` require a custom mapping. | A separate table containing `Ґ/ґ`, `Є/є`, `І/і`, and `Ї/ї`, compiled into the application. |

### GTA III

`russian_chars.txt` is placed next to the application and loaded at runtime. It
can technically be replaced in the build output directory, but doing so globally
changes the default GTA III behavior for English, Russian, and Ukrainian. An
explicit custom mapping is safer for an individual file or project.

The Belarusian profile is loaded from the embedded
`Assets/ViceCity/belarusian.gxtmap.json` resource. The copy of that asset in the
output directory is not the source of the built-in profile: replacing the copy
without rebuilding the application does not alter the default Belarusian table.
A custom profile works without rebuilding.

### GTA: Vice City

The Russian, Ukrainian, and empty English profiles are defined in
`GTAVC/ViceCityTextEncodingProfile.cs` and compiled into the executable. An
external file cannot edit them, but a custom mapping can override any of them.

The current Belarusian layout uses:

- `І = 0x49`, `і = 0x69`;
- `Ў = 0x86`, `ў = 0x9D`;
- `Т = 0x91`, `т = 0xA8`;
- `Ё = 0x96`, `ё = 0xAF`.

Two older layouts are also hardcoded for read compatibility:

- the continuous Belarusian alphabet, uppercase followed by lowercase, across
  `0x80–0xBF`;
- an intermediate layout with `Т/т = T/y`, `І/і = 0x86/0x9D`, and
  `Ў/ў = 0x91/0xA8`.

These layouts exist to recognize old GXT files. To produce a file for the current
TXD, re-encode the old text with the current Belarusian preset instead of merely
saving the GXT.

> **Important:** `russian_chars_vc.txt` is still listed in the project as content
> copied to the application directory, but the current Vice City code never reads
> it. Replacing that file does not change the encoding.

## Language codes

During JSON import, the `language` field accepts these case-insensitive values:

| Result | Accepted JSON values |
| --- | --- |
| Automatic detection | missing field, empty string, `auto` |
| English | `en`, `eng`, `english` |
| Belarusian | `be`, `bel`, `belarusian`, `беларуская` |
| Russian | `ru`, `rus`, `russian`, `русский` |
| Ukrainian | `uk`, `ukr`, `ukrainian`, `українська` |

A BYX manifest accepts only the canonical values `en`, `be`, `ru`, and `uk`. An
arbitrary value such as `pl` is rejected even when a matching custom profile is
provided. For such a profile, omit `language` in JSON, use `auto`, or use one of
the supported values such as `en`.

## Automatic detection

Automatic detection does not inspect the TXD and cannot verify which glyph is
actually drawn in a cell. Its result is only a guess used to select a built-in
table. Open a nonstandard GXT with an explicit mapping.

### File names and JSON text

The shared detector first checks the file name without its extension:

1. contains `belarus` or starts with `bel` → Belarusian;
2. contains `ukrain` or starts with `ukr` → Ukrainian;
3. contains `russian` or starts with `rus` → Russian;
4. contains `american` or `english` → English.

If the name does not resolve the language, creating a GXT from JSON examines all
entry texts concatenated together:

1. no characters in the Cyrillic range `U+0400–U+04FF` → English;
2. contains `Ў/ў` → Belarusian;
3. contains any of `Ґ/ґ`, `Є/є`, or `Ї/ї` → Ukrainian;
4. contains `І/і` → Ukrainian as a historical fallback, because Belarusian and
   Ukrainian share this letter;
5. any other Cyrillic text → Russian.

An explicit supported `language` other than `auto` skips these heuristics.

### Opening a binary GTA III GXT

On a normal open, the editor:

1. tries to determine the language from the file name;
2. if the name is inconclusive, checks the low character bytes in all entries;
3. selects Russian if any byte is `>= 0x80`, otherwise English.

The Cyrillic content itself is not classified. Without a suitable file name, a
Ukrainian or Belarusian GXT may be treated as Russian. A custom mapping removes
decoding's dependence on that decision.

### Opening a binary Vice City GXT

When no explicit language is supplied, profiles are checked in the following
order. Percentages use the number of low bytes before each string terminator.

1. An empty file or fewer than 2% of bytes in `0x80–0xBF` → English. This check
   runs before the file name is examined.
2. At least eight distinct codes in `0xB0–0xBF`, with that range accounting for
   at least 1% of the text → the continuous Belarusian legacy layout.
3. For a Belarusian file name, compare the counts of ASCII aliases `I/i` and
   `T/y`: `I/i >= T/y` selects the current profile; otherwise the intermediate
   legacy profile is selected.
4. Without a Belarusian name, the current profile is selected when `I/i`, `0xA8`,
   and `0xA9` each appear in at least 0.5% of the text.
5. The intermediate Belarusian profile is selected when `T/y` account for at
   least 0.5%, while `0xA8` appears at least four times and also accounts for at
   least 0.5%.
6. After the Belarusian checks, a `russian/rus*` name selects Russian and an
   `ukrain/ukr*` name selects Ukrainian.
7. Otherwise, two scores are calculated:

   ```text
   russianScore   = count(0xA9) * 4 + count('y')
   ukrainianScore = count('i') * 2 + count('t') * 2 + count(0xAF) * 3
   ```

   Ukrainian is selected only when `ukrainianScore > russianScore`; a tie or a
   lower score selects Russian.

An `american/english` name does not override the byte threshold in step 1. A
nonstandard English GXT containing many `0x80–0xBF` codes should be opened with an
explicit mapping.

## Replacing an encoding

### Commands in the current UI

- **“Open with mapping…”** opens an existing GXT
  and completely replaces the built-in table with the selected `.gxtmap.json`,
  compact `.json`, or `.txt` file.
- **“JSON with mapping…”** recreates a GXT
  from Unicode JSON text using the selected table instead of the profile chosen
  by `language` or automatic detection. In an open GXT or BYX project the command
  replaces the GXT in place. With no document open it still creates a new `.gxt`
  file.
- For Vice City, attach `fonts.txd` and open **“Mapping…”**. The
  **“GXT + TXD mapping”** window lets you import or edit a profile and select
  **“Interpret source bytes”** or **“Re-encode current text”**. Its **“Check…”** button only validates
  the profile and returns the selected mode to the main window. Bytes or the
  active profile change only after the subsequent preview and the
  **“Apply mapping”** confirmation.
- Despite its label, **“Belarusian 0x80–0xBF”** loads the current hybrid
  `Assets/ViceCity/belarusian.gxtmap.json`, including `І = 0x49`, `Ў = 0x86`, and
  the other current slots listed above. It does not select the continuous
  `0x80–0xBF` legacy layout.
- **“Convert mapping…”** changes one byte
  layout into another without an intermediate text-editing step. The source and
  target profiles must contain the same number and exact same set of characters.

### Canonical `.gxtmap.json`

The canonical format supports multiple decoding aliases for one character and a
separate code used when writing:

```json
{
  "version": 1,
  "mappings": [
    {
      "character": "В",
      "codes": ["0x81", "0x42"],
      "preferredCode": "0x81"
    }
  ]
}
```

### Compact JSON

Use this form for simple tables with one code per character:

```json
{
  "Ą": "0x80",
  "ą": "0x81"
}
```

### Legacy text dictionary

Each line contains decimal codes separated by commas, a space, and one character.
The first code becomes the preferred code:

```text
128,66 В
129 Б
```

The file is first read as strict UTF-8. If decoding fails, Windows-1251 is used.

### Profile restrictions

- the canonical format version must be `1`;
- one entry describes exactly one Unicode BMP character; control characters and
  surrogate pairs are rejected;
- each code must be within `0x20–0xFF`;
- a character cannot be declared twice, and one code cannot belong to different
  characters;
- `codes` cannot be empty, and `preferredCode` must be present in that list;
- formatting tokens `~...~` are always read and written as service ASCII,
  independently of the profile;
- if the em dash `—` is missing from the profile, the encoder substitutes `-`;
- a profile changes only byte interpretation and must match the real TXD cells;
  the editor does not redraw or rearrange glyphs.

A profile may contain Polish, Lithuanian, or any other supported BMP characters.
That does not create a new JSON/BYX language code or extend `GxtLanguage`.

### Interpret, re-encode, and convert

| Operation | Effect on the GXT |
| --- | --- |
| Interpret source bytes | The bytes do not change. The editor displays them through the new profile. Use this when the GXT already matches the attached TXD but was decoded incorrectly. |
| Re-encode current text | The current text is decoded with the old profile and encoded with the new one. Bytes change transactionally; the operation is rejected if any character cannot be encoded. |
| Convert mapping | Bytes are replaced directly by matching characters between the source and target profiles. The character sets must match exactly. |

Review the preview before re-encoding. It reports unmapped bytes, missing
characters, and the number of affected entries and bytes.

## Profile storage

Plain GXT and TXD files do not embed `.gxtmap.json`. When exporting them
separately, save the profile alongside them and distribute all files together.
One profile applies to the `font1`, `font2`, and `pager` atlases in the attached
TXD: the code is shared, while only the glyph image differs.

BYX v4 stores a custom profile inside the project as
`mapping/characters.json`, links it to the GXT/TXD through the manifest, and
restores it the next time the project is opened.

## Sources in the code

- language list and shared heuristics: [`Common/GxtLanguage.cs`](../Common/GxtLanguage.cs);
- accepted language codes: [`Common/GxtDomainRules.cs`](../Common/GxtDomainRules.cs);
- GTA III behavior: [`GTAIII/GXTManager.cs`](../GTAIII/GXTManager.cs);
- Vice City profiles and detector: [`GTAVC/ViceCityTextEncodingProfile.cs`](../GTAVC/ViceCityTextEncodingProfile.cs);
- custom mapping loading and validation: [`Services/CharacterMapFileSerializer.cs`](../Services/CharacterMapFileSerializer.cs).
