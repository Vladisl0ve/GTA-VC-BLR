# Vice City font metrics schema

Schema version 1 stores the canonical Vice City base rows as exactly 210 unsigned
advances in `font2`, followed by exactly 210 advances in `font1`. For ordinary
printable codes, `metricIndex = code - 0x20` and `code = metricIndex + 0x20`.
Context changes are sparse overrides keyed by context, font texture, and the same
hexadecimal byte code used by the character map.

The two `Heading` entries for Belarusian `Т` (`0x91`) and `т` (`0xA8`) are a
deliberately narrow compatibility representation of their effective advance 18.
They are retained so version-1 files and BYX projects continue to round-trip; they
are not a runtime metrics context in the preview. `Heading` is a Vice City font
style. The resolver applies the game's complete `FindNewCharacter` routing before
reading row 1 (`font1`) or looking up a runtime-context override. Both Belarusian
characters consequently route to metric index 198, whose canonical advance is 18.

The TXD preview applies ASI-authoritative Heading glyph overrides for the current
Belarusian atlas. When the linked recognized Main ASI profile confirms the
expected character codes, `Я/я` use font1 glyph/metric code `0xEB`, while
`Ё/ё` use `0xEC`. Other characters keep Vice City's standard routing.
