# Windows installer export

## Availability

**Export as → Windows installer…** is enabled only for a classic GTA Vice City
project with an attached TXD. GTA III and Definitive Edition are not supported.
The installer profile is applied only after the profile dialog is confirmed; this
marks the project dirty and the profile is persisted in BYX v4.

## BYX v4 installer data

The optional `installer` manifest item references `installer/profile.json` and an
array of binary attachments. Each attachment is stored as
`installer/assets/<guid>.bin` and has an ID, original file name, destination,
role, bytes, and SHA-256 in the profile/manifest pair. Legacy profiles may still
store the ASI Loader and additional-file roles, so existing BYX projects remain
readable. New exports accept only the main ASI and the exact SilentPatch set.

Loading verifies the declared entry set and every hash. Paths are case-insensitive
and must be relative to the game root. Absolute paths, `..`, control characters,
Windows device names, invalid Windows path characters, and duplicate destinations
are rejected. `.asi` and `.dll` attachments must be x86 PE images. README, source,
archive, checksum, and `APPLY_*.cmd`/`CLEAN_*.cmd` payloads are rejected. The
unpacked project limit is 512 MB and the installer attachment limit is 512 files.

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
welcome, game-directory, components, ready, progress, and finish pages. The common
Steam path is only an initial suggestion. Setup does not inspect `gta-vc.exe` or
validate the folder contents, and `AppendDefaultDirName=no` ensures the directory
selected by the user is used verbatim.

The stable Inno AppId and backup directory derive from the profile ProductId.
Original `MODELS\FONTS.TXD` and, when SilentPatch is selected, the eight IPL files
are copied to matching relative paths below `_BelarusianModBackup\<ProductId>`.
Existing GXT/ASI/INI payloads are mod-owned and overwritten without an original
backup. Transaction state, licenses, conflicts, and the uninstaller stay under
`%ProgramData%\GTA GXT Editor\Installations\<ProductId>`. A failed or cancelled
attempt restores its pending pre-attempt snapshots and does not commit new state.
An update must use the same game directory recorded by the first successful
installation; moving the installation requires uninstalling it first.

Updates keep the first original backup and update installed hashes. Reinstalling
without SilentPatch restores the IPL originals and removes its ASI/INI. Uninstall
restores the font/IPL originals, deletes mod-owned payloads, then removes the
ProductId backup directory. State created by the previous installer schema is
rejected with an instruction to uninstall that version first.

If a managed file changed after installation, interactive uninstall asks per file:
restore it (archive the changed copy, then restore/delete) or leave it (archive the
original backup and keep the current file). Silent uninstall uses the safe restore
choice automatically. Conflict copies remain under the support directory and the
interactive uninstaller displays their location.
