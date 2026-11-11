#!/usr/bin/env python3
"""
Genera el ícono de DeployWebJobs (flecha de "subir" sobre una base, en un cuadrado azul redondeado).
Sin dependencias: solo la biblioteca estándar de Python.

Uso:  python tools/make-icon.py
Salida: src/DeployWebJobs.Cli/Assets/app.ico  y  app-256.png
"""
import math
import os
import struct
import zlib

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "DeployWebJobs.Cli", "Assets")
SIZES = [16, 32, 48, 256]
SUPERSAMPLE = 4  # muestras por eje y por píxel (suaviza los bordes)


def sd_round_rect(x, y, cx, cy, hw, hh, r):
    """Distancia con signo a un rectángulo redondeado (<= 0 = adentro)."""
    qx = abs(x - cx) - (hw - r)
    qy = abs(y - cy) - (hh - r)
    return math.hypot(max(qx, 0.0), max(qy, 0.0)) + min(max(qx, qy), 0.0) - r


def in_triangle(px, py, a, b, c):
    def sign(p1, p2, p3):
        return (p1[0] - p3[0]) * (p2[1] - p3[1]) - (p2[0] - p3[0]) * (p1[1] - p3[1])
    p = (px, py)
    d1, d2, d3 = sign(p, a, b), sign(p, b, c), sign(p, c, a)
    neg = d1 < 0 or d2 < 0 or d3 < 0
    pos = d1 > 0 or d2 > 0 or d3 > 0
    return not (neg and pos)


def sample(x, y):
    """Color RGBA (0-255) en el punto (x, y) de un lienzo normalizado 0..1."""
    if sd_round_rect(x, y, 0.5, 0.5, 0.47, 0.47, 0.19) > 0:
        return (0, 0, 0, 0)

    # Degradé vertical azul
    t = min(max((y - 0.03) / 0.94, 0.0), 1.0)
    top, bottom = (33, 150, 243), (13, 71, 161)
    color = tuple(round(top[i] + (bottom[i] - top[i]) * t) for i in range(3))

    white = (255, 255, 255)
    in_head = in_triangle(x, y, (0.50, 0.17), (0.27, 0.47), (0.73, 0.47))
    in_shaft = 0.435 <= x <= 0.565 and 0.44 <= y <= 0.70
    in_base = sd_round_rect(x, y, 0.50, 0.795, 0.235, 0.035, 0.035) <= 0
    if in_head or in_shaft or in_base:
        color = white
    return (*color, 255)


def render(size):
    rows = []
    n = SUPERSAMPLE
    for py in range(size):
        row = bytearray()
        for px in range(size):
            r = g = b = a = 0
            for sy in range(n):
                for sx in range(n):
                    cr, cg, cb, ca = sample((px + (sx + 0.5) / n) / size, (py + (sy + 0.5) / n) / size)
                    r += cr * ca
                    g += cg * ca
                    b += cb * ca
                    a += ca
            total = n * n
            if a == 0:
                row += bytes((0, 0, 0, 0))
            else:
                row += bytes((round(r / a), round(g / a), round(b / a), round(a / total)))
        rows.append(bytes(row))
    return rows


def encode_png(size, rows):
    def chunk(tag, data):
        body = tag + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    raw = b"".join(b"\x00" + row for row in rows)
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def encode_dib(size, rows):
    """Formato BMP-dentro-de-ICO (el clásico, compatible con todo)."""
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    pixels = bytearray()
    for row in reversed(rows):  # los BMP van de abajo hacia arriba
        for i in range(0, len(row), 4):
            r, g, b, a = row[i:i + 4]
            pixels += bytes((b, g, r, a))
    mask = bytes(((size + 31) // 32) * 4 * size)  # máscara AND vacía (la transparencia va en el alfa)
    return header + bytes(pixels) + mask


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    frames = []
    png256 = None
    for size in SIZES:
        rows = render(size)
        if size >= 256:
            data = encode_png(size, rows)
            png256 = data
        else:
            data = encode_dib(size, rows)
        frames.append((size, data))

    count = len(frames)
    offset = 6 + 16 * count
    entries = b""
    blobs = b""
    for size, data in frames:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        blobs += data
        offset += len(data)

    ico = struct.pack("<HHH", 0, 1, count) + entries + blobs
    with open(os.path.join(OUT_DIR, "app.ico"), "wb") as f:
        f.write(ico)
    with open(os.path.join(OUT_DIR, "app-256.png"), "wb") as f:
        f.write(png256)
    print(f"OK: app.ico ({len(ico)} bytes, tamaños {SIZES}) y app-256.png en {os.path.normpath(OUT_DIR)}")


if __name__ == "__main__":
    main()
