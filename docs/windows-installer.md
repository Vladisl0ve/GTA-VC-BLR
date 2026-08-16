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
role, bytes, and SHA-256 in the profile/manifest pair. The roles are main ASI, ASI
Loader, SilentPatch, and additional file.

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
- `FONTB.TXD` and `MODELS\FONTS.TXD`, both copied from the attached TXD;
- the main plugin as `BelarusianLanguage.asi`;
- the x86 ASI Loader as `dinput8.dll`;
- all selected SilentPatch files, including `SilentPatchVC.asi`;
- explicitly selected additional files.

No source, archive, checksum, README, or apply/clean command file is copied into
the game.

## Installation and removal

Setup is available in English and Belarusian and shows the standard language,
welcome, game-directory, ready, progress, and finish pages. It suggests a common
Steam, Rockstar, or GOG directory (or the previous AppId directory), but browsing
is always available. A selected directory must contain `gta-vc.exe` and cannot be
a drive root or protected Windows directory. An executable without readable
version information produces a warning rather than a block.

The stable Inno AppId derives from the profile ProductId. Backups, transactional
state, licenses, conflict files, and the uninstaller live outside the game under
`%ProgramData%\GTA GXT Editor\Installations\<ProductId>`. Before each write, setup
captures both the first original and a pending pre-attempt snapshot. Duplicate
`BelarusianLanguage*.asi` files in the game root, `scripts`, and `plugins` are
disabled into the same state. A failed or cancelled attempt restores every pending
snapshot and does not commit a new state.

Updates keep the first original backup, update the installed hash, and restore or
remove payloads omitted by the new profile. On uninstall, unchanged replacements
are restored, newly created files are deleted, disabled ASI duplicates are
returned, and empty created directories are removed.

If a managed file changed after installation, interactive uninstall asks per file:
restore it (archive the changed copy, then restore/delete) or leave it (archive the
original backup and keep the current file). Silent uninstall uses the safe restore
choice automatically. Conflict copies remain under the support directory and the
interactive uninstaller displays their location.
