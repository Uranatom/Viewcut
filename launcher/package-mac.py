"""Build an executable .app ZIP without requiring Node or .NET on the user's Mac."""
import argparse
import pathlib
import plistlib
import stat
import struct
import zipfile
import zlib

parser = argparse.ArgumentParser()
parser.add_argument("rid", choices=["osx-arm64", "osx-x64"])
parser.add_argument("--output", default="artifacts/WebLauncher")
args = parser.parse_args()
repo = pathlib.Path(__file__).resolve().parent.parent
output = pathlib.Path(args.output).resolve()
binary = output / args.rid / "Viewcut.Web"
icon = (repo / "installer/Viewcut.ico").read_bytes()
count = struct.unpack_from("<H", icon, 4)[0]
pngs = []
dibs = []
for index in range(count):
    width, height, _, _, _, _, size, offset = struct.unpack_from("<BBBBHHII", icon, 6 + index * 16)
    data = icon[offset:offset + size]
    if data.startswith(b"\x89PNG"):
        pngs.append((width or 256, data))
    else:
        dibs.append((width or 256, data))
if not pngs:
    # Viewcut's Windows ICO uses uncompressed 32-bit DIBs. Convert the existing
    # pixels to PNG with the standard library, without introducing a build SDK.
    width, dib = max(dibs)
    header, dib_width, double_height, planes, depth, compression = struct.unpack_from("<IiiHHI", dib)
    height = abs(double_height) // 2
    if depth != 32 or compression != 0 or dib_width != width:
        raise ValueError("Expected an uncompressed 32-bit icon")
    pixels = dib[header:header + width * height * 4]
    has_alpha = any(pixels[3::4])
    mask = dib[header + width * height * 4:]
    mask_stride = ((width + 31) // 32) * 4
    rows = []
    for y in range(height):
        source_y = height - 1 - y if double_height > 0 else y
        row = bytearray([0])
        for x in range(width):
            b, g, r, alpha = pixels[(source_y * width + x) * 4:(source_y * width + x + 1) * 4]
            transparent = len(mask) >= mask_stride * height and mask[source_y * mask_stride + x // 8] & (128 >> (x % 8))
            row.extend((r, g, b, 0 if transparent else alpha if has_alpha else 255))
        rows.append(row)
    def chunk(kind, payload):
        return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(b"".join(rows))) + chunk(b"IEND", b"")
    pngs.append((width, png))
_, png = max(pngs)
icon_type = {16: b"icp4", 32: b"icp5", 64: b"icp6", 128: b"ic07", 256: b"ic08", 512: b"ic09", 1024: b"ic10"}[max(pngs)[0]]
icns = b"icns" + struct.pack(">I", len(png) + 16) + icon_type + struct.pack(">I", len(png) + 8) + png
plist = plistlib.dumps({
    "CFBundleIdentifier": "app.viewcut.web", "CFBundleName": "Viewcut Web",
    "CFBundleDisplayName": "Viewcut Web", "CFBundleExecutable": "Viewcut",
    "CFBundlePackageType": "APPL", "CFBundleVersion": "0.6.1",
    "CFBundleShortVersionString": "0.6.1", "CFBundleIconFile": "Viewcut.icns",
    "LSMinimumSystemVersion": "15.0", "LSUIElement": True,
    "LSArchitecturePriority": ["arm64" if args.rid.endswith("arm64") else "x86_64"],
})
files = {
    "Viewcut Web.app/Contents/Info.plist": (plist, 0o644),
    "Viewcut Web.app/Contents/MacOS/Viewcut": (binary.read_bytes(), 0o755),
    "Viewcut Web.app/Contents/Resources/Viewcut.icns": (icns, 0o644),
    "README.txt": ((repo / "launcher/README.md").read_bytes(), 0o644),
    "LICENSE.txt": ((repo / "LICENSE").read_bytes(), 0o644),
    "COPYRIGHT.txt": ((repo / "COPYRIGHT").read_bytes(), 0o644),
    "Viewcut Web.app/Contents/Resources/LICENSE.txt": ((repo / "LICENSE").read_bytes(), 0o644),
    "Viewcut Web.app/Contents/Resources/COPYRIGHT.txt": ((repo / "COPYRIGHT").read_bytes(), 0o644),
}
archive = output / f"Viewcut-Web-0.6.1-{args.rid}.zip"
with zipfile.ZipFile(archive, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
    for name, (data, mode) in files.items():
        info = zipfile.ZipInfo(name)
        info.create_system = 3
        info.external_attr = (stat.S_IFREG | mode) << 16
        info.compress_type = zipfile.ZIP_DEFLATED
        bundle.writestr(info, data)
print(archive)
