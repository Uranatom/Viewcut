# Viewcut 0.5.0

English · [简体中文](README.md)

Viewcut is a lightweight video player and rough-cut tool for Windows 11 x64. Review
camera or drone MP4/MOV footage, mark the parts worth keeping, and export all selected
clips from a video in one batch.

## Using Viewcut

1. Run `artifacts/Setup/Viewcut-0.5.0-Setup-x64.exe` after building, or launch the
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
`installer/ThirdParty.txt` and [the Chinese documentation](README.md).
