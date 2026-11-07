#!/usr/bin/env python3
"""Draws assets/LabelBridge.ico: the tray icon's printer with a green "running" dot.

Same shapes as src/LabelBridge.Tray/Icons.cs (32-unit grid), rendered with 4x4
supersampling at 16-256 px and packed as PNG frames. Standard library only.
"""
import struct
import sys
import zlib

DARK = (40, 44, 52)
WHITE = (255, 255, 255)
GREEN = (46, 160, 67)

# (kind, x, y, w, h | cx, cy, r, color), painted in order on a 32x32 grid.
SHAPES = [
    ("rect", 7, 1, 16, 12, DARK),    # top label outline
    ("rect", 9, 3, 12, 8, WHITE),    # top label
    ("rect", 2, 11, 26, 13, DARK),   # printer body
    ("rect", 6, 19, 18, 10, DARK),   # front label outline
    ("rect", 8, 21, 14, 6, WHITE),   # front label
    ("circle", 25, 23, 8, WHITE),    # ring
    ("circle", 25, 23, 6, GREEN),    # status dot
]


def color_at(x, y):
    """Color at a point in 32-unit space, or None if transparent."""
    c = None
    for s in SHAPES:
        if s[0] == "rect":
            _, rx, ry, rw, rh, col = s
            if rx <= x < rx + rw and ry <= y < ry + rh:
                c = col
        else:
            _, cx, cy, r, col = s
            if (x - cx) ** 2 + (y - cy) ** 2 <= r * r:
                c = col
    return c


def render(size, ss=4):
    rows = []
    for py in range(size):
        row = bytearray([0])  # PNG filter: none
        for px in range(size):
            acc = [0, 0, 0]
            hits = 0
            for sy in range(ss):
                for sx in range(ss):
                    x = (px + (sx + 0.5) / ss) * 32 / size
                    y = (py + (sy + 0.5) / ss) * 32 / size
                    c = color_at(x, y)
                    if c:
                        hits += 1
                        for i in range(3):
                            acc[i] += c[i]
            if hits:
                row += bytes([acc[0] // hits, acc[1] // hits, acc[2] // hits, 255 * hits // (ss * ss)])
            else:
                row += bytes(4)
        rows.append(bytes(row))
    return png(size, b"".join(rows))


def png(size, raw):
    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)  # 8-bit RGBA
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")


def ico(frames):
    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = 6 + 16 * len(frames)
    entries, data = b"", b""
    for size, img in frames:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(img), offset + len(data))
        data += img
    return header + entries + data


if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "assets/LabelBridge.ico"
    frames = [(s, render(s)) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)]
    open(out, "wb").write(ico(frames))
    open(out.replace(".ico", "-256.png"), "wb").write(frames[-1][1])
    print(out)
