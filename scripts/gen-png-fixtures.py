"""Generates tests/ToolBelt.Tests/Visualization/PngFixtureData.cs for PngReaderTests.

Each fixture is a small PNG hand-encoded here (every colour type and legal bit depth, all five scanline filters chosen
per row, optional Adam7 interlacing and tRNS) plus the expected 8-bit RGBA pixels computed from the source samples
following the PNG spec (16-bit -> high byte, low depths scaled to 0-255). Where Pillow can decode the file, its RGBA
output is asserted to match the expected pixels first, so the C# reader is graded against two independent decoders.

Run:  python scripts/gen-png-fixtures.py
"""
import base64
import io
import random
import struct
import zlib

from PIL import Image

OUT = "tests/ToolBelt.Tests/Visualization/PngFixtureData.cs"
CHANNELS = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}
ADAM7 = [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)]


def chunk(kind, data):
    return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data) & 0xFFFFFFFF)


def pack_row(samples, depth):
    if depth >= 8:
        fmt = ">B" if depth == 8 else ">H"
        return b"".join(struct.pack(fmt, s) for s in samples)
    out, acc, bits = bytearray(), 0, 0
    for s in samples:
        acc = (acc << depth) | s
        bits += depth
        if bits == 8:
            out.append(acc)
            acc, bits = 0, 0
    if bits:
        out.append(acc << (8 - bits))
    return bytes(out)


def paeth(a, b, c):
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    return a if pa <= pb and pa <= pc else (b if pb <= pc else c)


def filter_row(kind, cur, prev, bpp):
    out = bytearray(len(cur))
    for i in range(len(cur)):
        a = cur[i - bpp] if i >= bpp else 0
        b = prev[i]
        c = prev[i - bpp] if i >= bpp else 0
        pred = [0, a, b, (a + b) // 2, paeth(a, b, c)][kind]
        out[i] = (cur[i] - pred) & 0xFF
    return bytes([kind]) + bytes(out)


def encode(width, height, ctype, depth, pixels, interlace, rng, plte=None, trns=None):
    ch = CHANNELS[ctype]
    bpp = max(1, ch * depth // 8)
    passes = [(0, 0, 1, 1)] if not interlace else ADAM7
    raw = bytearray()
    for x0, y0, dx, dy in passes:
        xs = list(range(x0, width, dx))
        ys = list(range(y0, height, dy))
        if not xs or not ys:
            continue
        prev = bytes(len(pack_row([0] * (len(xs) * ch), depth)))
        for y in ys:
            samples = [s for x in xs for s in pixels[y][x]]
            cur = pack_row(samples, depth)
            raw += filter_row(rng.randrange(5), cur, prev, bpp)
            prev = cur
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, depth, ctype, 0, 0, 1 if interlace else 0))
    if plte:
        png += chunk(b"PLTE", bytes(c for rgb in plte for c in rgb))
    if trns is not None:
        png += chunk(b"tRNS", trns)
    png += chunk(b"tEXt", b"Comment\x00ancillary chunks are ignored")
    data = zlib.compress(bytes(raw), 9)
    png += chunk(b"IDAT", data[: len(data) // 2]) + chunk(b"IDAT", data[len(data) // 2:])   # split IDAT
    png += chunk(b"IEND", b"")
    return png


def expected_rgba(width, height, ctype, depth, pixels, plte=None, trns_key=None, trns_alpha=None):
    out = bytearray()
    top = (1 << depth) - 1
    for y in range(height):
        for x in range(width):
            s = pixels[y][x]
            eight = (lambda v: v >> 8) if depth == 16 else ((lambda v: v * 255 // top) if depth < 8 else (lambda v: v))
            if ctype == 3:
                r, g, b = plte[s[0]]
                a = trns_alpha[s[0]] if trns_alpha and s[0] < len(trns_alpha) else 255
            elif ctype == 0:
                r = g = b = eight(s[0])
                a = 0 if trns_key is not None and s[0] == trns_key[0] else 255
            elif ctype == 2:
                r, g, b = (eight(v) for v in s)
                a = 0 if trns_key is not None and tuple(s) == tuple(trns_key) else 255
            elif ctype == 4:
                r = g = b = eight(s[0])
                a = eight(s[1])
            else:
                r, g, b, a = (eight(v) for v in s)
            out += bytes([r, g, b, a])
    return bytes(out)


def main():
    rng = random.Random(20261005)
    fixtures = []
    cases = [(0, d) for d in (1, 2, 4, 8, 16)] + [(2, 8), (2, 16), (3, 1), (3, 2), (3, 4), (3, 8), (4, 8), (4, 16), (6, 8), (6, 16)]
    for ctype, depth in cases:
        for interlace in (False, True):
            for trns in (False, True):
                if trns and ctype in (4, 6):
                    continue
                w, h = rng.randint(1, 19), rng.randint(1, 13)
                if interlace and rng.random() < 0.5:
                    w, h = rng.randint(1, 4), rng.randint(1, 4)          # tiny images leave Adam7 passes empty
                top = (1 << depth) - 1
                ch = CHANNELS[ctype]
                pixels = [[tuple(rng.randint(0, top) for _ in range(ch)) for _ in range(w)] for _ in range(h)]
                plte = trns_bytes = trns_key = trns_alpha = None
                if ctype == 3:
                    plte = [(rng.randrange(256), rng.randrange(256), rng.randrange(256)) for _ in range(1 << depth)]
                    if trns:
                        trns_alpha = [rng.randrange(256) for _ in range(max(1, len(plte) // 2))]
                        trns_bytes = bytes(trns_alpha)
                elif trns:
                    trns_key = pixels[rng.randrange(h)][rng.randrange(w)]
                    trns_bytes = b"".join(struct.pack(">H", v) for v in trns_key)
                png = encode(w, h, ctype, depth, pixels, interlace, rng, plte, trns_bytes)
                exp = expected_rgba(w, h, ctype, depth, pixels, plte, trns_key, trns_alpha)
                # Graded by spec only where Pillow is known to differ: 16-bit (it clips instead of shifting) and interlaced
                # sub-8-bit greyscale with tRNS (it scales samples to 8 bits but compares the unscaled key, losing transparency;
                # PNG spec 11.3.2.1 compares the key with the raw sample).
                pillow_quirk = depth == 16 or (trns and ctype == 0 and depth < 8)
                if not pillow_quirk:
                    im = Image.open(io.BytesIO(png)).convert("RGBA")
                    assert im.size == (w, h)
                    assert im.tobytes() == exp, f"Pillow disagrees for type {ctype} depth {depth} interlace {interlace} trns {trns}"
                name = f"type{ctype}_{depth}bit{'_adam7' if interlace else ''}{'_trns' if trns else ''}"
                fixtures.append((name, w, h, base64.b64encode(png).decode(), base64.b64encode(exp).decode()))

    lines = [
        "// <auto-generated> by scripts/gen-png-fixtures.py — do not edit by hand.",
        "namespace ToolBelt.Tests.Visualization",
        "{",
        "    internal static class PngFixtureData",
        "    {",
        "        public static readonly (string Name, int Width, int Height, string Png, string Rgba)[] Cases =",
        "        {",
    ]
    for name, w, h, png, exp in fixtures:
        lines.append(f'            ("{name}", {w}, {h}, "{png}", "{exp}"),')
    lines += ["        };", "    }", "}", ""]
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print(f"{len(fixtures)} fixtures -> {OUT}")


if __name__ == "__main__":
    main()
