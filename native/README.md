# Native Belarusian font-metrics integration contract

`Assets/ViceCity/belarusian.fontmetrics.json` is the only editable source of
Vice City Belarusian font advances. Run:

```powershell
./scripts/generate-belarusian-font-metrics.ps1
./scripts/generate-belarusian-font-metrics.ps1 -Verify
```

The generator writes
`native/generated/BelarusianFontMetrics.generated.h`. The header is tracked so
native builds do not need PowerShell, but CI rejects drift. It contains the two
`uint16_t[210]` rows in their physical game order (`font2`, then `font1`) and the
declarative sparse context overrides.

A maintained `BelarusianLanguage.asi` source must include
`native/BelarusianFontMetricsIntegration.h`, call
`BelarusianFontMetrics_InstallBaseRows` for the game `Size[2][210]` storage, and
use `BelarusianFontMetrics_TryGetOverride` rather than declaring or copying a
second metrics table. Heading remains a game remap to row 1 index 198; its JSON
entries only expose the known effective Belarusian T/t preview advances.

## Remaining source-availability boundary

The repository still does not contain the maintained source that produced the
canonical embedded version 1.2.34 ASI. The adjacent 1.2.21 C source is stale: 71
metric slots and runtime context behavior differ, so it is intentionally neither
copied nor patched here. The adapter is therefore the compile-time integration
point for that source when it is recovered; this repository cannot complete or
build the final ASI wiring before then.

The canonical binary is optional and is not a repository or CI dependency. When
available, verify its exact file identity, unique payload offset `0x485E`,
840-byte table hash, physical row order, and equality with the JSON using:

```powershell
./scripts/verify-belarusian-font-metrics-asi.ps1 -AsiPath C:\path\to\BelarusianLanguage.asi
```

For `scripts/run-tests.ps1`, the same forensic check is enabled by setting
`GTA_VC_BLR_CANONICAL_ASI`; without it, only the required generated-header drift
check runs.
