#!/usr/bin/env bash
# scripts/fetch-ffmpeg-linux.sh
# Downloads BtbN FFmpeg 7.1 LGPL shared linux64 and extracts .so files to ReControl.Desktop/ffmpeg/
# Run from any directory; all paths are resolved relative to this script's location.
#
# Usage: bash scripts/fetch-ffmpeg-linux.sh
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

URL="https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-n7.1-latest-linux64-lgpl-shared-7.1.tar.xz"
# Assert LGPL build: URL must contain lgpl-shared
case "$URL" in
  *lgpl-shared*) ;;
  *) echo "ERROR: URL does not contain lgpl-shared -- aborting to prevent GPL build" >&2; exit 1 ;;
esac

ARCHIVE="/tmp/ffmpeg-linux64.tar.xz"
DEST="$SCRIPT_DIR/../ReControl.Desktop/ffmpeg"
DEST="$(realpath -m "$DEST")"

echo "Downloading FFmpeg 7.1 LGPL shared (linux64)..."
echo "  URL: $URL"
# -f fails fast on HTTP errors (so a 504/404 page is never saved as a fake archive);
# --retry 5 --retry-delay 3 rides out transient GitHub/CDN 5xx (e.g. 504 Gateway Time-out).
curl -fL --retry 5 --retry-delay 3 "$URL" -o "$ARCHIVE"

# Sanity-check the archive before extracting: a valid xz tar must list cleanly.
# Guards against silently-saved HTML error pages slipping past the download step.
if ! tar -tJf "$ARCHIVE" >/dev/null 2>&1; then
  echo "ERROR: downloaded file is not a valid xz archive (got $(wc -c < "$ARCHIVE") bytes) -- aborting" >&2
  rm -f "$ARCHIVE"
  exit 1
fi

echo "Extracting .so files..."
TMPDIR_EXTRACT="$(mktemp -d)"
tar -xJf "$ARCHIVE" -C "$TMPDIR_EXTRACT"

# .so files are in <archive-root>/lib/*.so*
LIB_DIR="$(find "$TMPDIR_EXTRACT" -maxdepth 2 -name "lib" -type d | head -1)"
if [ -z "$LIB_DIR" ]; then
  echo "ERROR: Could not locate lib/ directory inside the FFmpeg archive at $TMPDIR_EXTRACT" >&2
  rm -rf "$ARCHIVE" "$TMPDIR_EXTRACT"
  exit 1
fi
echo "  Found lib/ at: $LIB_DIR"

mkdir -p "$DEST"
# Use cp -a to preserve versioned symlinks (e.g. libavcodec.so.61.x.y -> libavcodec.so.61)
cp -a "$LIB_DIR"/. "$DEST"/

echo "FFmpeg .so files copied to $DEST"
echo "  Files: $(ls "$DEST")"

rm -rf "$ARCHIVE" "$TMPDIR_EXTRACT"
echo "Cleanup complete."
