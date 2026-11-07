# 0.6.0 validation

Validated on the development **Windows 11** machine. Windows 10 22H2 x64 is a compatibility target awaiting actual-machine acceptance, as described in [windows-compatibility.md](windows-compatibility.md).

- Desktop Release build: no warnings or errors.
- Full desktop smoke test passed with `VIEWCUT_TEST_CLASSIC_FONTS=1`, including playback, H.264/AAC and HEVC exports, hover preview, continuous divider dragging, clip editing, history, file operations and preferences.
- Windows 10 font/icon fallback layout checks passed in light and dark modes at 100, 125, 150, 175 and 200% DPI, in wide and narrow windows.
- Standalone Windows Shell file-operation regression passed. The test explicitly activates its hidden test window before clipboard assertions and ends native label editing with `LVM_EDITLABELW(-1)`. Clipboard/focus checks require an interactive desktop and may be affected by concurrent desktop activity.
- Six web model/media tests passed. They cover grouped history, naming, H.264 B frames + AAC, keyframe alignment, HEVC MOV, fast-start MP4, original packet payload preservation, bounded reads, cancellation and a virtual source with 64-bit offsets above 4 GB. Exported fixtures were fully decoded with an independent full FFmpeg build.
- Installed Google Chrome **156.0.8078.81** and Microsoft Edge **156.0.4314.58** passed the web end-to-end tests using independent temporary browser profiles. The configured extension control tool could not initialize in this session; Playwright used the installed browsers directly.
- Browser tests exercised single-click selection / double-click playback, multiple clips, duplicate-name rejection, endpoint dragging, live preview, undo/redo, Ctrl selection, real filesystem copy/delete through OPFS handles, directory refresh, thumbnails, preferences and five device scale factors. The native folder picker was substituted by the test; manual picker permission UX was not automated.
- Both browsers also opened an actual **1,681,704,849-byte H.264/AAC video** through a native file input and exported a selected range locally; both outputs passed full FFmpeg decoding. No source file was modified.
- The 0.6.0 Windows installer was compiled successfully. The web runtime archive contains static application files, a loopback development server and launchers; no video fixtures or browser profile data are included.

Local logs and screenshots are under `.tools/win10-*.log`, `artifacts/win10-fonts-*` and `artifacts/web-browsers/` (ignored by Git). The browser test runner writes version/results JSON there. The Windows installer is under `artifacts/Setup/`, and the browser runtime ZIP under `artifacts/Web/`.
