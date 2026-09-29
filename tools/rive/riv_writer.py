"""Minimal Rive runtime-format (.riv) writer.

Type and property keys come from rive_schema.json, extracted from the generated
headers of rive-app/rive-runtime (include/rive/generated/**/*_base.hpp).
Format (runtime_header.hpp / file.cpp):
  "RIVE" | varuint major | varuint minor | varuint fileId | property ToC (0-terminated)
  then objects: varuint typeKey, (varuint propertyKey, value)*, 0
"""
from __future__ import annotations

import json
import pathlib
import struct

MAJOR, MINOR = 7, 4

_TYPE_CODES = {
    "CoreUintType": "uint",
    "CoreIdType": "uint",
    "CoreDoubleType": "double",
    "CoreStringType": "string",
    "CoreBoolType": "bool",
    "CoreColorType": "color",
}


class Color(int):
    """ARGB colour, written as uint32."""

    @staticmethod
    def hex(value: str, alpha: float = 1.0) -> "Color":
        value = value.lstrip("#")
        a = round(max(0.0, min(1.0, alpha)) * 255)
        return Color((a << 24) | int(value, 16))


def varuint(value: int) -> bytes:
    if value < 0:
        raise ValueError(f"negative varuint {value}")
    out = bytearray()
    while True:
        byte = value & 0x7F
        value >>= 7
        if value:
            out.append(byte | 0x80)
        else:
            out.append(byte)
            return bytes(out)


class Schema:
    def __init__(self, path: pathlib.Path):
        self.types = json.loads(path.read_text())

    def type_key(self, name: str) -> int:
        return self.types[name]["typeKey"]

    def prop(self, type_name: str, prop: str) -> tuple[int, str | None]:
        name = type_name
        while name in self.types:
            info = self.types[name]
            if prop in info["props"]:
                p = info["props"][prop]
                return p["key"], _TYPE_CODES.get(p.get("type", ""))
            name = info["parent"]
        raise KeyError(f"{type_name}.{prop} not in schema")


class RivFile:
    def __init__(self, schema: Schema):
        self.schema = schema
        self.objects: list[tuple[str, dict]] = []

    def add(self, type_name: str, **props) -> int:
        """Append an object; returns its index in the file stream."""
        self.schema.type_key(type_name)  # validate type early
        for key in props:
            self.schema.prop(type_name, key)  # validate property early
        self.objects.append((type_name, props))
        return len(self.objects) - 1

    def _encode(self, type_name: str, prop: str, value) -> bytes:
        key, kind = self.schema.prop(type_name, prop)
        if isinstance(value, Color):
            kind = "color"
        elif kind is None:
            kind = {str: "string", bool: "bool", float: "double", int: "uint"}[type(value)]
        out = varuint(key)
        if kind == "uint":
            out += varuint(int(value))
        elif kind == "double":
            out += struct.pack("<f", float(value))
        elif kind == "string":
            data = value.encode("utf-8")
            out += varuint(len(data)) + data
        elif kind == "bool":
            out += bytes([1 if value else 0])
        elif kind == "color":
            out += struct.pack("<I", int(value) & 0xFFFFFFFF)
        else:
            raise ValueError(kind)
        return out

    def to_bytes(self) -> bytes:
        out = bytearray(b"RIVE")
        out += varuint(MAJOR) + varuint(MINOR) + varuint(0)
        out += varuint(0)  # empty property table of contents: only known keys are written
        for type_name, props in self.objects:
            out += varuint(self.schema.type_key(type_name))
            for prop, value in props.items():
                if value is None:
                    continue
                out += self._encode(type_name, prop, value)
            out += varuint(0)
        return bytes(out)
