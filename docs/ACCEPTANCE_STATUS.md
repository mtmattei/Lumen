# Acceptance Status (v1)

Checked against `spec-kit/planning/ACCEPTANCE_CRITERIA.md`.
Evidence: **T** = unit test in `Lumen.Tests`; **R** = observed at runtime (desktop, Linux/Xvfb, Release), screenshot in `docs/screenshots/`; **C** = by construction (code review).

Status: ✅ met · ◐ met with a stand-in or partially · ☐ not yet

## Architecture
| Criterion | Status | Evidence |
|---|---|---|
| No authoritative thresholds/business transitions in Rive | ✅ | C: all thresholds in `SystemStateClassifier`/`PressGesture`/`PointerField`; the renderer only interpolates |
| Simulator runs/tests without Rive | ✅ | T: `SimulatorTests`, `ClassifierTests` (plain `net10.0`) |
| Rive access goes through a defined adapter | ✅ | C: `ILumenCorePresenter` → `LumenCorePresenter` |
| Rive events map to semantic Uno commands | ✅ | T: `Every_rive_event_maps_to_a_semantic_command` |

## Core
| Criterion | Status | Evidence |
|---|---|---|
| All seven states visually distinct | ◐ | R: Offline/Booting (`first-run.png`), Nominal, Elevated, Degraded (`cooling-degraded.png`), Critical (`critical.png`, full load + cooling fault), Recovering. Procedural stand-in, not Rive |
| Nominal never frozen/obviously looped | ✅ | C: 4.3/5.6/6.9 s mismatched breathing, drifting lobes, per-particle phase |
| Continuous pointer proximity | ✅ | R: `developer-mode.png` shows pointerX/Y/Distance live; membrane reaches toward the pointer |
| Press biases toward actual contact | ✅ | C: `MembraneRadius` dent at the contact angle; R: `charged.png` |
| Hold reaches charged state | ✅ | T: `Press_gesture_reaches_charge_at_800ms`; R: RELEASE prompt |
| Release produces Rive-to-Uno pulse | ✅ | R: `overview-link.png` sequence; readouts flash in distance order |
| Exploded mode exposes all six subsystems | ✅ | R: `explorer-exploded.png` |
| Selection opens correct Uno inspector | ✅ | R: `explorer-inspector.png` (Thermal node → Thermal inspector with pump boost) |

## Data/scenario
| Criterion | Status | Evidence |
|---|---|---|
| Telemetry deterministic and causal | ✅ | T: `Same_inputs_produce_identical_traces`, `Load_raises_consumption_drain_and_temperature` |
| System load affects power/thermal | ✅ | T + R (SYSTEM LOAD slider) |
| Cooling fault transitions correctly | ✅ | T: `Cooling_scenario_follows_fixture_states`, `..._tracks_fixture_values` (±6 °C / ±5 psi); R: `cooling-degraded.png` |
| Recovery returns to Nominal | ✅ | T + R (event log shows the full sequence) |

## UX
| Criterion | Status | Evidence |
|---|---|---|
| Core dominates reference desktop | ✅ | R: 1440×900 screenshots |
| Substantial negative space | ✅ | R |
| Controls avoid generic SaaS styling | ✅ | Text + `›` actions, single selection line, neutral instrument sliders |
| Inspector feels spatially connected | ◐ | Node moves beside the Core, the Core offsets, the inspector slides in from the Core side; still a separate column |
| Mobile recomposed, not scaled | ✅ | R: `tablet-mobile.png` (390×844 and 900×1100) |
| First run interactive ≤ 10 s, no blocking tutorial | ✅ | T: `First_run_is_interactive_within_ten_seconds`; any input skips |

## Accessibility/performance
| Criterion | Status | Evidence |
|---|---|---|
| Critical Rive states have semantic Uno equivalents | ✅ | State word + glyph in header, health panel, live regions, Core automation name |
| Keyboard/assistive equivalents | ◐ | Space hold/release, E, arrows, Enter, Esc, nav via Tab; not yet verified with a screen reader |
| Reduced motion preserves essential information | ◐ | Toggle + OS setting: slower Core, shortened first run, no pulse delays, no tilt; not visually reviewed in this session |
| State is not color-only | ✅ | ● ▲ ◆ ■ ↻ glyphs + words everywhere |
| Telemetry ~10 Hz; Rive interpolates | ✅ | C: `TelemetryHost` 100 ms fixed steps; renderer lags toward inputs every frame |
| Pointer movement avoids broad XAML invalidation | ✅ | C: pointer → presenter inputs only |

## Showcase bar
| Criterion | Status | Evidence |
|---|---|---|
| Real app state visibly drives the Core | ✅ | R |
| Core interaction visibly affects Uno UI | ✅ | R: pulse → readouts, coach, SYSTEM LOAD reveal |
| Uno control visibly affects the Core continuously | ✅ | R: sliders → load/temperature → flow speed, nucleus intensity |
| Rive/XAML boundary hard to identify | ◐ | Subjective; needs review on real hardware |

## Not verified here
- `net10.0-android` / `net10.0-ios` builds (workloads not installed in this environment).
- Tilt input on devices. (Haptics are out of scope for v1.)
- Reduced-motion visual pass and a screen-reader pass.
- WebAssembly head builds (`net10.0-browserwasm`, 0 warnings); it was not run in a browser here.
