# Vice City Belarusian font metrics: Phase A findings

This note records the verified extraction and runtime contract only. No font-metrics domain model, resolver, serializer, project persistence, or UI is implemented in Phase A.

## Repository baseline

- The inspected branch is `master` at `9a3b061e7375302c8979c5bb8a58d2ffa310a60d`, identical to `origin/master` at inspection time.
- The tracked repository contains no `BelarusianLanguage.asi`, no corresponding native plugin source, and no existing font-metrics model or data file.
- The existing untracked directory `1499462841_vcfctool/` predates this work and was not touched.
- The current application recognizes `font1`, `font2`, and `pager` as font atlases in `Models/TxdModels.cs`. Vice City glyph cells use `index = code - 0x20` in `Services/GlyphAtlasService.cs`.
- `Assets/ViceCity/belarusian.gxtmap.json` is character-map schema version 1 and is embedded and copied by `GTA GXT Editor.csproj`. In that map, Belarusian `Т` (U+0422) is `0x91` and `т` (U+0442) is `0xA8`, so their ordinary metric indices are 113 and 136 respectively.

## Canonical artifact and extraction

The latest canonical installed artifact found in the adjacent translation workspace is:

```text
D:\Development\GTA VC BLR\!ACTUAL_PIERAKŁAD\GTA_VC_BLR_Installer_Input_v1.2.21\02_Main_ASI\BelarusianLanguage.asi
```

Verified identity:

```text
file size:       26112 bytes
file SHA-256:    17D98CC31D63067EB9040A44453D2EB09C983ED19C2AFAB276A227B2FBA523A7
embedded version: BelarusianLanguage 1.2.34 [OPT-NO-STEADY-VERIFY-SLEEP25-0823]
embedded build:   cyr-te-exit16-bank22-g13-t14safe-save15-menu14-singlefont-20260823AF
```

The unversioned artifact is byte-identical to `BelarusianLanguage_v1.2.34_fontv8.asi` in the same directory. Versions 1.2.32, 1.2.33, 1.2.33 optimized, and 1.2.34 all contain the same metric-table payload, while earlier binaries contain older payloads.

The canonical table is 840 bytes: two contiguous rows of 210 little-endian `uint16` values. The file offsets are:

| ASI row | Raw start | Bytes | Entries |
| --- | ---: | ---: | ---: |
| 0 | 18526 (`0x485E`) | 420 | 210 |
| 1 | 18946 (`0x4A02`) | 420 | 210 |

SHA-256 of the exact 840-byte table slice is `50E8D5CDC3C7904875FC014CB343BDEEC0D8C9CF57C234F4B5AB53B60DABC1EA`.

For an ordinary printable byte code, the metric index is:

```text
index = code - 0x20
code  = index + 0x20
```

The 210 entries therefore cover ordinary codes `0x20..0xF1`. Heading rendering is a special case because the game remaps characters before indexing the metric row; see below.

## Exact row-to-texture contract

The physical mapping is:

| ASI row | Game array/style | TXD texture | Logical JSON position |
| --- | --- | --- | ---: |
| row 0 | `Size[0]`, style 0 (`FONT_STANDART` in the historical source) | `font2` | 0 |
| row 1 | `Size[1]`, style 1 (`FONT_BANK`) | `font1` | 1 |

This order is established by Vice City's renderer, not by comments in the stale plugin source. The archived reversed `Font.cpp` declares `Size[MAX_FONTS][210]`, loads `Sprite[0]` from `font2`, loads `Sprite[1]` from `font1`, and indexes `Size[RenderState.FontStyle]` while rendering: [Vice City Font.cpp](https://ni.4a.si/anonymous/re3/plain/src/render/Font.cpp?id=678a19ce3bfa8264dbd151baff7cabddbd65d598).

Some comments in the old `BelarusianLanguage.c` call row 0 `FONT_BANK/cursive` and row 1 `FONT_STANDARD`. Those labels conflict with the renderer's actual sprite/style indices and must not be used for persistence or preview routing. The stable contract is the numeric row and texture mapping above: **row 0 = `font2`; row 1 = `font1`**.

## Canonical 2 x 210 base metrics

These values were decoded directly from the canonical ASI at the offsets above. They are the required baseline; `Default` means these stored values before context-specific runtime writes.

### Row 0 / `font2`

```text
[
  5, 9, 9, 0, 17, 17, 23, 3, 21, 18, 0, 8, 3, 8,
  3, 0, 16, 9, 16, 16, 15, 19, 15, 14, 17, 17, 4, 4,
  0, 0, 0, 17, 19, 17, 19, 15, 21, 18, 19, 16, 21, 13,
  15, 21, 20, 28, 21, 18, 22, 17, 21, 20, 18, 18, 20, 26,
  22, 18, 18, 0, 8, 0, 9, 8, 0, 14, 11, 12, 16, 11,
  13, 13, 15, 10, 14, 15, 11, 21, 17, 10, 20, 15, 12, 12,
  16, 17, 13, 16, 13, 15, 11, 0, 0, 0, 0, 0, 20, 19,
  19, 22, 27, 15, 17, 18, 20, 26, 21, 23, 17, 22, 21, 17,
  26, 11, 26, 17, 20, 26, 18, 16, 11, 12, 13, 21, 11, 15,
  17, 12, 21, 17, 17, 15, 24, 16, 10, 22, 22, 18, 7, 9,
  16, 23, 12, 13, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 15, 0, 0,
  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
  0, 0, 11, 0, 0, 0, 0, 0, 0, 0, 0, 19, 19, 16
]
```

### Row 1 / `font1`

```text
[
  15, 7, 31, 25, 20, 23, 21, 7, 11, 10, 26, 14, 6, 12,
  6, 26, 20, 7, 20, 20, 21, 20, 20, 19, 21, 20, 8, 30,
  24, 30, 24, 19, 20, 22, 22, 21, 22, 18, 18, 22, 22, 9,
  14, 21, 18, 27, 21, 24, 22, 22, 23, 20, 19, 23, 22, 31,
  23, 23, 21, 25, 13, 30, 10, 19, 10, 17, 17, 16, 17, 17,
  11, 17, 17, 7, 7, 18, 7, 25, 17, 17, 17, 17, 11, 17,
  11, 17, 18, 25, 19, 14, 17, 28, 26, 20, 15, 15, 22, 22,
  18, 25, 33, 21, 22, 22, 23, 26, 21, 21, 22, 25, 24, 21,
  29, 14, 27, 22, 22, 30, 22, 18, 18, 16, 21, 27, 18, 19,
  18, 19, 21, 18, 18, 19, 27, 21, 17, 25, 14, 23, 18, 16,
  24, 22, 18, 16, 15, 14, 15, 16, 17, 15, 15, 15, 15, 15,
  7, 15, 15, 15, 15, 15, 13, 15, 15, 7, 15, 16, 13, 23,
  15, 15, 15, 15, 15, 15, 17, 15, 16, 24, 17, 16, 17, 15,
  15, 13, 20, 23, 15, 16, 17, 16, 24, 15, 15, 15, 23, 18,
  15, 23, 18, 23, 16, 15, 23, 15, 15, 16, 10, 9, 10, 20
]
```

## Verified runtime context behavior

The latest binary's context routine begins at VA `0x10002760`. It reads `CMenuManager.m_bMenuActive` at `+0x38` and `m_nCurrScreen` at `+0xF8`. Its effective Belarusian `Т/т` advances are:

| Preview context | Runtime condition | `font2` `Т` / `т` | `font1` `Т` / `т` | Difference from base |
| --- | --- | ---: | ---: | --- |
| `Default` | raw canonical rows | 11 / 22 | 14 / 14 | none |
| `Gameplay` | menu inactive | 11 / 22 | 13 / 14 | `font1`, code `0x91` -> 13 |
| `Subtitles` | same runtime mode as gameplay | 11 / 22 | 13 / 14 | same as `Gameplay` |
| `MainMenu` | menu active, page not 8..20 or 28 | 11 / 22 | 14 / 14 | none |
| `SaveLoad` | menu active, page 8..20 inclusive | 11 / 22 | 15 / 15 | `font1`, codes `0x91`, `0xA8` -> 15 |
| `ExitConfirmation` | menu active, page 28 | 11 / 22 | 16 / 16 | `font1`, codes `0x91`, `0xA8` -> 16 |

The disassembly writes the invariant `font2` values at row-0 indices 113 (`Т`) and 136 (`т`), plus index 135 (`ш`, code `0xA7`) as 22. It writes the contextual values to row-1 indices 113 and 136, and keeps row-1 index 198 at 18. The embedded diagnostic strings independently describe the same values: gameplay `T=13,t=14`, main menu 14, save/load 15, exit 16, heading 18, and bank 22.

### Heading is a remap, not a menu-state override

`Heading` must not be implemented as another ordinary `0x91`/`0xA8` override. Vice City's `SetFontStyle(FONT_HEADING)` selects the `FONT_BANK`/row-1 metrics and enables half-texture routing; `FindNewCharacter` then maps both Belarusian `Т` and `т` to metric index 198. The effective heading advance for either character is therefore row 1, index 198 = **18**.

Consequences for a future resolver:

- For normal contexts, resolve `index = code - 0x20`, then apply the sparse override for the selected context.
- For `Heading`, first apply the heading character remap and only then read the metric row. Treating `Heading` as `{ code: 0x91/0xA8, advance: 18 }` may reproduce width for those two characters but hides the game's actual routing and is unsafe as a general contract.
- `Gameplay` and `Subtitles` are separate useful preview labels but currently share one ASI runtime mode and the same effective metrics.
- `MainMenu` equals the canonical base for `Т/т`; it requires no sparse override if `Default` is the extracted table.

## Binary-versus-source finding

The closest native source found is:

```text
D:\Development\GTA VC BLR\!ACTUAL_PIERAKŁAD\GTA_VC_BLR_Installer_Input_v1.2.21\99_Development_Source\BelarusianLanguage.c
SHA-256: 0C6E21FBD311E575E513F4E79DB7AC81918B6672BCC7C76C48B1EDBB2B706E87
```

It declares `kBelMetrics[2][210]` at lines 141..176 and patches the two contiguous rows at line 272. It is an older 1.2.21 generation, not the source of the canonical 1.2.34 artifact:

- The old source's 420 values differ from the canonical binary in **71 slots**: 5 in row 0 and 66 in row 1.
- Row-0 differences are indices 113, 135, 136, and 198 among the relevant corrections (the fifth is index 137); for example, the stale source has row0[113] = 14 and row0[136] = 14, while the canonical binary has 11 and 22.
- The stale source's runtime comments and code use main-menu/heading advance 20 and do not contain the exit-confirmation mode. The canonical binary uses main-menu 14, heading 18, save/load 15, gameplay 13/14, and exit 16.
- The stale source's row labels also disagree with the game's renderer mapping described above.

Therefore neither `kBelMetrics` in that C file nor its symbolic row comments are an authoritative generator input. The 840-byte canonical binary slice is the verified source of truth for bootstrapping the repository data. If a newer maintained native source is later recovered, it should be checked against both the file hash and table-slice hash recorded here before replacing that provenance.

## Phase B handoff constraints

- Persist exactly two rows of 210 integer advances in logical order `font2`, then `font1`.
- Validate both outer row count and every inner length; never silently truncate, pad, or swap rows.
- Keep the canonical base rows separate from sparse context changes. The minimal ordinary overrides are one entry for gameplay/subtitles, two for save/load, and two for exit confirmation; main menu needs none relative to base.
- Model heading remapping explicitly, or document a deliberately narrower compatibility representation. It is not equivalent to a general context-only override system.
- Codes in the metrics contract are the same byte codes used by the existing character map. In particular, `Т = 0x91` and `т = 0xA8`; do not substitute Unicode scalar values or Latin `T/t`.
- Keep raw ASI offsets and disassembly addresses as provenance/verification data only. Runtime file parsing is not required for the application.
