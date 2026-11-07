# 0.6.1 verification

This release adds a standalone web launcher, macOS browser support, Safari's read-only import / ZIP export path, and external video drops onto desktop and browser players.

Validated locally on Windows 11:

- Desktop build and smoke test, including H.264/AAC, HEVC, clip history, preferences and dropped-file activation. External drops leave source files intact. The embedded mpv child disables automatic file loading and forwards dropped-file notifications to Viewcut so the sidebar and clip state stay synchronized.
- Native AOT Windows launcher: actual automatic startup of installed Chrome and Edge using isolated profiles, embedded assets, repeat-launch reuse and idle shutdown. The EXE includes its icon and needs no installed Node/.NET runtime.
- Eight JavaScript media/model/portable tests: grouped history, naming, bounded metadata reads, B frames/AAC, HEVC, keyframe alignment, sample preservation, cancellation, 64-bit offsets and ZIP64.
- Installed Chrome and Edge: external drop, directory browsing, clip edits, live frame preview, direct exports, file operations, preferences and five display scale factors.
- Windows WebKit: real file import, Worker packet-copy / ZIP generation and preferences. Its native video decoding could not be used for acceptance; no video-playback success is claimed for Windows WebKit.

macOS 15 automated machines:

- Apple Silicon and Intel: full media tests, WebKit playback/editing/preview/ZIP flow, installed Chrome and Edge end-to-end tests, native AOT startup, service reuse / exit and app ZIP construction. Example successful run: https://github.com/Uranatom/Viewcut/actions/runs/42356475675
- Real Safari via SafariDriver: H.264 playback, multiple marked clips, a real mouse hover producing a decoded preview frame, and Worker ZIP preparation passed on Apple Silicon. The successful native test is https://github.com/Uranatom/Viewcut/actions/runs/42357716659 . Native picker permission prompts and the OS download/save dialog were not automated.
- Native Safari is distinct from WebKit. Earlier synthetic-click / hover tests did not reliably drive the browser; the acceptance test now uses WebDriver click/pointer actions and captures a browser screenshot. Preview rendering also waits for decoded data, handles transient unavailable frames and keeps its source video renderable for Safari.

Mac apps are development builds without Developer ID notarization. Gatekeeper first-launch approval and actual removable SD-card behavior still need user-machine acceptance. Windows 10 22H2 machine acceptance remains pending; the compatibility code and font/DPI fallback checks from 0.6.0 are retained.

Binary archives are assembled under `artifacts/Release/`, with stable asset filenames matching both README download tables and a SHA-256 manifest. Release publication also requires the project owner to choose the distribution license for the GPL libmpv desktop dependency; dependency license texts are prepared separately from Viewcut's own license.
