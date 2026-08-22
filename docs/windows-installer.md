# Windows installer export

## Availability

**Export as → Windows installer…** is enabled only for a classic GTA Vice City
project with an attached TXD. GTA III and Definitive Edition are not supported.
The **Windows installer** panel below the TXD controls opens the same profile
editor without starting an export. Confirming the dialog attaches the selected
files to the current project and marks it dirty. An incomplete draft, including
one with no attached installer files, can be confirmed here. Use the normal
**Save** command to persist the profile and its remaining binary attachments in
BYX v4. For a plain GXT document with an attached TXD, saving prompts for a new
BYX project path.

Export still opens the profile editor so its product metadata can be reviewed,
but files already stored in the BYX project are preselected and do not need to be
read from disk again. In export mode, confirming requires
`BelarusianLanguage.asi`, both `MODELS\gta3.img` and `MODELS\gta3.dir`, and the
complete SilentPatch set; an incomplete draft cannot be exported. Up to 23 files
from the game `txd` folder are optional.

## BYX v4 installer data

The optional `installer` manifest item references `installer/profile.json` and an
array of binary attachments. Each attachment is stored as
`installer/assets/<guid>.bin` and has an ID, original file name, destination,
role, bytes, and SHA-256 in the profile/manifest pair. Legacy profiles may still
store the ASI Loader and additional-file roles, so existing BYX projects remain
readable. A stored profile may contain a partial or empty attachment list. New
exports accept the main ASI, `MODELS\gta3.img`, `MODELS\gta3.dir`, the exact
SilentPatch set, and up to 23 optional `txd\*.txd` replacements.

Loading verifies the declared entry set and every hash. Paths are case-insensitive
and must be relative to the game root. Absolute paths, `..`, control characters,
Windows device names, invalid Windows path characters, and duplicate destinations
are rejected. `.asi` and `.dll` attachments must be x86 PE images. README, source,
archive, checksum, and `APPLY_*.cmd`/`CLEAN_*.cmd` payloads are rejected. The
unpacked project limit is 1 GB and the installer attachment limit is 512 files.

BYX v3 opens with no installer profile and is upgraded on save. BYX v1/v2 and
versions newer than v4 are rejected.

## Build

Export writes a snapshot to a unique temporary staging directory, emits a UTF-8
Inno script, and invokes the bundled Inno Setup 7.0.2 x86 command-line compiler.
The compiler is fully offline under `Tools/InnoSetup/7.0.2-x86`; its license,
pinned source and installer hash, and per-file SHA-256 inventory are included. The
completed executable is moved to the selected target atomically. Cancellation or
failure removes staging and leaves an existing target untouched.

The generated installer always contains:

- `TEXT\BELARUS.GXT`, generated from the current GXT snapshot;
- `MODELS\FONTS.TXD`, copied from the attached TXD;
- the main plugin as `BelarusianLanguage.asi`;
- `BelarusianLanguage.ini` in the game root (`Enabled=1`) so the translation is
  selected on launch;
- `MODELS\gta3.img` and `MODELS\gta3.dir`;
- up to 23 optional `txd\*.txd` replacements;
- `SilentPatchVC.asi`, `SilentPatchVC.ini`, and the eight fixed IPL replacements.

The core component is fixed. SilentPatch is one all-or-nothing component, selected
by default but removable on the components page. `FONTB.TXD`, `dinput8.dll`, and
arbitrary additional payloads are not emitted.

Two UTF-8 JSON manifests are staged and setup writes the one matching the selected
components to `_BelarusianModBackup\<ProductId>\manifest.json`. It records schema
version 1, the stable ProductId, product metadata, selected components, and each
file's path, component, SHA-256, and original-backup policy.

## Installation and removal

Setup is available in English and Belarusian and shows the standard language,
welcome, game-directory, components, ready, progress, and finish pages. The welcome
page displays the bundled Vice City postcard as a 24-bit BMP. The common
Steam path is only an initial suggestion. Setup does not inspect `gta-vc.exe` or
validate the folder contents, and `AppendDefaultDirName=no` ensures the directory
selected by the user is used verbatim.

The stable Inno AppId and backup directory derive from the profile ProductId.
Original `MODELS\FONTS.TXD`, `MODELS\gta3.img`, `MODELS\gta3.dir`, any installed
`txd\*.txd` replacements, and, when SilentPatch is selected, the eight IPL files
are copied to matching relative paths below `_BelarusianModBackup\<ProductId>`.
Existing GXT/ASI/INI payloads are mod-owned and overwritten without an original
backup. Transaction state, licenses, and the uninstaller stay under
`_BelarusianMod\<ProductId>` in the game folder. A shortcut named
`uninstall_BLR.exe` is created in the game root and is removed with the
localization. Uninstall removes that support directory except for a leftover
`conflicts` folder from an older installer, which is kept. A failed or cancelled
attempt restores its pending pre-attempt snapshots and does not commit new state.
An update must use the same game directory recorded by the first successful
installation; moving the installation requires uninstalling it first.

Updates keep the first original backup and update installed hashes. Reinstalling
without SilentPatch restores unmodified IPL originals and removes its ASI/INI.
Uninstall restores unmodified font, archive, TXD, and IPL originals, deletes unmodified mod-owned
payloads, then removes the ProductId backup directory. Files the user changed
after installation are left in place; uninstall does not prompt and does not copy
them to `conflicts`. State created by the previous installer schema, or an
older ProgramData-based installation of the same ProductId, is
rejected with an instruction to uninstall that version first.
