# Windows 10 / Windows 11 compatibility

Viewcut 0.6.0 uses one codebase and installer for Windows 10 **22H2 (build 19045) x64** and Windows 11 x64. This is the application compatibility target; it is not a claim that every Windows 10 edition is in Microsoft's current .NET support lifecycle. See the [.NET 10 support matrix](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md).

Changes:

- Inno Setup minimum build changes from 22000 to 19045; installation paths, shortcut icons, Explorer menu registration and uninstall behavior are retained.
- `UiFonts` selects the installed Windows 11 variable fonts / Fluent icons where available, and Segoe UI / Segoe MDL2 Assets on Windows 10. Newer export and drive glyphs have Windows 10 alternatives.
- Title-bar dark mode tries DWM attribute 20 and falls back to 19.
- Per-monitor DPI handling, managed buffering and coalesced divider movement are shared by both systems.
- libmpv and native FFmpeg remain the playback / export engines. Upstream mpv lists [Windows 10 1607 or newer](https://github.com/mpv-player/mpv#system-requirements); the shipped binary still needs verification on the actual target OS and GPU driver.

## Automated compatibility path checks

On the development Windows 11 system, `VIEWCUT_TEST_CLASSIC_FONTS=1` forces Windows 10 font and icon paths. Run the normal smoke test and `--layout` regression with that variable; use `VIEWCUT_TEST_THEME=light` or `dark`. Layout regression exercises 96 / 120 / 144 / 168 / 192 DPI and narrow / wide windows without changing the user's display settings.

This cannot emulate Windows 10 native libraries, Shell, codecs or GPU drivers. Passing on Windows 11 is not Windows 10 certification.

## Windows 10 machine acceptance checklist

1. Use a clean Windows 10 22H2 x64 machine or VM without the Windows 11 fonts and without a preinstalled .NET runtime. Record OS build, GPU and driver.
2. Install using a custom path. Check optional desktop shortcut icon, Start menu shortcut and MP4 / MOV / folder context entries.
3. Verify Chinese / English and dark / light / system appearance at 100 / 125 / 150 / 175 / 200%, including moving between monitors.
4. Open H.264 + AAC MP4, HEVC MOV, and real camera files. Verify startup, playback, pause, seeking, hover preview, resizing and recovery after a bad file.
5. Mark multiple clips, adjust handles, rename, delete multiple clips, undo / redo, switch files, restart, then export. Decode exports independently with FFmpeg and inspect audio sync and keyframe alignment.
6. Verify Shell copy / move / rename / recycle-bin / permanent delete using disposable fixtures, and external directory refresh.
7. Uninstall and verify removal of application files, shortcuts and installed context-menu keys. Confirm source and exported videos remain; test both retention and removal of settings.

Windows 10 real-machine acceptance is **pending** until these steps run on Windows 10. No Windows 10 guest is assumed to be available on the development machine.
