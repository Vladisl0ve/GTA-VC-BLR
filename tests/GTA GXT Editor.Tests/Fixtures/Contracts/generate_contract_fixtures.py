"""Build the committed binary contract fixtures without using product serializers.

This script is intentionally standalone. It encodes the documented binary layouts
directly and uses only Python's standard ZIP implementation for the BYX container.
Tests consume the committed outputs and never invoke this script.
"""

from __future__ import annotations

import hashlib
import json
import struct
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parent
RENDERWARE_VERSION = 0x1003FFFF


def fixed_ascii(value: str, length: int) -> bytes:
    encoded = value.encode("ascii")
    if len(encoded) > length:
        raise ValueError(f"{value!r} exceeds {length} ASCII bytes")
    return encoded.ljust(length, b"\0")


def gxt_text(value: str) -> bytes:
    return (value + "\0").encode("utf-16le")


def gta3_gxt(entries: list[tuple[str, str]]) -> bytes:
    values = [gxt_text(value) for _, value in entries]
    offset = 0
    keys = bytearray()
    for (name, _), value in zip(entries, values, strict=True):
        keys += struct.pack("<I", offset)
        keys += fixed_ascii(name, 8)
        offset += len(value)
    return b"".join(
        (
            b"TKEY",
            struct.pack("<I", len(keys)),
            keys,
            b"TDAT",
            struct.pack("<I", sum(map(len, values))),
            *values,
        )
    )


def vice_city_table(name: str, entries: list[tuple[str, str]], is_main: bool) -> bytes:
    values = [gxt_text(value) for _, value in entries]
    offset = 0
    keys = bytearray()
    for (key, _), value in zip(entries, values, strict=True):
        keys += struct.pack("<I", offset)
        keys += fixed_ascii(key, 8)
        offset += len(value)
    return b"".join(
        (
            b"" if is_main else fixed_ascii(name, 8),
            b"TKEY",
            struct.pack("<I", len(keys)),
            keys,
            b"TDAT",
            struct.pack("<I", sum(map(len, values))),
            *values,
        )
    )


def vice_city_gxt(tables: list[tuple[str, list[tuple[str, str]]]]) -> bytes:
    blocks = [
        vice_city_table(name, entries, index == 0)
        for index, (name, entries) in enumerate(tables)
    ]
    table_header_size = len(tables) * 12
    offset = 8 + table_header_size
    table_index = bytearray()
    for (name, _), block in zip(tables, blocks, strict=True):
        table_index += fixed_ascii(name, 8)
        table_index += struct.pack("<I", offset)
        offset += len(block)
    return b"".join(
        (
            b"TABL",
            struct.pack("<I", table_header_size),
            table_index,
            *blocks,
        )
    )


def chunk(chunk_type: int, payload: bytes) -> bytes:
    return struct.pack("<III", chunk_type, len(payload), RENDERWARE_VERSION) + payload


def txd() -> bytes:
    pixels = bytes((1, 2, 3, 255, 10, 20, 30, 128))
    texture_structure = b"".join(
        (
            struct.pack("<II", 8, 0x1101),
            fixed_ascii("font1", 32),
            fixed_ascii("", 32),
            struct.pack("<IIHHBBBB", 0x0500, 1, 2, 1, 32, 1, 4, 0),
            struct.pack("<I", len(pixels)),
            pixels,
        )
    )
    native_texture = chunk(0x01, texture_structure) + chunk(0x03, b"")
    dictionary = chunk(0x01, struct.pack("<HH", 1, 0))
    dictionary += chunk(0x15, native_texture)
    dictionary += chunk(0x03, b"")
    return chunk(0x16, dictionary)


def json_bytes(value: object) -> bytes:
    return (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def write_zip_entry(archive: zipfile.ZipFile, name: str, data: bytes) -> None:
    entry = zipfile.ZipInfo(name, date_time=(2020, 1, 1, 0, 0, 0))
    entry.compress_type = zipfile.ZIP_STORED
    entry.create_system = 0
    entry.external_attr = 0
    archive.writestr(entry, data)


def byx(gxt_data: bytes, txd_data: bytes) -> None:
    character_map = json_bytes(
        {
            "version": 1,
            "mappings": [
                {
                    "character": "Ж",
                    "codes": ["0x80"],
                    "preferredCode": "0x80",
                }
            ],
        }
    )
    metadata = json_bytes(
        {
            "format": "GXT_ENTRY_METADATA",
            "version": 1,
            "blocks": [
                {
                    "id": "contract.block",
                    "type": "mission",
                    "name": "Contract fixture",
                    "description": "Independent fixture",
                    "order": 1,
                }
            ],
            "entries": [
                {
                    "table": "MAIN",
                    "key": "HELLO",
                    "comment": "Original note",
                    "occurrences": [
                        {
                            "blockId": "contract.block",
                            "order": 1,
                            "context": "Fixture context",
                        }
                    ],
                }
            ],
        }
    )
    manifest = json_bytes(
        {
            "format": "BYX",
            "version": 3,
            "game": "GTA Vice City",
            "language": "en",
            "gxt": {
                "originalFileName": "contract-vc.gxt",
                "entry": "gxt/main.gxt",
                "sha256": sha256(gxt_data),
            },
            "txd": {
                "originalFileName": "fonts.txd",
                "entry": "txd/fonts.txd",
                "sha256": sha256(txd_data),
            },
            "characterMap": {
                "entry": "mapping/characters.json",
                "sha256": sha256(character_map),
                "isVerified": True,
            },
            "metadata": {
                "entry": "metadata/entries.json",
                "sha256": sha256(metadata),
            },
        }
    )

    with zipfile.ZipFile(ROOT / "project.byx", "w") as archive:
        write_zip_entry(archive, "gxt/main.gxt", gxt_data)
        write_zip_entry(archive, "txd/fonts.txd", txd_data)
        write_zip_entry(archive, "mapping/characters.json", character_map)
        write_zip_entry(archive, "metadata/entries.json", metadata)
        write_zip_entry(archive, "manifest.json", manifest)


def main() -> None:
    gta3_original = gta3_gxt([("HELLO", "Hello"), ("SECOND", "Second")])
    gta3_edited = gta3_gxt([("HELLO", "Updated III"), ("SECOND", "Second")])
    vice_city_original = vice_city_gxt(
        [("MAIN", [("HELLO", "Hello")]), ("MISSION", [("BRIEF", "Go there")])]
    )
    vice_city_edited = vice_city_gxt(
        [("MAIN", [("HELLO", "Updated VC")]), ("MISSION", [("BRIEF", "Go there")])]
    )
    txd_data = txd()

    fixtures = {
        "gta3-original.gxt": gta3_original,
        "gta3-edited.gxt": gta3_edited,
        "vice-city-original.gxt": vice_city_original,
        "vice-city-edited.gxt": vice_city_edited,
        "font1.txd": txd_data,
    }
    for name, data in fixtures.items():
        (ROOT / name).write_bytes(data)
    byx(vice_city_original, txd_data)

    for path in sorted(ROOT.iterdir()):
        if path.suffix in {".gxt", ".txd", ".byx"}:
            print(f"{path.name}: {path.stat().st_size} bytes, {sha256(path.read_bytes())}")


if __name__ == "__main__":
    main()
