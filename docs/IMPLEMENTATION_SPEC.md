# LUMEN — Implementation Spec (v1)

Source of truth for product intent: `spec-kit/` (LUMEN_SPEC, RIVE_CORE_SPEC, ARCHITECTURE, fixtures, acceptance criteria).
This document records how v1 is built in this repository and which decisions were made on the way.

**Core rule: Uno owns truth. The Core presenter expresses truth.**

---

## Key decisions

**Decision:** Render the living Core with a procedural SkiaSharp renderer (`SKCanvasElement`) behind `ILumenCorePresenter`, implementing the exact Rive contract (inputs, triggers, events).
**Reason:** No `lumen-core.riv` exists yet (it must be authored in the Rive editor), and the only public .NET Rive runtime on NuGet (`Rive.RiveSharp 1.0.5-alpha`, Dec 2022) ships Windows-only native binaries against SkiaSharp 2.88, while Uno.Sdk 6.7 uses SkiaSharp 3.119. It cannot run on desktop Linux/macOS, WASM or mobile as-is.
**Tradeoff:** The showcase's "Rive" half is a stand-in until a `.riv` and a compatible runtime exist. The adapter boundary is the same one a Rive presenter would use, so the swap touches one class (`LumenCorePresenter`) plus DI registration.

**Decision:** MVVM with CommunityToolkit.Mvvm and `x:Bind`.
**Reason:** State is a 10 Hz push stream plus imperative controls (sliders, toggles, press/hold). MVUX's feed projection adds allocations per tick and generator risk for no gain here.
**Tradeoff:** No built-in loading/error `FeedView` states; the app has no async data sources that need them.

**Decision:** One persistent shell page; the five experiences are modes of the shell.
**Reason:** The Core must stay alive and continuous across Overview → Explorer → Diagnostics (spatial continuity over page fades). Frame navigation would recreate it.
**Tradeoff:** No deep-linkable routes in v1.

**Decision:** Split domain + simulation + presenter contract into `Lumen.Core` (plain `net10.0`), tested by `Lumen.Tests` (xUnit).
**Reason:** Acceptance criteria require the simulator and domain transitions to run and test without Rive or UI.
**Tradeoff:** One extra project over the spec-kit's single-project sketch.

**Decision:** Neutral token system from the spec on top of the Fluent base theme (no Material).
**Reason:** The spec defines its own neutral palette, type roles and spacing; Material's color roles and filled components contradict "avoid SaaS styling".
**Tradeoff:** Custom styles for the handful of controls used.

---

## Architecture Brief

**Solution structure**
```text
Lumen.sln
  Lumen.Core/                  net10.0, no UI dependency
    Domain/        SystemState, SubsystemId, FaultKind, SystemTelemetry, SubsystemTelemetry
    Simulation/    TelemetrySimulator (deterministic causal model), SystemStateClassifier,
                   CoolingFaultTimeline (fixture), FaultScenarioService, NetworkNodeModel
    Presentation/  LumenVisualState, LumenVisualTrigger, CoreEvent, VisualStateMapper,
                   PointerField, PressGesture, ILumenCorePresenter, CoreEventRouter,
                   ILumenCommands, FirstRunTimeline, OnboardingCoach
  Lumen/                       Uno single project (desktop, wasm, android, ios)
    Presentation/Shell         ShellPage (+ ShellViewModel), developer overlay, pulse coordinator
    Presentation/{Overview,Energy,Explorer,Diagnostics,Network}  context panels (UserControls)
    Rive/                      LumenCorePresenter (adapter), LumenCoreView (procedural renderer)
    Services/                  TelemetryHost (10 Hz loop on UI dispatcher), DeviceSensorService
    Controls/                  InstrumentSlider, InstrumentToggle, TelemetryReadout, StatusRail, Sparkline
    Styles/                    Colors, Typography, Controls, Layout
  Lumen.Tests/                 xUnit on Lumen.Core
```

**State model**
- `TelemetrySimulator.Step(dt)` is a pure deterministic integrator over simulated seconds. No `Random`; ambient variation comes from fixed-period sine terms of sim time.
- Causality: `load ↑ → consumption ↑ → battery drain ↑ → heat ↑ → cooling demand ↑`; `pump efficiency ↓ → coolant pressure ↓ → cooling capacity ↓ → temperature ↑`.
- `SystemStateClassifier` owns every threshold (with hysteresis) and the Recovering path. Faults are injected through `FaultScenarioService`.
- `ShellViewModel` holds the latest `SystemTelemetry` snapshot and exposes formatted properties.

**Data flow**
```text
TelemetryHost (DispatcherQueueTimer, 100 ms)
  → FaultScenarioService.Advance → TelemetrySimulator.Step → SystemStateClassifier
  → ShellViewModel.Apply(snapshot)        (XAML bindings, 10 Hz)
  → VisualStateMapper.Map(snapshot) → ILumenCorePresenter.Apply(LumenVisualState)

LumenCoreView (pointer/press) → CoreEvent → CoreEventRouter → ILumenCommands (ShellViewModel)
```
Pointer movement writes directly to presenter inputs (no XAML property changes), so it never invalidates layout.

**Navigation model:** `ShellViewModel.Screen` enum. Selection line in the nav rail; context panel swaps content; Core switches render mode (Core / Flow / Exploded / Network).

**Services/dependencies:** Uno Extensions Hosting DI. Singletons: `TelemetrySimulator`, `FaultScenarioService`, `NetworkNodeModel`, `ShellViewModel`, `ILumenCorePresenter`. No new third-party packages beyond xUnit for tests.

**Platform constraints**
- Built and verified here: `net10.0-desktop`; `net10.0-browserwasm` requires the `wasm-tools` workload. Android/iOS TFMs stay in the project; they need their workloads to build.
- `SKCanvasElement` ignores XAML `Opacity`; all Core fades are baked into paint alpha.
- `TextBlock.CharacterSpacing` is a no-op on Uno Skia; letterspaced labels use explicit spaced strings.

**Testing/validation**
- Unit: simulator determinism + causality, cooling fixture state sequence, classifier hysteresis, visual mapping ranges, pointer-field zones, press timing, event → command routing, first-run timeline, onboarding coach.
- Build: desktop (and wasm when the workload is present).
- Runtime: launch desktop under Xvfb and capture a screenshot when available.

## Design Brief

- **Visual direction:** scientific instrument × industrial product × organism. Dark neutral canvas; colour only where state demands it (≥90% neutral at Nominal).
- **Tokens:** Canvas `#0B0C0C`, Surface `#111313`, Elevated `#161818`, Structural `#2D302E`, Primary `#F1F2EF`, Secondary `#969A96`, Tertiary `#646864`, Disabled `#454845`. Semantic: Nominal mineral green `#7FB89A`, Information cold blue `#8FB4D8`, Elevated mineral amber `#D6A55C`, Critical hot oxide `#D8653F`, Offline gray `#5A5E5B`.
- **Layout (desktop ≥ 1180 wide):** 3 columns — nav rail 200, Core stage *, context 320; telemetry rail 112 high at the bottom; footer metadata line. The Core stage takes ~45–50% of the window.
- **Tablet (720–1179):** nav collapses to a top selection line; Core central; context panel moves to the lower third.
- **Mobile (< 720):** recomposed remote: Core in the upper ~half, condition line, essential telemetry (2×2), compact nav at the bottom.
- **Typography:** interface sans (platform default, Inter/Geist where installed), telemetry mono (Cascadia/Consolas/Menlo/monospace). Hierarchy through size and spacing, weight stays Normal/SemiLight.
- **Spacing:** 4-px base; only 4, 8, 12, 16, 24, 32, 48, 64, 96.
- **Components:** text buttons with a `›` affordance and a hairline underline on hover; one selection line in the nav; instrument sliders with ticks and a live mono value; readouts = large mono value + small tertiary label; no filled cards, 0–2 px corner radii.

## Interaction Brief

- **First run (≤ 10 s):** 0.0 black → 0.8 point → 1.4 heartbeat → 2.0 hairline scan → 2.8 SYSTEM 01 → 3.2 particles converge → 4.0 nucleus → 4.8 membrane → 5.3 orbitals → 5.8 Power + Environment → 6.5 nav + health → 7.5 SYSTEM INITIALIZED / 6 SUBSYSTEMS ONLINE → 8.5 TOUCH THE CORE. Any key or click skips to interactive. Reduced motion shortens to ~1.5 s.
- **Coach:** TOUCH THE CORE → (contact) → HOLD after 400 ms → RELEASE at charge → pulse → INTERFACE LINK ESTABLISHED → SYSTEM LOAD slider revealed. No modal.
- **Pointer field:** normalized to rendered Core radius: > 300 px passive, 200–300 awareness, 100–200 attraction, 50–100 deformation, < 50 direct.
- **Press:** 0–250 ms contact, 250–800 ms charge, ≥ 800 ms charged. Compression biased toward contact point. Release when charged → radial pulse in the Core, then Uno readouts flash in order of distance from the Core centre.
- **Explode:** double-click/tap, `E`, or *Explore System* separates six nodes; click a node (or arrow keys + Enter) selects it; unrelated nodes recede; the Core offsets; the inspector opens beside the node. Inspector sliders update C# state continuously.
- **Diagnostics:** six faults; injecting one flows through the simulator; *Initiate recovery* clears it and runs Recovering → Nominal.
- **States:** empty (no events → “No active issues”), loading (Booting state), error (presenter failure → status text + controls remain; Core area shows a static label).
- **Feedback:** state badge = glyph + word + colour (never colour only); live region announces state changes.
- **Accessibility:** Core is a focusable automation element with a descriptive name; Space/Enter = press/hold/release; `E` explode/collapse; Ctrl+Shift+D developer overlay; reduced-motion toggle; high-contrast toggle strengthens strokes and text.
- **Runtime verification:** build desktop; run under Xvfb; screenshot after boot; exercise unit tests for all C#-owned behaviour.

## Implementation Plan

1. Domain + deterministic simulator + classifier + cooling fixture + tests.
2. Presenter contract (visual state, triggers, events, pointer field, press gesture, router) + tests.
3. Styles/tokens + controls (readout, sparkline, instrument slider/toggle, status rail).
4. Procedural Core renderer + `LumenCorePresenter` adapter (states, pointer, press/charge/pulse, explode, flow, network modes).
5. Shell: layout, nav, context panels for all five experiences, telemetry rail, 10 Hz host, pulse propagation.
6. First run + coach, accessibility, reduced motion, developer overlay, adaptive layouts, optional tilt.
7. Verify (tests, desktop build, runtime screenshot), document gaps.

## Unresolved Questions

- Who authors `lumen-core.riv`, and which Rive runtime targets Uno (official Uno/Skia runtime vs a RiveSharp rebuild against SkiaSharp 3)?
- Ship Geist/Geist Mono font files in the app (OFL) or keep platform fonts?
- Should Network show real peer instances (sync server) or stay simulated for v1?
- Haptics on mobile: which API per platform, and is it worth the platform-specific code in v1?
