# Compact FFmpeg tools

Viewcut copies packets without re-encoding. The release includes only the file protocol,
MOV/MP4 demuxer and muxers, packet parsers, required bitstream filters and common camera
audio/video decoders used during stream analysis. libmpv remains the full playback engine.

Source: official FFmpeg `release/8.1`, commit
`a35157abb96167fd7308edc92b55408d5bb084b0` (8.1.3-34-ga35157abb9).

- Source archive: https://api.github.com/repos/FFmpeg/FFmpeg/tarball/a35157abb96167fd7308edc92b55408d5bb084b0
- Downloaded archive SHA-256: `ead4a26e3ed93228f0b82b90a28d7d63c25a1e3d908b30a4a12d8a7c9e8e8975`
- Compiler used: MinGW-w64 GCC 13.2.0, x86_64 UCRT POSIX SEH (winlibs r8).
- License: LGPL 2.1 or later; `COPYING.LGPLv2.1` is copied alongside the executables.
- No source code changes; `VERSION` is set explicitly for builds from a source archive.
- The full configure command is in `Build-FFmpegLite.sh` and `ffmpeg -version`.

With the extracted source, GNU make and MinGW GCC on PATH, run in Git Bash/MSYS2:

```bash
bash media/Build-FFmpegLite.sh .tools/ffmpeg-source/FFmpeg-FFmpeg-a35157a .tools/ffmpeg-lite
```

The tools are statically linked to avoid extra compiler runtime DLLs. They are not a
general-purpose FFmpeg distribution: unrelated containers, encoders and network protocols
are omitted. H.264/AAC MP4 and HEVC MOV exports are verified with an independent full
FFmpeg build, including complete output decoding and keyframe validation.

The application remains self-contained. WinForms runtime assemblies are not trimmed;
publishing only limits unused satellite translations and omits debug symbols.
