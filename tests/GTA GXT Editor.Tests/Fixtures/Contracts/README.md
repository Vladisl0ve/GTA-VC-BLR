# Binary contract fixtures

These files lock the external binary contracts of the supported formats. They are
committed test inputs, not data produced at test runtime. The adjacent standalone
`generate_contract_fixtures.py` describes their byte layout directly and uses only
Python's standard `struct`, `json`, and `zipfile` modules. It does not reference or
invoke `GXTManager`, `TxdReader`, `ByxProjectSerializer`, or any application code.

Tests also pin every fixture's SHA-256 so an accidental regeneration cannot silently
change the contract.

| File | Size | SHA-256 |
| --- | ---: | --- |
| `gta3-original.gxt` | 66 | `f2f80427cfb918bda8cbbc95c5837c5f8b97b80ec9531ce176700d14b094d79e` |
| `gta3-edited.gxt` | 78 | `c731f4099b7f7b9adf4710c7568a65e9fa7e084dcd316d6ef7ccd94f522554e8` |
| `vice-city-original.gxt` | 126 | `5bc605a404369429d41da82d214b90ae740bf7129488dc08634253e598ed8b3b` |
| `vice-city-edited.gxt` | 136 | `878a61b5e9deab56254c3d76841c40e67684af2d361be2f7f951eba64342041f` |
| `font1.txd` | 176 | `8af49bc69b36c533e46e9c959ec085eb976a791adc4c6836802a07ce5d2f58fa` |
| `project.byx` | 2240 | `5442e451b213d182c1b85b27ce452f766b01620e5a21400df3a324cebf4e9f70` |

## Encoded contracts

- GTA III has `HELLO = Hello` and `SECOND = Second`. The edited file changes
  `HELLO` to `Updated III`, forcing the second TDAT offset to move.
- Vice City has `MAIN/HELLO = Hello` and `MISSION/BRIEF = Go there`. The edited
  file changes `MAIN/HELLO` to `Updated VC`, moving the `MISSION` table offset
  from 72 to 82.
- TXD is a RenderWare `0x1003FFFF` PC D3D8 dictionary containing one uncompressed
  2x1 BGRA32 `font1` texture. Its pixels are
  `(1,2,3,255)` and `(10,20,30,128)`.
- BYX is a stored ZIP container using the v3 five-entry layout. It embeds the
  original Vice City GXT, the TXD above, a verified `Ж = 0x80` character map,
  and one metadata record with the comment `Original note`. Every embedded item
  is linked from `manifest.json` by its SHA-256.

The ZIP timestamp is fixed and entries use the stored method only to make the
committed outer fixture reproducible. ZIP timestamps, compression, and entry order
are deliberately not part of the tests; entry names and unpacked content are.
