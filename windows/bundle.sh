#!/usr/bin/env bash
# Collect uxplay.exe, the DLLs it needs and the GStreamer plugins into a
# self-contained folder that runs on a Windows PC without MSYS2.
#
# Run in an MSYS2 UCRT64 (or MINGW64/CLANGARM64) shell after building uxplay:
#     windows/bundle.sh <build-dir> <output-dir>
# Needs the ntldd package:  pacman -S $MINGW_PACKAGE_PREFIX-ntldd
#
# Resulting layout (UxPlay.exe, the tray app, is added next to "uxplay" separately):
#     <output-dir>/uxplay/bin/uxplay.exe + DLLs
#     <output-dir>/uxplay/lib/gstreamer-1.0/  GStreamer plugins
#     <output-dir>/uxplay/libexec/gstreamer-1.0/gst-plugin-scanner.exe
#     <output-dir>/uxplay/etc/fonts/          fontconfig setup (for text overlays)
#     <output-dir>/uxplay/uxplay-console.bat  run uxplay in a console, for troubleshooting

set -euo pipefail

BUILD=${1:?usage: bundle.sh <build-dir> <output-dir>}
OUT=${2:?usage: bundle.sh <build-dir> <output-dir>}
PREFIX=${MINGW_PREFIX:?run this from an MSYS2 UCRT64/MINGW64/CLANGARM64 shell}
HERE=$(cd "$(dirname "$0")" && pwd)

UX="$OUT/uxplay"
rm -rf "$UX"
mkdir -p "$UX/bin" "$UX/lib/gstreamer-1.0" "$UX/libexec/gstreamer-1.0" "$UX/etc"

cp "$BUILD/uxplay.exe" "$UX/bin/"
cp "$PREFIX"/lib/gstreamer-1.0/*.dll "$UX/lib/gstreamer-1.0/"
cp "$PREFIX/libexec/gstreamer-1.0/gst-plugin-scanner.exe" "$UX/libexec/gstreamer-1.0/"
if [ -d "$PREFIX/etc/fonts" ]; then
    cp -r "$PREFIX/etc/fonts" "$UX/etc/"
fi
cp "$HERE/uxplay-console.bat" "$UX/"
cp "$HERE/../LICENSE" "$UX/LICENSE.txt"

# Every DLL from the MSYS2 prefix that the executables and plugins load, recursively.
# ntldd prints lines like:  libglib-2.0-0.dll => C:\msys64\ucrt64\bin\libglib-2.0-0.dll (0x...)
prefix_win=$(cygpath -w "$PREFIX/bin" | tr '[:upper:]' '[:lower:]')
mapfile -t deps < <(
    ntldd -R "$UX"/bin/*.exe "$UX"/libexec/gstreamer-1.0/*.exe "$UX"/lib/gstreamer-1.0/*.dll 2>/dev/null |
    awk '{ print $3 }' |
    while IFS= read -r path; do
        lower=$(printf '%s' "$path" | tr '[:upper:]' '[:lower:]')
        case "$lower" in
            "$prefix_win"\\*) printf '%s\n' "$path" ;;
        esac
    done | sort -u
)
if [ ${#deps[@]} -eq 0 ]; then
    echo "bundle.sh: found no DLL dependencies; is ntldd installed?" >&2
    exit 1
fi
for dll in "${deps[@]}"; do
    cp -n "$(cygpath -u "$dll")" "$UX/bin/"
done

echo "Bundled uxplay.exe with ${#deps[@]} DLLs and $(ls "$UX/lib/gstreamer-1.0" | wc -l) GStreamer plugins"
du -sh "$UX"
