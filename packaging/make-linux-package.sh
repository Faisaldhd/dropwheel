#!/usr/bin/env bash
# Builds <out>/DropWheel-<version>-linux-x64.tar.xz from a published app folder and fills in the AUR PKGBUILD.
#   packaging/make-linux-package.sh <version> <publish-dir> [out-dir] [source-url-or-file]
# The last argument overrides where the PKGBUILD downloads the tarball from (CI uses the local file to test it).
set -euo pipefail
version="$1"
publish="$2"
out="${3:-dist}"
root="$(cd "$(dirname "$0")/.." && pwd)"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT

mkdir -p "$stage/dropwheel/app" "$stage/dropwheel/share" "$out"
cp -a "$publish/." "$stage/dropwheel/app/"
"$publish/dropwheel" integrate --export "$stage/dropwheel/share" > /dev/null
cp "$root/src/FileAction.Linux/Assets/dropwheel.png" "$stage/dropwheel/share/dropwheel.png"

cp "$root/LICENSE" "$root/THIRD-PARTY-NOTICES.md" "$stage/dropwheel/share/"

tarball="DropWheel-$version-linux-x64.tar.xz"
XZ_OPT="-9e -T0" tar -C "$stage" --owner=0 --group=0 -cJf "$out/$tarball" dropwheel
sha="$(sha256sum "$out/$tarball" | cut -d' ' -f1)"
source="${4:-$tarball::https://github.com/Metamorfolog/file_action/releases/download/v$version/$tarball}"

license="LicenseRef-AllRightsReserved"
if [ -f "$root/LICENSE" ] && grep -q "MIT License" "$root/LICENSE"; then license="MIT"; fi

sed -e "s|@VERSION@|$version|" \
    -e "s|@SOURCE@|$source|" \
    -e "s|@SHA256@|$sha|" \
    -e "s|@LICENSE@|$license|" \
    "$root/packaging/aur/PKGBUILD.in" > "$out/PKGBUILD"
echo "$out/$tarball"
echo "$out/PKGBUILD"
