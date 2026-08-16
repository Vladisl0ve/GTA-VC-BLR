# GTA GXT Editor

**Language:** English · [Беларуская](README.be.md)

A Windows editor for GTA III and GTA: Vice City text resources, tailored for Belarusian translation projects. The interface is available in English by default and in Belarusian.

## Documentation

This README is the main project documentation page. Detailed guides:

- Languages, encodings, and custom mappings — the language tables bundled for GTA III and Vice City, automatic detection, and replacing a character table with your own: [English](docs/languages-and-encodings.md) · [Беларуская](docs/languages-and-encodings.be.md).

## Technology

- .NET 10 LTS and C# 14;
- WPF/XAML with the system Fluent theme;
- MVVM powered by `CommunityToolkit.Mvvm` 8.4.2;
- MSTest 4.3.2 and Microsoft Testing Platform;
- nullable reference types and SDK-style .NET analyzers.

## Running the project

.NET SDK 10.0.302 or a newer patch release from the 10.0 line is required.

```powershell
dotnet restore "GTA GXT Editor.slnx"
dotnet build "GTA GXT Editor.slnx" --configuration Release
dotnet test "GTA GXT Editor.slnx" --configuration Release
dotnet run --project "GTA GXT Editor.csproj"
```

## GXT, TXD, and character mappings

At most one TXD can be attached to an open GXT. Together they form a pair with its own mapping profile between Unicode characters and codes in the `0x20–0xFF` range: a byte in the GXT selects a cell in the TXD atlas, while the profile describes which character occupies that cell.

The mapping editor displays the `font1`, `font2`, and `pager` atlases together with the code and assigned characters of every cell. It can:

- assign and clear characters and select the preferred code used for encoding;
- import canonical `.gxtmap.json`, compact JSON, and legacy text dictionaries;
- export canonical `.gxtmap.json`;
- apply the bundled Belarusian preset containing `Ё/ё`, `І/і`, and `Ў/ў`, loaded from the canonical asset;
- analyze missing characters, unmapped bytes, conflicts, and usage frequency;
- reinterpret the source bytes without changing the GXT or transactionally re-encode the current Unicode text for the TXD.

Formatting constructs such as `~...~` are always encoded as control ASCII. A code has the same meaning for every font in the attached TXD; only the glyph image changes. The editor does not alter glyph pixels or placement.

A canonical profile looks like this:

```json
{
  "version": 1,
  "mappings": [
    {
      "character": "А",
      "codes": ["0x41"],
      "preferredCode": "0x41"
    }
  ]
}
```

The single built-in source of the Belarusian mapping is
`Assets/ViceCity/belarusian.gxtmap.json`. It is embedded into the application and
also copied to the output directory for use with GXT/TXD files. The profile defines
the target layout for the Belarusian `fonts.txd`: it preserves the 1C ASCII aliases,
uses `І = 0x49` and `і = 0x69`, and reuses the Russian `И/и` slots for
`Ў = 0x86` and `ў = 0x9D`. The former `Ъ/ъ` slots are reserved for `Ё/ё`:
`Ё = 0x96`, `ё = 0xAF`.

GXT files created by earlier project versions are detected separately. The editor
can read both the old continuous `0x80–0xBF` layout and the intermediate layout with
`І = 0x86`, `Ў = 0x91`, `і = 0x9D`, and `ў = 0xA8`. To use such a file in the
game, re-encode it with the current built-in preset instead of merely saving it
unchanged.

The **Open with mapping…** and **JSON → GXT with mapping…** commands accept a
canonical `.gxtmap.json`, compact `.json` such as `{ "А": "0x80" }`, and legacy
`.txt` dictionaries. An explicitly selected file takes priority over the built-in
encoding.

When exporting a GXT or TXD separately, the application also offers to save the
`.gxtmap.json` profile. Replacing a TXD keeps the profile as an unverified draft;
removing a TXD leaves it active as the custom GXT encoding.

## BYX projects

BYX v4 is a self-contained ZIP container with a GXT, an optional single TXD, the
pair's profile, translator metadata, and SHA-256 checksums. A Vice City project may
also contain a Windows-installer profile and its binary resources under safe,
GUID-derived archive names. The combined unpacked size is limited to 512 MB and an
installer profile may contain at most 512 files.

BYX v3 projects remain readable. They open with an empty installer profile and are
written as v4 the next time they are saved. BYX v1/v2 and versions newer than v4 are
rejected.

RenderWare D3D8/D3D9 PC TXD files are supported: PAL4/PAL8, common
8/16/24/32-bit rasters, DXT1, and DXT3. TXD files for PS2, Xbox, and mobile
versions are rejected before the open project is changed.

## Windows installer export

For a GTA Vice City project with an attached TXD, choose **Export as → Windows
installer…**. To prepare and store its files before exporting, use the
**Windows installer** panel below the TXD controls and save the project as BYX.
The profile editor requires `BelarusianLanguage.asi` and a complete SilentPatch
distribution: `SilentPatchVC.asi`, `SilentPatchVC.ini`, and the eight supported
IPL files. Files already stored in BYX remain selected on later exports. ASI
Loaders and arbitrary additional files are not included.

Export is offline and creates one English/Belarusian `Setup.exe` with the bundled
Inno Setup 7.0.2 x86 compiler. The installer always writes `TEXT\BELARUS.GXT`,
`MODELS\FONTS.TXD`, and `BelarusianLanguage.asi`; SilentPatch is a setup component
that is selected by default. Original font/IPL files are mirrored under
`_BelarusianModBackup\<ProductId>`, while transactional state remains under
ProgramData. The selected directory is used verbatim and does not need to contain
`gta-vc.exe`. See
[Windows installer export](docs/windows-installer.md) for the exact contract.

## GXT features

The application reads and writes GTA III/Vice City GXT files and supports built-in
and custom character tables, searching, adding, editing, deleting, and importing
missing keys, as well as converting GXT to JSON and back.

For GTA Vice City, the bundled encounter-order metadata automatically adds mission
and game-block names, context, and progression order to entries. The canonical
layer is read-only, is not copied into BYX, and does not replace custom translator
comments. If a BYX project provides its own occurrences for an entry, they take
priority over the bundled metadata.

The table can be filtered by block type and name and switched between progression
order and the original GXT order. The first file added through **Source/comparison…**
is treated as the English source and appears in the table, inspector, and entry
editor.

Translator comments are stored in the BYX project metadata. Adding a comment to a
plain GXT makes the save command offer to create a BYX project; separate GXT export
remains available. Comment import and export use the `GXT_COMMENTS` v1 format and
include the current text to detect mismatches.

Belarusian is supported by the built-in encodings for both games, including
`Ё/ё`, `І/і`, and `Ў/ў`. The JSON field `"language": "be"` selects the Belarusian
GXT encoding without heuristic detection. A language can suggest a preset, but it
never replaces the mapping profile of a GXT + TXD pair automatically.
