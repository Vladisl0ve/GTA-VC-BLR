# BelarusianLanguage.asi — Font Metrics and Character Mapping Contract

**Target repository:** `Vladisl0ve/GTA-VC-BLR`  
**Primary sample:** `BelarusianLanguage_v1.2.34_fontv8.asi`  
**Purpose:** implementation reference for Codex/agent work that adds **safe, static reading of generated `BelarusianLanguage.asi` files** into GTA-VC-BLR.

> This document intentionally separates facts proven from the canonical 1.2.34 binary, facts already represented by the repository, and recommendations for future ASI builds.

---

## 1. Scope

The ASI reader should be able to inspect a `BelarusianLanguage.asi` **without loading or executing it** and recover, when possible:

1. PE/ASI identity and build information.
2. Base Vice City font-advance tables.
3. Known runtime/context-specific font metric overrides.
4. Runtime routing information that affects how metric indexes are interpreted.
5. Supported `gta-vc.exe` build signatures/addresses when they can be recognized.
6. Diagnostics showing whether the extracted information matches the repository's canonical Belarusian profiles.

The reader **must not claim that an ASI contains a Unicode character mapping unless the ASI explicitly embeds such metadata**.

For the canonical 1.2.34 sample:

- the font metric data is embedded in the ASI;
- the runtime code knows byte/metric slots that it patches;
- the Unicode `character ↔ GXT byte` profile is **not a self-describing table in the ASI**;
- therefore `.gxtmap.json` remains a separate source of truth.

---

## 2. Three independent layers

Vice City text rendering must be modeled as three separate things:

| Layer | Question | Canonical repository source |
|---|---|---|
| Character mapping | Which GXT byte encodes a Unicode character? | `Assets/ViceCity/belarusian.gxtmap.json` |
| Glyph bitmap | Which pixels are drawn for that byte? | `fonts.txd` |
| Font metric / advance | How far does the pen move after the glyph? | `Assets/ViceCity/belarusian.fontmetrics.json`, and runtime ASI behavior |

Do **not** infer one layer from another.

In particular:

- transparent pixel bounds in `fonts.txd` are not the authoritative advance;
- a metric index is not automatically a Unicode character;
- a byte code can be routed to a different physical metric index by Vice City's font logic;
- the same bitmap/byte layout may be interpreted through different mappings in old translations.

---

## 3. What an `.asi` file is

In the GTA modding ecosystem, an `.asi` plugin is conventionally a native library loaded into the game process by an ASI loader.

For the supplied canonical sample, the file is specifically:

- Windows **PE32**;
- x86 / Intel i386;
- marked as a **DLL**;
- five PE sections;
- entry point RVA `0x1000`;
- preferred image base `0x10000000`;
- no normal PE export table;
- no normal PE import table.

The absence of an import table is valid for this sample. The plugin resolves required WinAPI functions itself at runtime. A reader must therefore **not reject an ASI simply because `IMAGE_DIRECTORY_ENTRY_IMPORT` is empty**.

### Safety rule

The editor/importer must parse the file as bytes only.

**Never call `LoadLibrary`, never execute `DllMain`, never start the game to inspect the file, and never run code from the ASI.**

---

## 4. Canonical 1.2.34 sample fingerprint

These values describe the supplied sample exactly and are useful as a golden test fixture.

| Property | Value |
|---|---|
| File size | `26112` bytes |
| SHA-256 | `17D98CC31D63067EB9040A44453D2EB09C983ED19C2AFAB276A227B2FBA523A7` |
| Format | PE32 DLL, x86 |
| PE sections | 5 |
| Build marker | `BelarusianLanguage 1.2.34 [OPT-NO-STEADY-VERIFY-SLEEP25-0823]` |
| Base metric payload raw offset | `0x485E` |
| Base metric payload RVA | `0x605E` |
| Base metric payload size | `840` bytes |
| Base metric payload SHA-256 | `50E8D5CDC3C7904875FC014CB343BDEEC0D8C9CF57C234F4B5AB53B60DABC1EA` |
| Sparse `.mspc` pair table raw offset | `0x6470` |
| Sparse pair count | `63` |
| Sparse pair payload size | `252` bytes |
| Sparse pair SHA-256 | `76042A424B19D4DAA528286C85BD0239A432863B319BC46BC5C7DC3B2529EE34` |

### Important compatibility rule

`0x485E` and `0x6470` are **properties of this exact build**, not a file-format specification.

A general ASI reader must convert between RVA and raw file offsets using the PE section table and should prefer structural identification over fixed offsets.

---

## 5. PE layout of the canonical sample

The sample contains these sections:

| Section | RVA | Raw file offset | Raw size / role |
|---|---:|---:|---|
| `.text` | `0x1000` | `0x0400` | main executable code |
| `.rdata` | `0x6000` | `0x4800` | read-only constants, build string, base metrics |
| `.data` | `0x8000` | `0x5A00` | writable globals |
| `.reloc` | `0xA000` | `0x5C00` | PE relocations |
| `.mspc` | `0xB000` | `0x6200` | additional executable/hook code and sparse metric patch data |

Approximate raw layout:

```text
0x0000 ─────────────────────────────────────────
       DOS header / PE headers
0x0400 ───────────────────────────────────────── .text
       main ASI code
0x4800 ───────────────────────────────────────── .rdata
       build marker
0x485D build-marker NUL terminator
0x485E base font metrics: 840 bytes
       other read-only strings/data
0x5A00 ───────────────────────────────────────── .data
0x5C00 ───────────────────────────────────────── .reloc
0x6200 ───────────────────────────────────────── .mspc
       injected/runtime hook code
0x6470 sparse metric-index/advance pairs
       ...
0x6600 end of file
```

The base metric table happens to start immediately after the build marker's NUL in this build. Treat that adjacency as a **useful signature**, not as a permanent ABI.

---

## 6. Base font metric format

The canonical ASI embeds:

```text
2 rows × 210 values × uint16 little-endian = 840 bytes
```

Physical order:

```text
row 0 = font2
row 1 = font1
```

Equivalent C shape:

```c
uint16_t Size[2][210];
```

Equivalent repository concepts:

```text
font2.advances[210]
font1.advances[210]
```

### Byte-code/index relationship

For ordinary Vice City metric slots:

```text
metricIndex = code - 0x20
code        = metricIndex + 0x20

valid ordinary code range = 0x20..0xF1
metric indexes             = 0..209
```

Example:

```text
code 0x91 -> metricIndex 0x91 - 0x20 = 113
code 0xA8 -> metricIndex 0xA8 - 0x20 = 136
```

### Physical row semantics

- **Font2 / row 0** is used by the Bank font style.
- **Font1 / row 1** is used by Standard.
- Heading also uses font1, but Vice City's `FindNewCharacter`-style routing changes which physical metric slot is used for some characters.

This is why a physical metric index cannot always be interpreted as `code = index + 0x20` at the semantic/UI level.

---

## 7. Canonical base payload

The repository already stores the full two-row payload in:

```text
Assets/ViceCity/belarusian.fontmetrics.json
```

That JSON should remain the human-editable source of truth.

The existing repository verification script proves that the canonical ASI contains the same 840-byte payload exactly once and that the bytes occur in physical order `font2`, then `font1`.

For an importer, the desired result is not a raw byte dump; it is a `FontMetricsProfile` equivalent to the existing JSON model.

### Selected canonical values useful for tests

```text
Font1:
  code 0x91 -> metric index 113 -> base advance 14
  code 0xA8 -> metric index 136 -> base advance 14

Heading effective routed slot:
  physical Font1 metric index 198 -> advance 18

Bank-related physical slots used by the current routing:
  Font2 metric indexes 135 and 136 -> advance 22
```

The last two cases illustrate why **routing and mapping must remain distinct**.

---

## 8. Context-dependent T/t spacing

The 1.2.34 build marker describes the intended behavior:

```text
cyr-te-exit16-bank22-g13-t14safe-save15-menu14-singlefont-20260823AF
```

Runtime log strings in the binary describe the same policy:

```text
gameplay/subtitles -> standard T=13, t=14, heading 18
main menu          -> standard T/t 14, heading 18
save/load pages    -> standard T/t 15, heading 18
exit confirmation  -> standard T/t 16, heading 18
```

The repository's semantic `.fontmetrics.json` expresses the non-default overrides as:

| Context | Font | GXT code | Meaning under current map | Effective advance |
|---|---|---:|---|---:|
| Gameplay | Font1 | `0x91` | `Т` | 13 |
| Subtitles | Font1 | `0x91` | `Т` | 13 |
| SaveLoad | Font1 | `0x91` | `Т` | 15 |
| SaveLoad | Font1 | `0xA8` | `т` | 15 |
| ExitConfirmation | Font1 | `0x91` | `Т` | 16 |
| ExitConfirmation | Font1 | `0xA8` | `т` | 16 |
| Heading | Font1 | `0x91` | effective `Т` preview | 18 |
| Heading | Font1 | `0xA8` | effective `т` preview | 18 |

### Why MainMenu is not a sparse override

The base Font1 values for the current `Т/т` slots are already `14`, so MainMenu can use the base table.

### Why gameplay has only a `Т=13` override

Lowercase `т` remains at the base advance `14`. Therefore the effective pair is `13/14`.

### Why Heading needs special treatment

The Heading entries in `.fontmetrics.json` represent **effective preview values**.

They must not be implemented by blindly writing to the raw `0x91/0xA8` metric indexes during heading rendering. The native integration contract explicitly requires preserving Vice City's heading remap, including the known physical font1 metric index `198`.

---

## 9. Bank style and the value 22

The binary status text says:

```text
bank 22
```

The canonical ASI also writes `22` into relevant physical Font2 slots used by the Bank routing.

Do not encode this as a rule such as:

```text
"character X always has bank width 22"
```

unless the complete font-style character routing proves which semantic character reaches that physical slot.

Correct model:

```text
character byte
    -> Vice City font-style / FindNewCharacter routing
    -> physical row + metric index
    -> advance
```

This also applies to Heading.

---

## 10. Character mapping is external to the ASI

The current canonical Belarusian mapping in the repository is:

```text
Assets/ViceCity/belarusian.gxtmap.json
```

It is not safe to reconstruct that Unicode map from metric values or from physical TXD cells.

### Current mapping

#### Uppercase

| Character | Codes | Preferred |
|---|---|---|
| А | `41` | `41` |
| Б | `80` | `80` |
| В | `81`, alias `42` | `81` |
| Г | `82` | `82` |
| Д | `83` | `83` |
| Е | `45` | `45` |
| Ё | `96` | `96` |
| Ж | `84` | `84` |
| З | `85` | `85` |
| І | `49` | `49` |
| Й | `87` | `87` |
| К | `4B` | `4B` |
| Л | `88` | `88` |
| М | `89`, alias `4D` | `89` |
| Н | `8A`, alias `48` | `8A` |
| О | `4F` | `4F` |
| П | `8B` | `8B` |
| Р | `50` | `50` |
| С | `43` | `43` |
| Т | `91` | `91` |
| У | `8C` | `8C` |
| Ў | `86` | `86` |
| Ф | `8D` | `8D` |
| Х | `58` | `58` |
| Ц | `8E` | `8E` |
| Ч | `8F` | `8F` |
| Ш | `90` | `90` |
| Ы | `92` | `92` |
| Ь | `93` | `93` |
| Э | `94` | `94` |
| Ю | `95` | `95` |
| Я | `AD` | `AD` |

#### Lowercase

| Character | Code |
|---|---:|
| а | `61` |
| б | `97` |
| в | `98` |
| г | `99` |
| д | `9A` |
| е | `65` |
| ё | `AF` |
| ж | `9B` |
| з | `9C` |
| і | `69` |
| й | `9E` |
| к | `6B` |
| л | `9F` |
| м | `A0` |
| н | `A1` |
| о | `6F` |
| п | `A2` |
| р | `70` |
| с | `63` |
| т | `A8` |
| у | `A3` |
| ў | `9D` |
| ф | `A4` |
| х | `78` |
| ц | `A5` |
| ч | `A6` |
| ш | `A7` |
| ы | `A9` |
| ь | `AA` |
| э | `AB` |
| ю | `AC` |
| я | `AE` |

All table values above are hexadecimal byte codes.

### Aliases

The current profile supports decoding aliases for visually compatible Latin ASCII glyphs:

```text
В: 0x81, alias 0x42 ('B'); preferred encoding 0x81
М: 0x89, alias 0x4D ('M'); preferred encoding 0x89
Н: 0x8A, alias 0x48 ('H'); preferred encoding 0x8A
```

Decoding may accept aliases. Encoding should use `preferredCode`.

---

## 11. Legacy Belarusian T/y mapping must not be confused with the current map

An older/intermediate Belarusian profile used:

```text
Т = 0x54 ('T')
т = 0x79 ('y')
І/і = 0x86/0x9D
Ў/ў = 0x91/0xA8
```

The current profile instead uses:

```text
І/і = 0x49/0x69
Ў/ў = 0x86/0x9D
Т/т = 0x91/0xA8
```

This matters directly for ASI interpretation:

- in the current 1.2.34 build, runtime `0x91/0xA8` T/t spacing behavior belongs to **current `Т/т`**;
- in an old map, the same bytes could mean different letters;
- therefore **an ASI reader must never label a byte as a Unicode character until a compatible mapping profile is known**.

Recommended result model:

```text
RawMetricTarget:
  row
  physicalMetricIndex
  byteCode?          // only when direct code mapping is valid
  semanticCharacter? // only when a companion mapping + routing proves it
```

---

## 12. `.mspc` sparse enforcement table

The canonical sample has a second metric-related data structure in `.mspc`.

At raw offset `0x6470` there are **63 pairs**:

```c
struct SparseMetricPair
{
    uint16_t metricIndex;
    uint16_t advance;
};
```

Total:

```text
63 × 4 = 252 bytes
```

The runtime loop applies these entries to **Font1 / physical row 1**.

Canonical pair list:

```text
 93:19, 144:15, 145:14, 146:15, 147:16, 148:17, 149:15,
150:15, 151:15, 152:15, 153:15, 154:7,  155:15, 156:15,
157:15, 158:15, 159:15, 160:13, 161:15, 162:15, 163:7,
164:15, 165:16, 166:13, 167:23, 168:15, 169:15, 170:15,
171:15, 172:15, 173:15, 174:17, 175:15, 176:16, 177:24,
178:17, 179:16, 180:17, 181:15, 182:15, 183:13, 184:20,
185:23, 186:15, 187:16, 188:17, 189:16, 190:24, 191:15,
192:15, 193:15, 194:23, 195:18, 196:15, 197:23, 198:18,
199:23, 200:16, 201:15, 202:23, 203:15, 204:15, 205:16
```

### Interpretation rule

These are **physical metric indexes**, not GXT byte codes and not Unicode characters.

Do not apply:

```text
unicode = mapping[index + 0x20]
```

unless the runtime routing for the active font style proves that this is the correct semantic path.

This table is primarily useful as:

1. a forensic signature for this ASI family;
2. evidence of runtime metric enforcement;
3. an additional consistency check against the base Font1 row/runtime routing.

It should not replace the semantic `.fontmetrics.json` model.

---

## 13. Runtime executable detection in the sample

The `.mspc` code identifies two known `gta-vc.exe` layouts using PE fields from the game executable.

### Known targets found in 1.2.34

| Game build | PE TimeDateStamp | SizeOfImage | Font metrics base RVA | Font/details state RVA | Hook RVA |
|---|---:|---:|---:|---:|---:|
| Steam-supported layout | `0x504D6947` | `0x614000` | `0x296BD8` | `0x57F820` | `0x14FFE0` |
| Retail 1.0-supported layout | `0x48982736` | `0x696000` | `0x295BE0` | `0x57E828` | `0x14FED0` |

The ASI computes addresses as:

```text
runtimeAddress = gtaVcImageBase + RVA
```

The hook code temporarily changes page protection and writes a 5-byte relative `JMP`.

### Reader implication

These signatures may be exposed as diagnostic/build-support metadata, but they are runtime implementation details, not part of the font profile.

A future ASI may support more executables or resolve addresses differently.

---

## 14. Existing repository contract

GTA-VC-BLR already has most of the semantic destination model.

Important files:

```text
Assets/ViceCity/belarusian.gxtmap.json
Assets/ViceCity/belarusian.fontmetrics.json

docs/languages-and-encodings.md

native/BelarusianFontMetricsIntegration.h
native/generated/BelarusianFontMetrics.generated.h

scripts/generate-belarusian-font-metrics.ps1
scripts/verify-belarusian-font-metrics-asi.ps1
```

### Existing canonical verifier

`scripts/verify-belarusian-font-metrics-asi.ps1` already verifies the exact 1.2.34 sample by:

- expected file length;
- exact ASI SHA-256;
- base-table offset `0x485E`;
- `2 × 210 × uint16` payload length;
- payload SHA-256;
- equality with `belarusian.fontmetrics.json`;
- unique occurrence of the base payload in the ASI.

The new reader should **generalize this forensic knowledge**, not create a second unrelated interpretation of the binary.

---

## 15. Recommended ASI reader architecture

Suggested stages:

### Stage A — PE validation

Parse statically:

1. DOS `MZ` header.
2. `e_lfanew`.
3. `PE\0\0` signature.
4. COFF header.
5. PE32 optional header.
6. section table.

Validate all arithmetic with checked/64-bit calculations before slicing byte arrays.

Require/record:

```text
Machine
Characteristics
AddressOfEntryPoint
ImageBase
SizeOfImage
sections:
  name
  VirtualAddress
  VirtualSize
  PointerToRawData
  SizeOfRawData
  Characteristics
```

Do not require an import/export table.

### Stage B — identify BelarusianLanguage family

Search read-only data for a NUL-terminated ASCII marker:

```text
BelarusianLanguage <version> [...]
```

Return:

```text
ProductName
Version
BuildTag
MarkerRva
MarkerRawOffset
```

Do not treat arbitrary matching strings outside valid PE sections as sufficient proof.

### Stage C — extract base metric rows

#### Exact canonical mode

If file SHA-256 equals the canonical 1.2.34 hash:

- read `840` bytes at `0x485E`;
- verify the canonical payload SHA-256;
- decode `420` little-endian `uint16` values;
- split as 210 Font2 + 210 Font1.

#### Structural mode for same ASI family

Do **not** hardcode `0x485E`.

Possible recognizers, from strongest to weakest:

1. future self-describing metadata block — preferred;
2. code/data references that identify a 840-byte `2×210` table;
3. table immediately following a recognized build marker, if validated by references and shape;
4. known binary-signature patterns for a specific ASI generation family.

All structural matches must be bounds-checked and ambiguity must be reported.

Never silently choose the first of multiple candidates.

### Stage D — extract runtime sparse data

For this family, recognize the `.mspc` loop/table only if its surrounding code signature is known.

Validate:

```text
pair count is reasonable
every metricIndex < 210
pair table stays inside its containing section/file
```

Store physical indexes as physical indexes.

Do not manufacture semantic context names from numeric indexes alone.

### Stage E — derive semantic context overrides

Preferred order:

1. explicit metadata in future ASI;
2. exact known build adapter;
3. known, versioned binary signature/CFG recognizer;
4. otherwise: return base rows and report context overrides as unknown.

Trying to recover arbitrary control-flow semantics from every future optimized ASI is brittle and should not be the default contract.

### Stage F — attach character mapping separately

The reader may compare against or accept a companion `.gxtmap.json`.

If no mapping is supplied:

```text
CharacterMappingSource = ExternalOrUnknown
```

If current bundled mapping is explicitly selected and binary targets match the current layout, semantic labels such as `0x91 = Т` may be added.

### Stage G — produce diagnostics

Recommended warnings:

```text
ExactKnownBuild
KnownFamilyStructuralMatch
UnknownAsiBuild
AmbiguousMetricPayload
MetricPayloadHashMismatch
NoSemanticContextMetadata
ExternalMappingRequired
MappingTargetsConflict
UnsupportedPeArchitecture
MalformedPe
OutOfBoundsSection
```

---

## 16. Suggested C# result model

Illustrative shape only; adapt to the repository architecture.

```csharp
public sealed record AsiFontMetricsReadResult(
    AsiPeInfo Pe,
    AsiBuildInfo? Build,
    FontMetricsProfile? Metrics,
    IReadOnlyList<AsiPhysicalMetricPatch> PhysicalPatches,
    IReadOnlyList<AsiExecutableTarget> ExecutableTargets,
    AsiMappingAssociation Mapping,
    IReadOnlyList<AsiDiagnostic> Diagnostics);

public sealed record AsiPeInfo(
    ushort Machine,
    uint EntryPointRva,
    ulong ImageBase,
    uint SizeOfImage,
    IReadOnlyList<AsiPeSection> Sections);

public sealed record AsiBuildInfo(
    string Product,
    Version? Version,
    string? BuildTag,
    string Sha256);

public sealed record AsiPhysicalMetricPatch(
    FontMetricRow Row,
    int MetricIndex,
    ushort Advance,
    string? KnownContext,
    byte? KnownGxtCode,
    char? KnownCharacter);

public sealed record AsiExecutableTarget(
    uint TimeDateStamp,
    uint SizeOfImage,
    uint? FontMetricsRva,
    uint? FontStateRva,
    uint? HookRva);

public sealed record AsiMappingAssociation(
    AsiMappingAssociationKind Kind,
    string? ProfileId,
    string? ProfileSha256);
```

The important design choice is that `KnownCharacter` is nullable.

---

## 17. Compatibility levels

A useful reader should clearly say **how certain it is**.

### Level 1 — Exact known binary

Evidence:

```text
whole-file SHA-256 match
```

Result:

- deterministic parsing;
- exact offsets may be used;
- exact expected payload hashes;
- semantic adapter may be applied.

### Level 2 — Known generated family

Evidence:

- valid PE;
- recognized build marker;
- recognized code/data signatures;
- unambiguous metric payload;
- optional family-specific hook signatures.

Result:

- base rows recoverable;
- physical patch data recoverable if recognized;
- semantic contexts only when the family adapter proves them.

### Level 3 — Unknown ASI

Evidence:

- valid PE/DLL, but unknown BelarusianLanguage build.

Result:

- PE metadata only, plus any explicit self-describing metadata;
- no guessed semantic mapping;
- no guessed context model.

Failing safely is better than producing a plausible but incorrect font profile.

---

## 18. Strong recommendation for future generated ASIs

Reverse-engineering optimizer-dependent machine code is a poor long-term interchange format.

Future ASIs generated by this project should embed a small **read-only, self-describing metadata block** solely for tooling.

This is a proposal. It is **not present in the canonical 1.2.34 file**.

### Proposed envelope

```text
ASCII:
GTA_VC_BLR_ASI_META\0

uint32 little-endian:
JSON byte length

UTF-8:
JSON payload
```

Example:

```json
{
  "format": "gta-vc-blr-asi-metadata",
  "version": 1,
  "product": "BelarusianLanguage",
  "pluginVersion": "1.3.0",
  "fontMetrics": {
    "rowOrder": ["font2", "font1"],
    "metricCount": 210,
    "codeBase": "0x20",
    "payloadSha256": "..."
  },
  "mapping": {
    "profileId": "belarusian-vc-current",
    "profileSha256": "..."
  },
  "contexts": [
    {
      "context": "Gameplay",
      "font": "font1",
      "code": "0x91",
      "advance": 13
    }
  ],
  "runtimeTargets": [
    {
      "timeDateStamp": "0x...",
      "sizeOfImage": "0x...",
      "fontMetricsRva": "0x..."
    }
  ]
}
```

### Security/bounds rules

Reader:

- searches only within declared PE section ranges;
- permits only one authoritative metadata block;
- rejects unsupported metadata versions;
- caps JSON size, e.g. 1 MiB;
- validates UTF-8 strictly;
- treats every embedded RVA/offset as untrusted input;
- verifies any declared payload hash before association;
- never follows arbitrary pointers outside the file.

### Why embed a mapping hash instead of duplicating the map

The canonical `.gxtmap.json` should remain the editable source of truth.

Embedding its SHA-256/profile ID gives the reader a cryptographically strong association without creating another hand-maintained copy of every character pair in C/ASM.

---

## 19. Tests required for a reader implementation

At minimum:

### Canonical fixture

Given the exact 1.2.34 ASI:

- PE is parsed as x86 PE32 DLL;
- file hash matches;
- build string version is `1.2.34`;
- base payload is found;
- exactly 210 Font2 and 210 Font1 advances are returned;
- payload hash equals the canonical hash;
- values equal `Assets/ViceCity/belarusian.fontmetrics.json`;
- row order is Font2 then Font1.

### No-import fixture

The canonical ASI must parse successfully despite an empty import directory.

### Corruption tests

Mutate:

- one base-table byte;
- a PE section raw size;
- a section raw pointer;
- sparse pair index to `>= 210`;
- metadata length to exceed file size.

Expected:

- deterministic validation error/warning;
- no out-of-bounds read;
- no execution.

### Offset-independence test

Rebuild a test PE where the metric payload resides at another valid raw offset/RVA but retains recognized metadata/signatures.

Expected:

- structural/metadata mode finds it;
- importer does not depend on `0x485E`.

### Mapping tests

1. Current companion map:
   - semantic `0x91 -> Т`, `0xA8 -> т`.

2. Intermediate legacy T/y map:
   - reader must **not** relabel current 1.2.34 T/t patches as though the legacy map were current;
   - emit mapping/profile conflict diagnostic.

3. No mapping:
   - raw/base metrics still import;
   - semantic character fields remain unknown.

### Unknown-build test

A valid unrelated `.asi`/DLL must not be misidentified merely because it contains 420 plausible `uint16` values.

---

## 20. Rules for Codex agents modifying this area

1. **Do not merge mapping, bitmap, and advance logic.**
2. **Do not execute user-supplied ASI files.**
3. **Do not hardcode raw offset `0x485E` as the ASI format.**
4. **Do not require a PE import table.**
5. **Keep physical row order Font2 then Font1.**
6. **Keep the 210-entry metric contract.**
7. **Use `metricIndex = code - 0x20` only for direct ordinary-code addressing.**
8. **Preserve Vice City style/routing semantics for Bank and Heading.**
9. **Treat the Heading T/t JSON overrides as effective preview values, not proof that Heading reads physical indexes 113/136.**
10. **Treat `.mspc` sparse indexes as physical metric indexes.**
11. **Do not infer Unicode characters from physical metric indexes without a compatible mapping.**
12. **Use `preferredCode` when encoding; decoding aliases are not preferred output bytes.**
13. **The current Belarusian map uses `Т/т = 0x91/0xA8`; the older T/y map is legacy compatibility data.**
14. **Reuse the existing `FontMetricsProfile`/mapping models instead of creating parallel formats.**
15. **Reuse/generalize the existing canonical verification script and generated metrics contract.**
16. **For future ASI builds, prefer explicit versioned metadata over disassembly heuristics.**
17. **Every heuristic must expose uncertainty through diagnostics rather than silently guessing.**
18. **All offsets, lengths, RVAs, section ranges, counts, and indexes are untrusted input.**

---

## 21. Canonical-source priority

When information conflicts, use this priority:

### Character mapping

```text
1. Explicit companion/custom .gxtmap.json selected for the document
2. Current bundled Assets/ViceCity/belarusian.gxtmap.json
3. Legacy profiles only for decoding/compatibility
4. Never infer Unicode mapping from ASI metric data
```

### Base metrics

```text
1. Assets/ViceCity/belarusian.fontmetrics.json — editable source of truth
2. Generated native header — generated representation
3. Exact canonical ASI — verification/reference binary
4. Heuristic extraction from unknown ASI — imported data with diagnostics
```

### Runtime context semantics

```text
1. Versioned explicit ASI metadata, when introduced
2. Maintained native source/integration contract
3. Exact known-binary adapter
4. Known-family static signature analysis
5. Unknown -> do not guess
```

---

## 22. External verification notes

Public ASI-loader documentation describes ASI loading as loading custom `.asi` native libraries into the game process, which is consistent with treating the sample as a PE DLL rather than a proprietary container.

Public reverse-engineered Vice City/reVC code also corroborates the model where font rendering uses native font tables and font-style-dependent character routing. Those external sources are useful for architectural confirmation, but the exact offsets, hashes, mappings, and Belarusian runtime behavior in this document come from the supplied 1.2.34 binary and the GTA-VC-BLR repository.

---

## 23. Repository files that should be read together with this document

```text
docs/languages-and-encodings.md

Assets/ViceCity/belarusian.gxtmap.json
Assets/ViceCity/belarusian.fontmetrics.json

native/README.md
native/BelarusianFontMetricsIntegration.h
native/generated/BelarusianFontMetrics.generated.h

scripts/generate-belarusian-font-metrics.ps1
scripts/verify-belarusian-font-metrics-asi.ps1
```

---

## 24. Short implementation checklist

Before considering the ASI-reading feature complete:

- [ ] Static PE32 reader; no execution.
- [ ] Safe RVA ↔ raw-offset conversion.
- [ ] Build-marker extraction.
- [ ] Exact 1.2.34 golden adapter.
- [ ] General structural or metadata-driven metric extraction.
- [ ] 2 × 210 little-endian `uint16` support.
- [ ] Font2/Font1 row order preserved.
- [ ] Context overrides represented using existing profile semantics.
- [ ] Physical Bank/Heading routing not confused with direct GXT codes.
- [ ] `.mspc` sparse pair table treated as physical indexes.
- [ ] Companion mapping association is explicit.
- [ ] Legacy T/y mapping conflict is detected.
- [ ] Unknown ASIs fail safely with diagnostics.
- [ ] Corrupt PE/input tests cover all bounds.
- [ ] Future ASI metadata format has a versioned parser and tests.

---

## Appendix A — canonical 1.2.34 constants

```text
ASI_FILE_SIZE = 26112
ASI_SHA256 =
  17D98CC31D63067EB9040A44453D2EB09C983ED19C2AFAB276A227B2FBA523A7

BASE_METRICS_RAW_OFFSET = 0x485E
BASE_METRICS_RVA        = 0x605E
BASE_METRICS_ROWS       = 2
BASE_METRICS_COUNT      = 210
BASE_METRICS_VALUE_SIZE = 2
BASE_METRICS_SIZE       = 840
BASE_METRICS_SHA256 =
  50E8D5CDC3C7904875FC014CB343BDEEC0D8C9CF57C234F4B5AB53B60DABC1EA

SPARSE_TABLE_RAW_OFFSET = 0x6470
SPARSE_TABLE_COUNT      = 63
SPARSE_PAIR_SIZE        = 4
SPARSE_TABLE_SIZE       = 252
SPARSE_TABLE_SHA256 =
  76042A424B19D4DAA528286C85BD0239A432863B319BC46BC5C7DC3B2529EE34
```

---

## Appendix B — canonical executable targets found in 1.2.34

```text
Target A:
  PE TimeDateStamp  = 0x504D6947
  SizeOfImage       = 0x614000
  FontMetricsRva    = 0x296BD8
  FontStateRva      = 0x57F820
  HookRva           = 0x14FFE0

Target B:
  PE TimeDateStamp  = 0x48982736
  SizeOfImage       = 0x696000
  FontMetricsRva    = 0x295BE0
  FontStateRva      = 0x57E828
  HookRva           = 0x14FED0
```

---

## Appendix C — key current Belarusian byte assignments

```text
І = 0x49    і = 0x69
Ў = 0x86    ў = 0x9D
Т = 0x91    т = 0xA8
Ё = 0x96    ё = 0xAF
Я = 0xAD    я = 0xAE
```

Current contextual T/t effective advances:

```text
Default/MainMenu: 14 / 14
Gameplay:         13 / 14
Subtitles:        13 / 14
SaveLoad:         15 / 15
ExitConfirmation: 16 / 16
Heading:          18 / 18 effective through heading routing
Bank:             22 in the relevant routed physical Font2 slots
```
