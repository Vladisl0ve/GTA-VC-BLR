# Vice City font metrics schema

Schema version 1 stores the canonical Vice City base rows as exactly 210 unsigned
advances in `font2`, followed by exactly 210 advances in `font1`. For ordinary
printable codes, `metricIndex = code - 0x20` and `code = metricIndex + 0x20`.
Context changes are sparse overrides keyed by context, font texture, and the same
hexadecimal byte code used by the character map.

The two `Heading` entries for Belarusian `Т` (`0x91`) and `т` (`0xA8`) are a
deliberately narrow compatibility representation of their effective advance 18.
They are not a general description of Vice City's heading routing. The game first
remaps both characters to metric index 198 and then reads row 1 (`font1`), whose
canonical advance at that index is 18. A resolver that models heading behavior
beyond these two characters must implement that remap explicitly rather than
generalizing the compatibility overrides.
