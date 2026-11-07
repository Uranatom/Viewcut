"""Build an executable .app ZIP without requiring Node or .NET on the user's Mac."""
import argparse
import pathlib
import plistlib
import stat
import struct
import zipfile

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
for index in range(count):
    width, height, _, _, _, _, size, offset = struct.unpack_from("<BBBBHHII", icon, 6 + index * 16)
    data = icon[offset:offset + size]
    if data.startswith(b"\x89PNG"):
        pngs.append((width or 256, data))
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
