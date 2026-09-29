#!/usr/bin/env bash
# Builds liblumen_rive for the current Linux/macOS host into Lumen.Rive/runtimes/<rid>/native/.
set -euo pipefail

RIVE_COMMIT="9b958d4"   # rive-app/rive-runtime main, 2026-09-29 (lumen-core.riv verified against 7.4)
HERE="$(cd "$(dirname "$0")" && pwd)"
DEPS="$HERE/.deps/rive-runtime"

if [ ! -d "$DEPS/.git" ]; then
  git clone --filter=blob:none https://github.com/rive-app/rive-runtime.git "$DEPS"
fi
git -C "$DEPS" fetch --depth 1 origin "$RIVE_COMMIT" 2>/dev/null || true
git -C "$DEPS" checkout -q "$RIVE_COMMIT"

case "$(uname -s)-$(uname -m)" in
  Linux-x86_64) RID=linux-x64; LIB=liblumen_rive.so ;;
  Linux-aarch64) RID=linux-arm64; LIB=liblumen_rive.so ;;
  Darwin-arm64) RID=osx-arm64; LIB=liblumen_rive.dylib ;;
  Darwin-x86_64) RID=osx-x64; LIB=liblumen_rive.dylib ;;
  *) echo "unsupported host"; exit 1 ;;
esac

cmake -S "$HERE" -B "$HERE/.build/$RID" -G Ninja -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_CXX_COMPILER="${CXX:-clang++}" -DRIVE_RUNTIME_DIR="$DEPS"
cmake --build "$HERE/.build/$RID"

OUT="$HERE/../../Lumen.Rive/runtimes/$RID/native"
mkdir -p "$OUT"
cp "$HERE/.build/$RID/$LIB" "$OUT/"
echo "built $OUT/$LIB"
