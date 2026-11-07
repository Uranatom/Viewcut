# Viewcut 0.6.1

English · [简体中文](README.md)

License: [GNU GPL v3.0](LICENSE) (`GPL-3.0-only`). Third-party components retain their own licenses; see [dependency notices](media/THIRD-PARTY-NOTICES.txt).

Viewcut is a lightweight video player and rough-cut tool targeting Windows 10 22H2 and Windows 11 x64. Windows 10 machine acceptance is pending; see [compatibility notes](docs/windows-compatibility.md). Review
camera or drone MP4/MOV footage, mark the parts worth keeping, and export all selected
clips from a video in one batch.

## Latest downloads

| Edition | Download |
| --- | --- |
| Windows desktop · Win10 22H2 / Win11 x64 | [Setup wizard](https://github.com/Uranatom/Viewcut/releases/latest/download/Viewcut-Setup-x64.exe) |
| Web launcher · Windows x64 | [Single-file EXE](https://github.com/Uranatom/Viewcut/releases/latest/download/Viewcut-Web-Windows.exe) |
| Web launcher · Apple Silicon Mac | [Mac app ZIP](https://github.com/Uranatom/Viewcut/releases/latest/download/Viewcut-Web-macOS-AppleSilicon.zip) |
| Web launcher · Intel Mac | [Mac app ZIP](https://github.com/Uranatom/Viewcut/releases/latest/download/Viewcut-Web-macOS-Intel.zip) |

[All release files](https://github.com/Uranatom/Viewcut/releases/latest) · [Static web deployment bundle](https://github.com/Uranatom/Viewcut/releases/latest/download/Viewcut-Web-Static.zip)

## Desktop Quick Start

1. Download and run the Windows installer. Choose the install folder and optional desktop shortcut, then open Viewcut.
2. **Drop an MP4 / MOV directly onto the player**, or double-click a video in the sidebar.
3. Press Space to play/pause, **I** to mark in and **O** to mark out. Repeat to select several clips. Marking out does not export automatically.
4. Choose an export folder, then click the timeline's **export icon** to export every clip from the current video. Hover over icons for their names and shortcuts.

## Web Quick Start

1. Download `Viewcut-Web-Windows.exe`, or unzip the appropriate Mac ZIP to get `Viewcut Web.app`.
2. **Double-click the app icon. Your browser opens automatically.** No command line, Node installation, .NET installation or manual server startup is required.
3. Drop a video onto the player, or use the sidebar to open a folder/video. Chrome, Edge and macOS Safari are supported targets.
4. Mark clips with I / O, then export. **Chrome / Edge** write to an authorized directory. **Safari** prepares a ZIP; click Download and extract it to get the clips.
5. On Mac use **Command+click** for multiple selection, **Command+Z** to undo and **Command+Shift+Z** to redo. Change language and theme in Preferences.

Videos stay on your device. The Mac app is currently an unnotarized test build and may require first-launch approval in macOS settings. See [launcher notes](launcher/README.md) for compatibility and validation. Windows 10 machine acceptance remains pending.

## Detailed usage

1. Run `artifacts/Setup/Viewcut-0.6.1-Setup-x64.exe` after building, or launch the
   portable `artifacts/Viewcut/Viewcut.exe`. The installer supports a custom installation
   folder, optional desktop shortcut and optional Explorer context-menu commands.
2. Browse folders in the right sidebar. Double-click a video to load it. A single click
   selects a file. Choose between large icons and details view.
3. Press Space to play/pause, Left/Right to seek three seconds, and P/N for the previous/next video.
4. Press I to mark a clip start and O to mark its end. Marking clips saves the draft;
   click **Export all clips** when you have finished reviewing the video.
5. Hover over the timeline to preview a frame. Drag a clip's handles to adjust its
   boundaries. Scroll to zoom; Shift-scroll to pan; **Fit** shows the whole video.
6. Right-click a clip to rename or delete it. Ctrl-click to select multiple clips;
   Delete or Backspace removes selected markers. A custom clip name is used for its
   export filename. Duplicate names are reported.
7. Resize the sidebar and timeline using their dividers. The current video is highlighted.

Lossless cuts copy the video and audio streams without re-encoding. The beginning may
include frames from an earlier keyframe. Source videos are preserved.

All action buttons use matching icons. Hover over an icon for its name and shortcut.
Clip and export counts appear as badges.

## Undo and redo

Use the timeline undo/redo icons, Ctrl+Z, Ctrl+Y or Ctrl+Shift+Z. These cover clip creation,
individual or batch deletion, renaming and boundary adjustments. A batch deletion or a
complete handle drag is one action. A new edit clears the redo branch.

History is separate for each video and kept for the current run: up to 100 edits for each
of the 64 most recently used videos. Saved drafts persist after closing; undo history
does not. Export jobs and exported videos are preserved. Ctrl+Z in address, search and
filename text fields remains a text-editing command.

## Preferences

Open **Preferences** beside **Choose save folder** and **Open save folder**.
Switch between **简体中文 / English** and **Dark / Light / Use system setting**.
Changes apply immediately and persist across launches, without reloading the video or
changing saved clips or export jobs. User filenames and clip names are never translated.

The light appearance uses Windows 11-style light surfaces, dark text and blue accents.
The video area remains black. System appearance follows Windows application theme changes.
Both languages and appearances are checked at 100%, 125%, 150%, 175% and 200% display scaling.

Windows-provided context menus, file-operation progress windows and system dialogs follow
the Windows display language.

## Sidebar file management

The sidebar supports marquee selection, Ctrl-click, Shift-click, Select all, Cut/Copy/Paste,
drag and drop, inline and batch rename, new folders, properties, sorting and automatic refresh.
Right-click files to access their native Windows context menu and installed Shell extensions.

- Ctrl+A selects all; Ctrl+C / Ctrl+X / Ctrl+V copy, cut and paste.
- Delete sends files to the Recycle Bin where supported; Shift+Delete requests permanent
  deletion with confirmation. Delete on the timeline or Clips page removes markers only.
- F2 renames; Ctrl+Shift+N creates a folder; Alt+Enter opens properties; F5 refreshes.
- Ctrl+L edits the folder address; Ctrl+F focuses search; Alt+Left/Right navigates history.

## Building

The repository contains source code. Generated installers, SDKs and media binaries are
not committed. Use .NET 10 SDK, x64 libmpv at `.tools/mpv/libmpv-2.dll`, and the compact
FFmpeg tools described in [media/README.md](media/README.md).

```powershell
pwsh -NoProfile -File ./Build.ps1
pwsh -NoProfile -File ./installer/Build-Setup.ps1
```

The installer build also requires Inno Setup 6.7. It bundles the .NET Desktop Runtime and
media dependencies for offline installation. The compact FFmpeg/ffprobe pair is about
11 MB; the full libmpv playback engine is retained.

Session data is stored in `%LOCALAPPDATA%/Viewcut/session.json`. Uninstall removes installed
files, shortcuts and registered Explorer commands. Optional settings cleanup preserves
source videos and exported clips. Third-party build sources and notices are listed in
[dependency notices](media/THIRD-PARTY-NOTICES.txt) and [the Chinese documentation](README.md).
