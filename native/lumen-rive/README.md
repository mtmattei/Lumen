# liblumen_rive: native Rive runtime for Uno

This folder holds a C ABI over [rive-runtime](https://github.com/rive-app/rive-runtime) (MIT), adapted from rive-sharp's interop (MIT, © 2022 Rive).

The runtime does the loading, the state machine and hit testing. Rendering calls back into managed code through one table of function pointers. `Lumen.Rive/SkiaBridge.cs` implements those callbacks with SkiaSharp 3, so the Core draws on the same Skia canvas Uno composes.

```text
lumen-core.riv ─► rive-runtime (C++) ─► LumenCallbacks ─► SkiaBridge (C#) ─► SKCanvas (SKCanvasElement)
                          ▲                                        │
                 inputs / triggers / pointer            events (PulseCompleted, SubsystemSelected{id}, …)
```

## Build

Only the core runtime is compiled: no text, audio, layout or scripting, since `lumen-core.riv` uses none of them. The runtime is pinned to commit `9b958d4` in the build scripts.

| Host | Command | Output |
|---|---|---|
| Linux / macOS | `native/lumen-rive/build.sh` (needs clang, cmake, ninja) | `Lumen.Rive/runtimes/<rid>/native/liblumen_rive.{so,dylib}` |
| Windows | `native/lumen-rive/build.ps1` from a VS Developer PowerShell (needs LLVM clang-cl, cmake, ninja) | `Lumen.Rive/runtimes/win-<arch>/native/lumen_rive.dll` |

The binaries are not committed. Without them, `RiveScene.IsRuntimeAvailable` is false: the app uses the procedural Core and the native tests skip.

## Status (spike)

| Target | State |
|---|---|
| Linux x64 desktop | Built and verified. There are 7 headless tests (`Lumen.Tests/RiveCoreTests`), and the app runs with `engine: Rive runtime` at ~56 fps under Xvfb. |
| Windows / macOS desktop | Build scripts written; not run in this environment. |
| Android / iOS | Not started. It needs NDK / Xcode builds of the same CMake project, packaged as `runtimes/android-*/native` and a static iOS library. |
| WebAssembly | Not started. It needs a static library built with the Emscripten version .NET 10 uses (3.1.56), linked through `NativeFileReference`. Function-pointer callbacks need checking under the WASM AOT/interpreter. |

## Known gaps
- Images, meshes, feathering, clip strokes and layer masks aren't implemented by the bridge. `lumen-core.riv` uses none of them.
- Pointer listener events are reported synchronously inside `pointerDown`/`pointerUp` and cleared by the next advance. `RiveScene.Pointer` collects them immediately.
- Current rive-runtime reads listener types from `ListenerInputType` children. The generator writes those as well as the legacy field.
