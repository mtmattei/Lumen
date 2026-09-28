# Acceptance Criteria

## Architecture
- [ ] No authoritative thresholds/business transitions in Rive.
- [ ] Simulator runs/tests without Rive.
- [ ] Rive access goes through a defined adapter.
- [ ] Rive events map to semantic Uno commands.

## Core
- [ ] All seven states visually distinct.
- [ ] Nominal never appears frozen/obviously looped.
- [ ] Continuous pointer proximity.
- [ ] Press biases toward actual contact.
- [ ] Hold reaches charged state.
- [ ] Release produces Rive-to-Uno pulse.
- [ ] Exploded mode exposes all six subsystems.
- [ ] Selection opens correct Uno inspector.

## Data/scenario
- [ ] Telemetry is deterministic and causal.
- [ ] System load affects power/thermal behavior.
- [ ] Cooling fault transitions correctly.
- [ ] Recovery returns to Nominal.

## UX
- [ ] Core dominates reference desktop.
- [ ] Substantial negative space retained.
- [ ] Controls avoid generic SaaS styling.
- [ ] Inspector feels spatially connected.
- [ ] Mobile is recomposed, not scaled.
- [ ] First run interactive within 10s and has no blocking tutorial.

## Accessibility/performance
- [ ] Critical Rive states have semantic Uno equivalents.
- [ ] Keyboard/assistive equivalents exist.
- [ ] Reduced motion preserves essential information.
- [ ] State is not color-only.
- [ ] Telemetry ~10Hz; Rive interpolates.
- [ ] Pointer movement avoids broad XAML layout invalidation.

## Showcase bar
- [ ] Viewer can see real app state drive Rive without explanatory slides.
- [ ] Rive interaction visibly affects Uno UI.
- [ ] Uno control visibly affects Rive continuously.
- [ ] Rive/XAML boundary is difficult to identify during pulse and inspector flows.
