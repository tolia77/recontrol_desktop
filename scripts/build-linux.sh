#!/usr/bin/env bash
# scripts/build-linux.sh
# One-command Linux build: ensures .env, fetches FFmpeg .so libs, self-contained-publishes
# linux-x64, stages the packaging tree, and produces .deb, .rpm, and .tar.gz artifacts.
#
# Prerequisites (Linux x64 build host):
#   - .NET 10 SDK  (https://dotnet.microsoft.com/download)
#   - gem install fpm  (https://fpm.readthedocs.io)
#   - rpm-build (sudo apt-get install rpm  OR  sudo dnf install rpm-build)
#   - curl, tar, realpath (standard on Debian/Ubuntu/Fedora)
#
# Usage: bash scripts/build-linux.sh
# Run from any directory; all paths are resolved relative to this script's location.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CSPROJ="$REPO_ROOT/ReControl.Desktop/ReControl.Desktop.csproj"
PUBLISH_DIR="$REPO_ROOT/publish-linux"
STAGING_DIR="$REPO_ROOT/staging"
DIST_DIR="$REPO_ROOT/dist"
INSTALLER_LINUX="$REPO_ROOT/installer/linux"
ASSETS_DIR="$REPO_ROOT/ReControl.Desktop/Assets"

VERSION="1.0.0"

echo "=== ReControl Desktop — Linux build ==="
echo "  Repo:    $REPO_ROOT"
echo "  Version: $VERSION"

# --- Step 1: Ensure .env exists ---
ENV_FILE="$REPO_ROOT/ReControl.Desktop/.env"
ENV_EXAMPLE="$REPO_ROOT/ReControl.Desktop/.env.prod.example"
if [ ! -f "$ENV_FILE" ]; then
  if [ ! -f "$ENV_EXAMPLE" ]; then
    echo "ERROR: Neither .env nor .env.prod.example found in ReControl.Desktop/" >&2
    exit 1
  fi
  echo "  .env not found -- copying .env.prod.example"
  cp "$ENV_EXAMPLE" "$ENV_FILE"
else
  echo "  .env already present"
fi

# --- Step 2: Fetch FFmpeg .so libs ---
echo ""
echo "=== Fetching FFmpeg .so libs ==="
bash "$SCRIPT_DIR/fetch-ffmpeg-linux.sh"

# --- Step 3: dotnet publish (self-contained linux-x64) ---
echo ""
echo "=== Publishing ReControl.Desktop (linux-x64, self-contained) ==="
dotnet publish "$CSPROJ" \
  -c Release \
  -r linux-x64 \
  --self-contained \
  -o "$PUBLISH_DIR"

# Defensive A2 fix: ensure the ELF apphost is executable regardless of whether
# dotnet publish set the bit correctly (A2 unverified on this build host).
chmod +x "$PUBLISH_DIR/ReControl.Desktop"
echo "  chmod +x publish-linux/ReControl.Desktop (defensive A2 guard)"

# --- Step 3b: Defensive A3 fix — flatten runtimes/linux-x64/native/*.so* into the publish root ---
# A3 (libSkiaSharp.so / libHarfBuzzSharp.so location) was NOT verified on a clean Linux build.
# If dotnet publish leaves them under runtimes/linux-x64/native/ instead of the app root,
# the app will fail to load Skia at runtime. Unconditionally flatten: this is a no-op if the
# files are already flat, and a fix if they are not.
if compgen -G "$PUBLISH_DIR/runtimes/linux-x64/native/*.so*" >/dev/null 2>/dev/null; then
  echo "  Flattening runtimes/linux-x64/native/*.so* into publish root (defensive A3 guard)"
  cp -an "$PUBLISH_DIR/runtimes/linux-x64/native/"*.so* "$PUBLISH_DIR/" 2>/dev/null || true
else
  echo "  runtimes/linux-x64/native/ absent or empty -- skipping A3 flatten (already flat)"
fi

# --- Step 4: Verify FFmpeg .so presence in publish output ---
echo ""
echo "=== Verifying FFmpeg .so in publish output ==="
if ! compgen -G "$PUBLISH_DIR/ffmpeg/libavcodec.so*" >/dev/null 2>/dev/null; then
  echo "ERROR: publish-linux/ffmpeg/libavcodec.so* not found." >&2
  echo "       Ensure the csproj linux-x64 ItemGroup (ffmpeg/**/*.so*) is present" >&2
  echo "       and that fetch-ffmpeg-linux.sh populated ReControl.Desktop/ffmpeg/ correctly." >&2
  exit 1
fi
echo "  publish-linux/ffmpeg/libavcodec.so* -- OK"

# --- Step 5: Stage packaging tree ---
echo ""
echo "=== Staging packaging tree ==="
rm -rf "$STAGING_DIR"

# 5a: App binaries -> /usr/lib/recontrol-desktop/
LIB_DEST="$STAGING_DIR/usr/lib/recontrol-desktop"
mkdir -p "$LIB_DEST"
cp -a "$PUBLISH_DIR"/. "$LIB_DEST"/

# 5b: Wrapper -> /usr/bin/recontrol-desktop (0755)
BIN_DEST="$STAGING_DIR/usr/bin"
mkdir -p "$BIN_DEST"
cp "$INSTALLER_LINUX/recontrol-desktop" "$BIN_DEST/recontrol-desktop"
chmod 755 "$BIN_DEST/recontrol-desktop"

# 5c: .desktop entry -> /usr/share/applications/
APPS_DEST="$STAGING_DIR/usr/share/applications"
mkdir -p "$APPS_DEST"
cp "$INSTALLER_LINUX/recontrol-desktop.desktop" "$APPS_DEST/"

# 5d: Icon PNG set -> /usr/share/icons/hicolor/<size>x<size>/apps/
for size in 16 32 48 64 128 256 512; do
  ICON_SRC="$ASSETS_DIR/recontrol-${size}.png"
  ICON_DEST="$STAGING_DIR/usr/share/icons/hicolor/${size}x${size}/apps"
  mkdir -p "$ICON_DEST"
  if [ -f "$ICON_SRC" ]; then
    cp "$ICON_SRC" "$ICON_DEST/recontrol-desktop.png"
  else
    echo "  WARNING: $ICON_SRC not found -- skipping ${size}x${size} icon"
  fi
done

echo "  Staging tree:"
find "$STAGING_DIR/usr" -maxdepth 3 -type d | sort | sed 's|^|    |'

# --- Step 6: Create output directory ---
mkdir -p "$DIST_DIR"

# --- Step 7: fpm .deb ---
echo ""
echo "=== Building .deb ==="
fpm -s dir -t deb \
  --name recontrol-desktop \
  --version "$VERSION" \
  --architecture amd64 \
  --description "ReControl Desktop -- self-contained remote desktop client" \
  --maintainer "ReControl" \
  --url "https://port3003.kokhan.me" \
  --depends "libx11-6" \
  --depends "libice6" \
  --depends "libsm6" \
  --depends "libfontconfig1" \
  --after-install "$INSTALLER_LINUX/postinstall.sh" \
  --package "$DIST_DIR/" \
  -C "$STAGING_DIR" \
  usr

# --- Step 8: fpm .rpm ---
echo ""
echo "=== Building .rpm ==="
# Note: requires rpm-build on the build host (apt install rpm OR dnf install rpm-build)
fpm -s dir -t rpm \
  --name recontrol-desktop \
  --version "$VERSION" \
  --architecture x86_64 \
  --description "ReControl Desktop -- self-contained remote desktop client" \
  --maintainer "ReControl" \
  --url "https://port3003.kokhan.me" \
  --depends "libX11" \
  --depends "libICE" \
  --depends "libSM" \
  --depends "fontconfig" \
  --after-install "$INSTALLER_LINUX/postinstall.sh" \
  --package "$DIST_DIR/" \
  -C "$STAGING_DIR" \
  usr

# --- Step 9: tar.gz fallback ---
echo ""
echo "=== Building .tar.gz fallback ==="
tar -czf "$DIST_DIR/recontrol-desktop-linux-x64.tar.gz" -C "$PUBLISH_DIR" .

# --- Done ---
echo ""
echo "=== Build complete ==="
echo "  Artifacts:"
ls -lh "$DIST_DIR/"*.deb "$DIST_DIR/"*.rpm "$DIST_DIR/"*.tar.gz 2>/dev/null | sed 's|^|    |'
echo ""
echo "  Expected filenames:"
echo "    dist/recontrol-desktop_${VERSION}_amd64.deb"
echo "    dist/recontrol-desktop-${VERSION}-1.x86_64.rpm"
echo "    dist/recontrol-desktop-linux-x64.tar.gz"
