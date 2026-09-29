# Builds lumen_rive.dll for Windows into Lumen.Rive/runtimes/win-<arch>/native/.
# Requires: Git, CMake, Ninja, LLVM (clang-cl) — the Rive runtime is developed against clang.
# Run from a "Developer PowerShell for VS 2022" so the Windows SDK and linker are on PATH.
$ErrorActionPreference = 'Stop'

$RiveCommit = '9b958d4'   # keep in sync with build.sh
$Here = $PSScriptRoot
$Deps = Join-Path $Here '.deps/rive-runtime'

if (-not (Test-Path (Join-Path $Deps '.git'))) {
    git clone --filter=blob:none https://github.com/rive-app/rive-runtime.git $Deps
}
git -C $Deps fetch --depth 1 origin $RiveCommit 2>$null
git -C $Deps checkout -q $RiveCommit

$Rid = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$Build = Join-Path $Here ".build/$Rid"

cmake -S $Here -B $Build -G Ninja -DCMAKE_BUILD_TYPE=Release `
    -DCMAKE_C_COMPILER=clang-cl -DCMAKE_CXX_COMPILER=clang-cl -DRIVE_RUNTIME_DIR="$Deps"
cmake --build $Build

$Out = Join-Path $Here "../../Lumen.Rive/runtimes/$Rid/native"
New-Item -ItemType Directory -Force $Out | Out-Null
Copy-Item (Join-Path $Build 'lumen_rive.dll') $Out -Force
Write-Host "built $Out/lumen_rive.dll"
