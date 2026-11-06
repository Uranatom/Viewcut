#!/usr/bin/env bash
set -euo pipefail
# Official FFmpeg source: a35157abb96167fd7308edc92b55408d5bb084b0 (release/8.1).
# Requires a 64-bit MinGW GCC toolchain and GNU make on PATH; run in Git Bash/MSYS2.
source_dir=$(cd "$1" && pwd)
mkdir -p "$2"
prefix=$(cd "$2" && pwd)
cd "$source_dir"
# Source archives have no .git; prevent version.sh from picking up Viewcut's parent repository.
printf '%s\n' '8.1.3-34-ga35157abb9-viewcut' > VERSION
./configure --prefix="$prefix" --target-os=mingw32 --arch=x86_64 --cc=gcc \
  --disable-everything --disable-autodetect --disable-network --disable-doc --disable-debug \
  --disable-x86asm --disable-ffplay --enable-ffmpeg --enable-ffprobe --enable-protocol=file \
  --enable-demuxer=mov --enable-muxer=mp4,mov \
  --enable-parser=h264,hevc,aac,ac3,mpegaudio,mpeg4video \
  --enable-bsf=aac_adtstoasc,extract_extradata,h264_mp4toannexb,hevc_mp4toannexb \
  --enable-decoder=h264,hevc,aac,pcm_s16le,pcm_s24le,pcm_s32le,mp3,ac3,eac3,opus \
  --extra-ldflags=-static
"${MAKE:-gmake}" -j"${JOBS:-8}"
"${MAKE:-gmake}" install
cp COPYING.LGPLv2.1 "$prefix/bin/FFmpeg-LICENSE.txt"
