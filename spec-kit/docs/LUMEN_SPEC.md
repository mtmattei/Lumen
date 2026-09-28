# LUMEN Product + Interaction + Visual Specification

## Premise
LUMEN is a fictional distributed systems-control application composed of Power, Environment, Thermal, Navigation, Communications and Compute. Users observe simulated telemetry, manipulate load, inject faults, investigate subsystems and restore system health.

Bidirectional contract: `C# state -> Rive presentation` and `Rive interaction -> semantic Uno command -> application state`. Rive never owns authoritative business state.

## Showcase goals
1. C# state visibly changes Rive.
2. Rive interaction produces semantic Uno commands.
3. Pointer, touch, sliders and telemetry continuously influence presentation.
4. Rive and XAML feel like one physical composition.
5. Desktop, WebAssembly and mobile are purposefully composed.
6. The result is a real app, not a scripted animation reel.

## Main screen
Reference desktop 1440x900; minimum 1024x720. Core receives ~45–50% of visual attention. Quiet navigation left, context/status right, primary telemetry around Core, low telemetry rail bottom. Priority: Core > condition > selected subsystem > primary telemetry > contextual controls > navigation > metadata. Target ~45% quiet/negative space.

## States
Offline, Booting, Nominal, Elevated, Degraded, Critical, Recovering.

## Direct manipulation
Pointer proximity gradually biases particles, rotates nearby geometry and deforms the membrane. Press compresses the Core toward contact. Hold accumulates energy. At ~800ms it is charged. Release emits a radial pulse beginning in Rive and continuing through Uno controls in visual-distance order.

## Explode Core
Double-click/tap or Explore System separates six connected subsystem nodes. Selecting one recedes unrelated systems, moves the target toward focus, offsets the Core and creates space for an Uno inspector. Inspector controls update C# continuously and Rive responds during manipulation.

## Screens
### Overview
Living Core, primary telemetry, health, events and minimal actions.
### Energy Flow
Solar -> Core -> Battery/System. Particle speed = rate, density = volume, direction = transfer direction.
### System Explorer
Exploded Core, subsystem selection, contextual Uno inspector.
### Diagnostics
Cooling Fault, Power Deficit, Network Degradation, Sensor Failure, Compute Overload, Navigation Loss.
### Network
Application instances as nodes around System 01 with platform, latency, connection state and last telemetry.

## Primary scenario
Cooling Pump Degradation: Nominal -> pump efficiency falls -> coolant pressure falls -> temperature rises -> Elevated -> Thermal destabilizes -> Degraded -> investigate -> recovery -> Recovering -> Nominal. Target 30–60 seconds.

## Visual direction
Scientific instrument x precision industrial product x living organism. Avoid cyberpunk, generic HUDs, decorative neon, excessive glass, SaaS card grids, giant rounded rectangles and decorative icons.

### Neutral tokens
Canvas #0B0C0C; Surface #111313; Elevated #161818; Structural #2D302E; Primary #F1F2EF; Secondary #969A96; Tertiary #646864; Disabled #454845.

Semantic accents: Nominal mineral green; Information cold blue; Elevated mineral amber; Critical hot oxide; Offline gray. At nominal, ~90%+ of visible UI remains neutral.

### Typography
Interface: neutral neo-grotesk such as Geist/Inter or equivalent. Telemetry: technical mono such as Geist Mono/IBM Plex Mono or equivalent. Use scale/spacing before weight.

### Spacing
4px base. 4, 8, 12, 16, 24, 32, 48, 64, 96.

### Motion rules
Mass accelerates slowly. Energy moves quickly. Rive may be subtly elastic while Uno controls remain precise. Motion originates at cause. Prefer spatial continuity/rearrangement over generic fades.

## Uno controls
Buttons are primarily text + directional affordance, not filled cards. Instrument sliders use ticks/live values and update continuously. Toggles retain semantic toggle behavior. Navigation uses one selection line rather than pills. Telemetry uses large value + quiet label. Context inspectors emerge into space created by the selected subsystem.

## Responsive behavior
Desktop: central Core, nav left, context right, telemetry bottom. Tablet: collapsed nav, central Core, lower-third/context inspector. Mobile: recompose as tactile remote, not scaled desktop; Core roughly upper half, then condition, essential telemetry, compact navigation.

## Mobile sensors
Optional tilt feeds gravityX/gravityY with only ~3–6% displacement. Disable/simplify for reduced motion.

## Accessibility
Rive is never the sole carrier of critical information. Every meaningful visual state has an Uno semantic equivalent. Provide keyboard equivalents, screen-reader semantics, reduced motion, high contrast and non-color differentiation.

## Developer mode
Ctrl+Shift+D reveals Uno-owned regions, Rive-owned regions and live bindings.

## First-run timeline
0.0 black; 0.8 point; 1.4 heartbeat/haptic; 2.0 hairline scan; 2.8 SYSTEM 01; 3.2 particles converge; 4.0 nucleus; 4.8 membrane; 5.3 orbitals; 5.8 Power + Environment emerge; 6.5 nav + health; 7.5 SYSTEM INITIALIZED / 6 SUBSYSTEMS ONLINE; 8.5 TOUCH THE CORE; <=10s fully interactive.

Interaction learning: TOUCH THE CORE -> contact -> HOLD after ~400ms -> RELEASE at charge -> Core-to-Uno pulse -> INTERFACE LINK ESTABLISHED. Then expose SYSTEM LOAD so dragging immediately changes power draw, thermal load, particle velocity and Core intensity. No tutorial modal/carousel.
