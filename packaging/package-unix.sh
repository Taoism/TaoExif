#!/bin/bash
# Builds the Linux and macOS packages from the publish folders. Runs on Linux (or WSL) because
# Unix file permissions and dpkg-deb are needed:
#   packaging/package-unix.sh <version> <repo root>
# Output in <repo>/release:
#   taoexif_<v>_amd64.deb                 Ubuntu / Debian (installs to /opt/taoexif, /usr/bin/taoexif, menu entry)
#   TaoExif-<v>-linux-x64.tar.gz          portable Linux build
#   TaoExif-<v>-macos-arm64.zip           TaoExif.app for Apple silicon
#   TaoExif-<v>-macos-x64.zip             TaoExif.app for Intel Macs
set -euo pipefail

VERSION="$1"
ROOT="$(cd "$2" && pwd)"
PUB="$ROOT/publish"
OUT="$ROOT/release"
PKG="$ROOT/packaging"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$OUT"

# normalise permissions: dirs/files readable, the app and the exiftool script executable
fix_modes() {
  find "$1" -type d -exec chmod 755 {} +
  find "$1" -type f -exec chmod 644 {} +
  local f
  for f in "${@:2}"; do chmod 755 "$f"; done
}

# ---------- Debian / Ubuntu .deb ----------
DEB="$WORK/deb"
mkdir -p "$DEB/DEBIAN" "$DEB/opt/taoexif" "$DEB/usr/bin" "$DEB/usr/share/applications" "$DEB/usr/share/doc/taoexif"
cp -r "$PUB/linux-x64/." "$DEB/opt/taoexif/"
ln -s /opt/taoexif/TaoExif "$DEB/usr/bin/taoexif"
ln -s /opt/taoexif/taoexif-cli "$DEB/usr/bin/taoexif-cli"
cp "$PKG/linux/taoexif.desktop" "$DEB/usr/share/applications/"
for s in 48 128 256 512; do
  mkdir -p "$DEB/usr/share/icons/hicolor/${s}x${s}/apps"
  cp "$PKG/linux/taoexif-$s.png" "$DEB/usr/share/icons/hicolor/${s}x${s}/apps/taoexif.png"
done
{
  echo "Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/"
  echo "Upstream-Name: TaoExif"
  echo "Source: https://github.com/Taoism/TaoExif"
  echo
  echo "Files: *"
  echo "License: MIT"
  echo
  cat "$ROOT/LICENSE"
  echo
  cat "$ROOT/THIRD-PARTY-NOTICES.txt"
} > "$DEB/usr/share/doc/taoexif/copyright"
fix_modes "$DEB" "$DEB/opt/taoexif/TaoExif" "$DEB/opt/taoexif/taoexif-cli" "$DEB/opt/taoexif/exiftool/exiftool"

SIZE_KB=$(du -sk "$DEB" --exclude=DEBIAN | cut -f1)
cat > "$DEB/DEBIAN/control" <<EOF
Package: taoexif
Version: $VERSION
Architecture: amd64
Maintainer: Taoism <366831+Taoism@users.noreply.github.com>
Installed-Size: $SIZE_KB
Depends: perl, libc6, libgcc-s1, libstdc++6, libfontconfig1, libx11-6, libice6, libsm6, libicu78 | libicu77 | libicu76 | libicu74 | libicu72 | libicu70, libssl3t64 | libssl3
Suggests: libimage-exiftool-perl
Section: graphics
Priority: optional
Homepage: https://github.com/Taoism/TaoExif
Description: dark-mode GUI for ExifTool
 TaoExif views and edits photo and video metadata with Phil Harvey's ExifTool:
 a folder browser with tag columns and thumbnails, metadata editing, an
 OpenStreetMap map for GPS tagging, date shifting, renaming, copying and
 removing metadata, and a command box for raw exiftool commands.
 .
 ExifTool is bundled; an installed libimage-exiftool-perl is used when present.
 The taoexif-cli command offers the same operations for scripts.
EOF
chmod 644 "$DEB/DEBIAN/control"
dpkg-deb --root-owner-group -Zxz --build "$DEB" "$OUT/taoexif_${VERSION}_amd64.deb" >/dev/null
echo "built taoexif_${VERSION}_amd64.deb"

# ---------- portable Linux tarball ----------
T="$WORK/tar/TaoExif-$VERSION-linux-x64"
mkdir -p "$T"
cp -r "$PUB/linux-x64/." "$T/"
cp "$ROOT/LICENSE" "$ROOT/README.md" "$T/"
fix_modes "$WORK/tar" "$T/TaoExif" "$T/taoexif-cli" "$T/exiftool/exiftool"
tar --owner=0 --group=0 -C "$WORK/tar" -czf "$OUT/TaoExif-$VERSION-linux-x64.tar.gz" "TaoExif-$VERSION-linux-x64"
echo "built TaoExif-$VERSION-linux-x64.tar.gz"

# ---------- macOS .app bundles ----------
for rid in osx-arm64 osx-x64; do
  arch=${rid#osx-}
  M="$WORK/mac-$arch"
  APP="$M/TaoExif.app/Contents"
  mkdir -p "$APP/MacOS" "$APP/Resources"
  cp -r "$PUB/$rid/." "$APP/MacOS/"
  cp "$PKG/macos/taoexif.icns" "$APP/Resources/"
  sed "s/@VERSION@/$VERSION/g" "$PKG/macos/Info.plist" > "$APP/Info.plist"
  printf 'APPL????' > "$APP/PkgInfo"
  cp "$ROOT/LICENSE" "$ROOT/README.md" "$M/"
  fix_modes "$M" "$APP/MacOS/TaoExif" "$APP/MacOS/taoexif-cli" "$APP/MacOS/exiftool/exiftool"
  # zip keeping Unix permissions (Finder's Archive Utility honours them)
  python3 - "$M" "$OUT/TaoExif-$VERSION-macos-$arch.zip" <<'PY'
import os, sys, zipfile
src, dst = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for base, dirs, files in os.walk(src):
        dirs.sort()
        for name in sorted(dirs) + sorted(files):
            path = os.path.join(base, name)
            rel = os.path.relpath(path, src)
            st = os.stat(path)
            info = zipfile.ZipInfo(rel + ("/" if os.path.isdir(path) else ""))
            info.create_system = 3
            info.external_attr = (st.st_mode & 0xFFFF) << 16
            if os.path.isdir(path):
                info.external_attr |= 0x10
                z.writestr(info, b"")
            else:
                info.compress_type = zipfile.ZIP_DEFLATED
                with open(path, "rb") as f:
                    z.writestr(info, f.read())
PY
  echo "built TaoExif-$VERSION-macos-$arch.zip"
done
