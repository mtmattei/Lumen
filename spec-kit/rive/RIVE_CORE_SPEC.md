# Rive Core Contract

## Hierarchy
Core / Nucleus / InternalGeometry / Membrane / Orbitals / FlowNetwork / InteractionField / StateEffects / Subsystems(Power, Environment, Thermal, Navigation, Communications, Compute).

## State machine
Name: `LumenCore`

State: `systemState` = Offline, Booting, Nominal, Elevated, Degraded, Critical, Recovering.

Numeric inputs: `health 0..1`, `power 0..1`, `temperature 0..1`, `load 0..1`, `latency 0..1`, `pointerX -1..1`, `pointerY -1..1`, `pointerDistance 0..1`, `interactionForce 0..1`, `gravityX -1..1`, `gravityY -1..1`.

Triggers: `wake`, `pulse`, `fault`, `recover`, `explode`, `collapse`, `inspect`, `acknowledge`.

Events to Uno: `CorePressed`, `CoreCharged`, `CoreReleased`, `PulseCompleted`, `SubsystemSelected(id)`, `ExplodeCompleted`, `CollapseCompleted`, `RecoveryVisualCompleted`. Events never mutate business state directly.

## State expression
Offline: near-dark, no flow/orbits. Booting: particles converge inward, geometry assembles. Nominal: balanced independent low-frequency motion, 4–7s breathing with mismatched periods. Elevated: subtle contraction, faster flow, localized amber. Degraded: broken symmetry, unstable affected subsystem, interrupted flow. Critical: contracted nucleus, unstable orbitals, incoherent membrane, localized hot oxide, escaping particles, irregular heartbeat; never tint all red. Recovering: reverse entropy, realign, reform, one strong heartbeat, Nominal.

## Pointer field
>300px passive; 200–300 awareness; 100–200 attraction; 50–100 deformation; <50 direct interaction. Normalize to rendered Core.

## Press
0–250ms contact; 250–800ms charge; 800ms+ charged. Compression biases toward actual contact.

## Uno boundary
Uno/C# owns all thresholds, state transitions and faults. Send normalized state through a single presenter/adapter. Application telemetry target 10Hz; Rive interpolates at render rate. If Rive fails, Uno status/controls remain usable.
