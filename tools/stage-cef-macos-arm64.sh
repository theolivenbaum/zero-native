#!/usr/bin/env bash
# Stage a CEF binary distribution for macOS arm64 under <output>/runtimes/osx-arm64/native.
#
# CefGlue.Common's NuGet feed publishes per-RID `cef.redist.*` packages for
# win-x64, win-arm64, linux-x64, linux-arm64, and osx-x64 — but not osx-arm64.
# Apps that target Apple Silicon therefore have to stage a CEF build manually
# (or pass `CefPlatformOptions.CefDirectory` at runtime). This script automates
# the manual path.
#
# Usage:
#   tools/stage-cef-macos-arm64.sh <publish-dir> [cef-version]
#
# Example:
#   dotnet publish samples/ZeroNative.Sample.Cef -c Release -r osx-arm64 \
#     -o ./out/sample-osx-arm64
#   tools/stage-cef-macos-arm64.sh ./out/sample-osx-arm64
#
# The CEF version should match the major Chromium/CEF version that CefGlue
# binds to. CefGlue.Common 120.x targets `cef_binary_120.*`; pin the exact
# build at https://cef-builds.spotifycdn.com/index.html.

set -euo pipefail

PUBLISH_DIR="${1:?usage: stage-cef-macos-arm64.sh <publish-dir> [cef-version]}"
CEF_VERSION="${2:-120.2.7+gf2e6e64+chromium-120.0.6099.234}"

if [[ ! -d "$PUBLISH_DIR" ]]; then
    echo "publish dir not found: $PUBLISH_DIR" >&2
    exit 1
fi

case "$(uname -s)" in
    Darwin|Linux) ;;
    *) echo "unsupported host platform: $(uname -s)" >&2 ; exit 1 ;;
esac

TARBALL="cef_binary_${CEF_VERSION}_macosarm64.tar.bz2"
URL_TARBALL="${TARBALL//+/%2B}"
URL="https://cef-builds.spotifycdn.com/${URL_TARBALL}"
STAGE_DIR="$PUBLISH_DIR/runtimes/osx-arm64/native"
TMP_DIR="$(mktemp -d -t zero-native-cef-stage-XXXXXX)"
trap 'rm -rf "$TMP_DIR"' EXIT

echo "Downloading $URL"
curl --fail --location --output "$TMP_DIR/$TARBALL" "$URL"

echo "Extracting"
tar -xjf "$TMP_DIR/$TARBALL" -C "$TMP_DIR"

ROOT="$TMP_DIR/${TARBALL%.tar.bz2}"
if [[ ! -d "$ROOT" ]]; then
    # Some builds extract to a slightly different folder; pick the first match.
    ROOT="$(find "$TMP_DIR" -maxdepth 1 -type d -name "cef_binary_*_macosarm64" | head -1)"
fi
if [[ -z "$ROOT" || ! -d "$ROOT" ]]; then
    echo "could not locate the extracted CEF folder under $TMP_DIR" >&2
    exit 1
fi

mkdir -p "$STAGE_DIR" "$STAGE_DIR/Resources"
# cp -R preserves symlinks and directory structure on both macOS and Linux.
cp -R "$ROOT/Release/." "$STAGE_DIR/"
cp -R "$ROOT/Resources/." "$STAGE_DIR/Resources/"

echo "Staged CEF $CEF_VERSION into $STAGE_DIR"
echo
echo "Tip: pass --self-contained=true to dotnet publish so the runtimes/ folder"
echo "is preserved next to the executable. If you publish without self-contained,"
echo "set CefPlatformOptions.CefDirectory to point at this folder at runtime."
