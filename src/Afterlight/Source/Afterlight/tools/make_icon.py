"""Generate the original geometric app icon using only the Python standard library."""
import pathlib, struct, zlib

root = pathlib.Path(__file__).resolve().parent.parent / "Assets"
root.mkdir(exist_ok=True)
size = 128
pixels = bytearray()
for y in range(size):
    pixels.append(0)
    for x in range(size):
        color = (15, 29, 37, 255)
        if 21 <= x <= 106 and 20 <= y <= 108:
            if y >= 25 and abs(x - 64) <= (y - 25) * 0.52 and y <= 99:
                color = (101, 233, 213, 255)
            if 58 <= x <= 70 and 74 <= y <= 100:
                color = (15, 29, 37, 255)
        if 23 <= x <= 104 and 111 <= y <= 114:
            color = (255, 125, 77, 255)
        pixels.extend(color)
def chunk(name, data):
    return struct.pack(">I", len(data)) + name + data + struct.pack(">I", zlib.crc32(name + data) & 0xffffffff)
png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(bytes(pixels))) + chunk(b"IEND", b"")
(root / "icon.png").write_bytes(png)
ico = struct.pack("<HHH", 0, 1, 1) + struct.pack("<BBBBHHII", size, size, 0, 0, 1, 32, len(png), 22) + png
(root / "afterlight.ico").write_bytes(ico)
