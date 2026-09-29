"""Generate Lumen/Assets/Rive/lumen-core.riv from the LumenCore contract.

Contract: spec-kit/rive/RIVE_CORE_SPEC.md (+ contract extensions `stressedSubsystem`, `pressed`).
Run:  python3 tools/rive/build_lumen_core.py
"""
from __future__ import annotations

import math
import pathlib

from riv_writer import Color, RivFile, Schema

HERE = pathlib.Path(__file__).parent
OUT = HERE.parent.parent / "Lumen" / "Assets" / "Rive" / "lumen-core.riv"

FPS = 60
SIZE = 500.0
C = SIZE / 2
R = 120.0            # membrane radius
RETICLE = 185.0      # instrument ring
EXPLODED = 222.0     # subsystem distance when exploded
TAU = math.tau

WARM = "F1EBDD"
AMBER = "E8B070"
HAIR = "F1F2EF"
NOMINAL = "7FB89A"
INFO = "8FB4D8"
ELEVATED = "D6A55C"
CRITICAL = "D8653F"
CANVAS = "0B0C0C"

STATES = ["Offline", "Booting", "Nominal", "Elevated", "Degraded", "Critical", "Recovering"]
SUBSYSTEMS = ["Power", "Environment", "Thermal", "Navigation", "Communications", "Compute"]
# Same angles as the procedural renderer: Environment top, Navigation bottom, Power left, Comms right.
SUBSYSTEM_ANGLES = [-150, -90, 150, 90, -30, 30]

# Property keys used for keying (from the schema, checked at build time).
K = {}


def rad(deg: float) -> float:
    return deg * math.pi / 180


class Builder:
    def __init__(self):
        self.f = RivFile(Schema(HERE / "rive_schema.json"))
        self.animations: list[dict] = []
        self.anim_index: dict[str, int] = {}
        self.events: dict[str, int] = {}

    # ── artboard components ────────────────────────────────────────────────
    def node(self, name, parent, x=0.0, y=0.0, **extra):
        return self.f.add("Node", name=name, parentId=parent, x=float(x), y=float(y), **extra)

    def ellipse_shape(self, name, parent, w, h=None, x=0.0, y=0.0, fill=None, stroke=None, thickness=1.0,
                      opacity=1.0, rotation=0.0, gradient=None, trim=None):
        h = w if h is None else h
        shape = self.f.add("Shape", name=name, parentId=parent, x=float(x), y=float(y),
                           opacity=float(opacity), rotation=float(rotation))
        self.f.add("Ellipse", name=f"{name}Path", parentId=shape, width=float(w), height=float(h))
        paints = {}
        if gradient is not None:
            fill_id = self.f.add("Fill", name=f"{name}Fill", parentId=shape)
            grad = self.f.add("RadialGradient", name=f"{name}Gradient", parentId=fill_id,
                              startX=0.0, startY=0.0, endX=float(w) / 2, endY=0.0)
            for pos, color in gradient:
                self.f.add("GradientStop", name="stop", parentId=grad, position=float(pos), colorValue=color)
            paints["fill"] = fill_id
        elif fill is not None:
            fill_id = self.f.add("Fill", name=f"{name}Fill", parentId=shape)
            paints["fillColor"] = self.f.add("SolidColor", name=f"{name}FillColor", parentId=fill_id, colorValue=fill)
            paints["fill"] = fill_id
        if stroke is not None:
            stroke_id = self.f.add("Stroke", name=f"{name}Stroke", parentId=shape, thickness=float(thickness),
                                   transformAffectsStroke=False)
            paints["strokeColor"] = self.f.add("SolidColor", name=f"{name}StrokeColor", parentId=stroke_id, colorValue=stroke)
            if trim is not None:
                paints["trim"] = self.f.add("TrimPath", name=f"{name}Trim", parentId=stroke_id,
                                            start=float(trim[0]), end=float(trim[1]), offset=float(trim[2]), modeValue=1)
            paints["stroke"] = stroke_id
        return shape, paints

    def path_shape(self, name, parent, points, closed, fill=None, stroke=None, thickness=1.0, opacity=1.0,
                   gradient=None, gradient_radius=100.0, trim=None):
        shape = self.f.add("Shape", name=name, parentId=parent, opacity=float(opacity))
        path = self.f.add("PointsPath", name=f"{name}Path", parentId=shape, isClosed=closed)
        for p in points:
            if p[0] == "S":
                self.f.add("StraightVertex", name="v", parentId=path, x=float(p[1]), y=float(p[2]), radius=0.0)
            else:  # ("C", x, y, inRot, inDist, outRot, outDist)
                self.f.add("CubicDetachedVertex", name="v", parentId=path, x=float(p[1]), y=float(p[2]),
                           inRotation=float(p[3]), inDistance=float(p[4]), outRotation=float(p[5]), outDistance=float(p[6]))
        paints = {}
        if gradient is not None:
            fill_id = self.f.add("Fill", name=f"{name}Fill", parentId=shape)
            grad = self.f.add("RadialGradient", name=f"{name}Gradient", parentId=fill_id,
                              startX=0.0, startY=0.0, endX=float(gradient_radius), endY=0.0)
            for pos, color in gradient:
                self.f.add("GradientStop", name="stop", parentId=grad, position=float(pos), colorValue=color)
        elif fill is not None:
            fill_id = self.f.add("Fill", name=f"{name}Fill", parentId=shape)
            paints["fillColor"] = self.f.add("SolidColor", name=f"{name}FillColor", parentId=fill_id, colorValue=fill)
        if stroke is not None:
            stroke_id = self.f.add("Stroke", name=f"{name}Stroke", parentId=shape, thickness=float(thickness),
                                   transformAffectsStroke=False, cap=1)
            paints["strokeColor"] = self.f.add("SolidColor", name=f"{name}StrokeColor", parentId=stroke_id, colorValue=stroke)
            if trim is not None:
                paints["trim"] = self.f.add("TrimPath", name=f"{name}Trim", parentId=stroke_id,
                                            start=float(trim[0]), end=float(trim[1]), offset=float(trim[2]), modeValue=1)
        return shape, paints

    def event(self, name, subsystem_id=None):
        index = self.f.add("Event", name=name, parentId=0)
        if subsystem_id is not None:
            self.f.add("CustomPropertyNumber", name="id", parentId=index, propertyValue=float(subsystem_id))
        return index

    # ── animations (collected, written after all components) ──────────────
    def anim(self, name, seconds, loop, tracks):
        """tracks: list of (objectIndex, propName, [(t_seconds, value)...], kind='double'|'color')."""
        self.anim_index[name] = len(self.animations)
        self.animations.append({"name": name, "frames": max(1, round(seconds * FPS)), "loop": loop, "tracks": tracks})

    def write_animations(self, ease_id):
        for a in self.animations:
            self.f.add("LinearAnimation", name=a["name"], fps=FPS, duration=a["frames"], loopValue=a["loop"], speed=1.0)
            by_object: dict[int, list] = {}
            for obj, prop, keys, *kind in a["tracks"]:
                by_object.setdefault(obj, []).append((prop, keys, kind[0] if kind else "double"))
            for obj, props in by_object.items():
                self.f.add("KeyedObject", objectId=obj)
                for prop, keys, kind in props:
                    self.f.add("KeyedProperty", propertyKey=K[prop])
                    for t, value in keys:
                        frame = min(a["frames"], max(0, round(t * FPS)))
                        if kind == "color":
                            self.f.add("KeyFrameColor", frame=frame, interpolationType=1, value=value)
                        else:
                            self.f.add("KeyFrameDouble", frame=frame, interpolationType=2, interpolatorId=ease_id,
                                       value=float(value))


def sine_keys(period, base, amplitude, phase=0.0, samples=12):
    """Smooth periodic keys over one period (cubic-eased between samples)."""
    return [(period * i / samples, base + amplitude * math.sin(TAU * i / samples + phase)) for i in range(samples + 1)]


def build() -> bytes:
    b = Builder()
    f = b.f
    for prop, type_name in [("x", "Node"), ("y", "Node"), ("rotation", "Node"), ("scaleX", "Node"), ("scaleY", "Node"),
                            ("opacity", "Node"), ("colorValue", "SolidColor"), ("offset", "TrimPath"), ("end", "TrimPath"),
                            ("thickness", "Stroke"), ("vx", "StraightVertex"), ("vy", "StraightVertex")]:
        K[prop] = f.schema.prop(type_name, {"vx": "x", "vy": "y"}.get(prop, prop))[0]

    f.add("Backboard")
    ab = f.add("Artboard", name="LumenCore", width=SIZE, height=SIZE, originX=0.0, originY=0.0, clip=True)
    assert ab == 1
    art = 0  # components index relative to the artboard: artboard is object 0 of its run
    base = ab  # file index of the artboard; component indices are (file index - base)

    def rel(file_index):
        return file_index - base

    # The artboard's run starts at the artboard, so every parentId/objectId below is artboard-relative.
    orig_add = f.add

    def add_rel(type_name, **props):
        return rel(orig_add(type_name, **props))

    f.add = add_rel

    # ── hierarchy (spec: Core / Nucleus / InternalGeometry / Membrane / Orbitals / FlowNetwork /
    #    InteractionField / StateEffects / Subsystems) ──
    core = b.node("Core", art, C, C)                       # Mode layer: scale (explode shrink)
    gravity = b.node("Gravity", core)                      # gravityX/Y blends: x / y
    compress = b.node("Compress", gravity)                 # interactionForce blend: scale
    press = b.node("PressCompress", compress)              # Press layer: scale
    instab = b.node("Instability", press)                  # SystemState layer: x, rotation

    reticle = b.node("Reticle", core)                      # SystemState: opacity
    ring, _ = b.ellipse_shape("ReticleRing", reticle, RETICLE * 2, stroke=Color.hex(HAIR, 0.16))
    axis_v, _ = b.path_shape("AxisVertical", reticle, [("S", 0, -RETICLE * 1.1), ("S", 0, RETICLE * 1.1)], False,
                             stroke=Color.hex(HAIR, 0.28))
    axis_l, _ = b.path_shape("AxisLeft", reticle, [("S", -RETICLE * 1.1, 0), ("S", -R * 1.08, 0)], False,
                             stroke=Color.hex(HAIR, 0.28))
    axis_r, _ = b.path_shape("AxisRight", reticle, [("S", R * 1.08, 0), ("S", RETICLE * 1.1, 0)], False,
                             stroke=Color.hex(HAIR, 0.28))
    ticks = b.node("Ticks", reticle)                        # Drift layer: slow rotation
    for i in range(12):
        a = TAU * i / 12
        big = i % 3 == 0
        b.ellipse_shape(f"Tick{i}", ticks, 4.4 if big else 2.4, x=math.cos(a) * RETICLE, y=math.sin(a) * RETICLE,
                        fill=Color.hex(HAIR, 0.5))
    for x, y in [(0, -RETICLE * 1.1), (0, RETICLE * 1.1), (-RETICLE * 1.1, 0), (RETICLE * 1.1, 0)]:
        b.ellipse_shape("AxisEnd", reticle, 5, x=x, y=y, fill=Color.hex(HAIR, 0.9))

    # Particles (InteractionField biases them toward the pointer).
    field = b.node("InteractionField", instab)             # pointerX/Y blends: x / y
    particles = b.node("Particles", field)                 # SystemState: scale (converge), opacity
    rings = []
    for ring_i, (count, radius, squash) in enumerate([(18, R * 1.18, 0.78), (14, R * 0.72, 0.9), (12, R * 1.42, 0.7)]):
        pr = b.node(f"ParticleRing{ring_i}", particles)     # Spin layer: rotation
        rings.append(pr)
        for i in range(count):
            a = TAU * (i + 0.37 * ring_i) / count + 0.4 * math.sin(i * 1.7)
            rr = radius * (0.9 + 0.2 * math.sin(i * 2.3 + ring_i))
            color = AMBER if (i + ring_i) % 5 == 0 else WARM
            b.ellipse_shape(f"P{ring_i}_{i}", pr, 2.6 if i % 4 else 3.4, x=math.cos(a) * rr, y=math.sin(a) * rr * squash,
                            fill=Color.hex(color, 0.55 if ring_i == 1 else 0.42))

    # Orbitals.
    orbitals = b.node("Orbitals", instab, rotation=rad(-7))  # SystemState: opacity
    orbit_spin = b.node("OrbitSpin", orbitals)              # latency blend: scaleY wobble
    orbit_ring, orbit_paints = b.ellipse_shape("OrbitRing", orbit_spin, R * 2.7, R * 0.5, stroke=Color.hex(HAIR, 0.42),
                                               thickness=1.1)
    beads = b.node("Beads", orbit_spin)                     # Orbit layer: rotation (beads ride an ellipse via scaleY)
    beads_inner = b.node("BeadsTrack", beads, scaleY=0.185)
    for i in range(3):
        a = TAU * i / 3
        b.ellipse_shape(f"Bead{i}", beads_inner, 4.6, 4.6 / 0.185, x=math.cos(a) * R * 1.35, y=math.sin(a) * R * 1.35,
                        fill=Color.hex(AMBER, 0.9))

    # Membrane: layered, independently breathing shells.
    membrane = b.node("Membrane", instab)                   # Breath layer: scale
    reach = b.node("MembraneReach", membrane)               # pointerDistance blend: scale
    contract = b.node("MembraneContract", reach)            # SystemState: scaleX/scaleY
    heat, _ = b.ellipse_shape("HeatHaze", contract, R * 2, opacity=0.0,  # temperature blend: opacity
                              gradient=[(0, Color.hex(AMBER, 0.0)), (0.7, Color.hex(AMBER, 0.05)), (1, Color.hex(AMBER, 0.22))])
    shell, shell_paints = b.ellipse_shape("Shell", contract, R * 2,
                                          gradient=[(0, Color.hex(WARM, 0.02)), (0.7, Color.hex(WARM, 0.05)), (1, Color.hex(WARM, 0.14))])
    shell_line, shell_line_paints = b.ellipse_shape("ShellLine", contract, R * 2, stroke=Color.hex(WARM, 0.55), thickness=1.3)
    lobes = b.node("Lobes", contract)                       # Drift layer: rotation
    for i in range(4):
        b.ellipse_shape(f"Lobe{i}", lobes, R * 1.86, R * (1.04 + 0.08 * i), rotation=rad(i * 45),
                        stroke=Color.hex(WARM, 0.14 - i * 0.02))

    # Flow network: filaments from the nucleus toward the membrane.
    flow = b.node("FlowNetwork", instab)                    # load blend: opacity ; SystemState: opacity via parent
    filament_trims = []
    for i in range(8):
        a = TAU * i / 8 + 0.2
        end = R * 0.9
        mid = end * 0.55
        mx, my = math.cos(a + 0.5) * mid, math.sin(a + 0.5) * mid
        ex, ey = math.cos(a) * end, math.sin(a) * end
        _, paints = b.path_shape(f"Filament{i}", flow,
                                 [("C", 0, 0, 0, 0, math.atan2(my, mx), mid * 0.6),
                                  ("C", ex, ey, math.atan2(my - ey, mx - ex), mid * 0.4, 0, 0)], False,
                                 stroke=Color.hex(AMBER, 0.35), thickness=1.0, trim=(0.0, 0.35, 0.0))
        filament_trims.append(paints["trim"])

    # Internal geometry: the four-point star.
    geometry = b.node("InternalGeometry", instab)           # Drift layer: rotation
    tall, wide, pinch = R * 0.95, R * 0.27, R * 0.035
    # Concave four-point star: every edge bows toward the centre.
    star = [("C", 0, -tall, rad(90) + 0.05, tall * 0.4, rad(90) - 0.05, tall * 0.4),
            ("C", wide, 0, rad(180) - 0.25, wide * 0.7, rad(180) + 0.25, wide * 0.7),
            ("C", 0, tall, rad(-90) + 0.05, tall * 0.4, rad(-90) - 0.05, tall * 0.4),
            ("C", -wide, 0, 0.25, wide * 0.7, -0.25, wide * 0.7)]
    b.path_shape("Star", geometry, star, True, gradient_radius=tall,
                 gradient=[(0, Color.hex(AMBER, 0.55)), (0.5, Color.hex(AMBER, 0.14)), (1, Color.hex(AMBER, 0.0))],
                 opacity=1.0)
    b.path_shape("StarLine", geometry, star, True, stroke=Color.hex(WARM, 0.5), thickness=1.0)
    for y in (-tall, tall):
        b.ellipse_shape("Spark", geometry, 7, y=y, fill=Color.hex("FFFFFF", 0.85))

    # Nucleus.
    nucleus = b.node("Nucleus", instab)                     # load blend: scale ; health blend via NucleusGlow opacity
    beat = b.node("NucleusBeat", nucleus)                   # Heartbeat layer: scale
    glow, _ = b.ellipse_shape("NucleusGlow", beat, R * 0.8,
                              gradient=[(0, Color.hex("FFFFFF", 0.95)), (0.25, Color.hex(AMBER, 0.5)), (1, Color.hex(AMBER, 0.0))])
    b.ellipse_shape("NucleusCore", beat, 8, fill=Color.hex("FFFFFF", 1.0))

    # State effects: six localized arcs, one per subsystem. Never tint the whole Core.
    effects = b.node("StateEffects", instab, opacity=0.0)    # SystemState: opacity
    arc_colors, arc_nodes = [], []
    for i, ang in enumerate(SUBSYSTEM_ANGLES):
        # Rive ellipse paths start at the top (-90°), so +62° centres the 56° arc on the subsystem angle.
        arc_node = b.node(f"Stress{SUBSYSTEMS[i]}", effects, rotation=rad(ang + 62), opacity=0.0)  # stressed blend: opacity
        _, paints = b.ellipse_shape(f"StressArc{i}", arc_node, R * 2.05, stroke=Color.hex(ELEVATED), thickness=2.2,
                                    trim=(0.0, 56 / 360, 0.0))
        arc_colors.append(paints["strokeColor"])
        arc_nodes.append(arc_node)

    recover_ring, recover_paints = b.ellipse_shape("RecoverRing", instab, R * 2, stroke=Color.hex(NOMINAL, 0.6),
                                                   thickness=1.2, opacity=0.0)   # FX recover: scale + opacity
    pulse_ring, _ = b.ellipse_shape("PulseRing", core, R * 2, stroke=Color.hex(WARM, 0.8), thickness=2.0, opacity=0.0)
    pulse_ring2, _ = b.ellipse_shape("PulseEcho", core, R * 1.7, stroke=Color.hex(AMBER, 0.5), thickness=1.0, opacity=0.0)
    flash, _ = b.ellipse_shape("FaultFlash", instab, R * 2.2, opacity=0.0,
                               gradient=[(0, Color.hex(CRITICAL, 0.0)), (0.8, Color.hex(CRITICAL, 0.0)), (1, Color.hex(CRITICAL, 0.35))])

    # Subsystems: six connected nodes (explode separates them).
    subsystem_nodes, subsystem_rings, subsystem_links = [], [], []
    subsystem_group = b.node("Subsystems", core)
    links = b.node("Links", subsystem_group, opacity=0.0)    # Mode: opacity
    link_ends = []
    for i, name in enumerate(SUBSYSTEMS):
        a = rad(SUBSYSTEM_ANGLES[i])
        shape = f.add("Shape", name=f"{name}Link", parentId=links)
        path = f.add("PointsPath", name=f"{name}LinkPath", parentId=shape, isClosed=False)
        f.add("StraightVertex", name="v", parentId=path, x=math.cos(a) * R * 1.04, y=math.sin(a) * R * 1.04, radius=0.0)
        link_ends.append(f.add("StraightVertex", name="v", parentId=path, x=math.cos(a) * RETICLE, y=math.sin(a) * RETICLE,
                               radius=0.0))                  # Mode: x, y
        stroke = f.add("Stroke", name=f"{name}LinkStroke", parentId=shape, thickness=1.0, transformAffectsStroke=False)
        f.add("SolidColor", name=f"{name}LinkColor", parentId=stroke, colorValue=Color.hex(HAIR, 0.3))
    for i, name in enumerate(SUBSYSTEMS):
        a = rad(SUBSYSTEM_ANGLES[i])
        n = b.node(name, subsystem_group, x=math.cos(a) * RETICLE, y=math.sin(a) * RETICLE)  # Mode: x, y
        ring_node = b.node(f"{name}Ring", n, scaleX=0.35, scaleY=0.35)                         # Mode: scale
        b.ellipse_shape(f"{name}Face", ring_node, 36, fill=Color.hex(CANVAS, 0.92), stroke=Color.hex(HAIR, 0.6))
        b.ellipse_shape(f"{name}Inner", ring_node, 16, stroke=Color.hex(WARM, 0.6))
        b.ellipse_shape(f"{name}Dot", n, 4, fill=Color.hex(WARM, 0.9))
        hit, _ = b.ellipse_shape(f"{name}Hit", n, 44, fill=Color.hex(CANVAS, 0.004))
        subsystem_nodes.append(n)
        subsystem_rings.append(ring_node)
        subsystem_links.append(hit)

    hit_area, _ = b.ellipse_shape("CoreHitArea", core, R * 2.3, fill=Color.hex(CANVAS, 0.004))

    # Events (spec: never mutate business state; Uno maps them to commands).
    for name in ["CorePressed", "CoreCharged", "CoreReleased", "PulseCompleted", "ExplodeCompleted",
                 "CollapseCompleted", "RecoveryVisualCompleted"]:
        b.events[name] = b.event(name)
    for i, name in enumerate(SUBSYSTEMS):
        b.events[f"SubsystemSelected{i}"] = b.event("SubsystemSelected", i)

    # Shared easing for keyframes and transitions; written last so component indices never depend on it.
    ease = f.add("CubicEaseInterpolator", x1=0.42, y1=0.0, x2=0.58, y2=1.0)

    # ── animations ─────────────────────────────────────────────────────────
    A = b.anim
    A("idle", 0.1, 0, [])

    # Independent low-frequency loops with mismatched periods (Nominal never looks frozen).
    A("breath", 4.3, 1, [(membrane, "scaleX", sine_keys(4.3, 1.0, 0.022)), (membrane, "scaleY", sine_keys(4.3, 1.0, 0.018, 0.6))])
    A("drift", 5.7, 1, [(lobes, "rotation", sine_keys(5.7, 0.0, rad(9))),
                        (geometry, "rotation", sine_keys(5.7, 0.0, rad(2.5), 1.3)),
                        (ticks, "rotation", [(0, 0.0), (5.7, rad(2.0))])])
    A("orbit", 6.9, 1, [(beads, "rotation", [(0, 0.0), (6.9, TAU)])])
    A("spin", 24.0, 1, [(rings[0], "rotation", [(0, 0.0), (24, TAU)]), (rings[1], "rotation", [(0, 0.0), (24, -TAU * 0.75)]),
                        (rings[2], "rotation", [(0, 0.0), (24, TAU * 0.5)])])
    beat_keys = [(0, 1.0), (0.12, 1.14), (0.28, 1.0), (0.4, 1.07), (0.6, 1.0), (2.6, 1.0)]
    A("heartbeat", 2.6, 1, [(beat, "scaleX", beat_keys), (beat, "scaleY", beat_keys)])
    irregular = [(0, 1.0), (0.1, 1.2), (0.25, 1.0), (0.55, 1.12), (0.7, 1.0), (1.05, 1.18), (1.2, 1.0), (1.7, 1.0)]
    fast = [(0, 1.0), (0.1, 1.16), (0.24, 1.0), (0.34, 1.08), (0.5, 1.0), (1.6, 1.0)]
    A("heartbeat_fast", 1.6, 1, [(beat, "scaleX", fast), (beat, "scaleY", fast)])
    A("heartbeat_irregular", 1.7, 1, [(beat, "scaleX", irregular), (beat, "scaleY", irregular)])

    # Flow speed/volume blended by load: offsets travel 1 vs 3 lengths per 4 s.
    def flow_anim(name, cycles, alpha):
        tracks = []
        for i, trim in enumerate(filament_trims):
            tracks.append((trim, "offset", [(0, i * 0.11), (4.0, i * 0.11 + cycles)]))
        tracks.append((flow, "opacity", [(0, alpha)]))
        return A(name, 4.0, 1, tracks)
    flow_anim("flow_calm", 1, 0.45)
    flow_anim("flow_intense", 3, 1.0)

    # Numeric input poses (1D blends between two keyed poses).
    def pose(name, tracks):
        A(name, 0.1, 0, [(obj, prop, [(0, v)]) for obj, prop, v in tracks])

    pose("load_low", [(nucleus, "scaleX", 0.9), (nucleus, "scaleY", 0.9)])
    pose("load_high", [(nucleus, "scaleX", 1.25), (nucleus, "scaleY", 1.25)])
    pose("health_low", [(glow, "opacity", 0.55), (shell_line, "opacity", 0.6)])
    pose("health_high", [(glow, "opacity", 1.0), (shell_line, "opacity", 1.0)])
    pose("power_low", [(particles, "opacity", 0.45)])
    pose("power_high", [(particles, "opacity", 1.0)])
    pose("temperature_low", [(heat, "opacity", 0.0)])
    pose("temperature_high", [(heat, "opacity", 1.0)])
    pose("latency_low", [(orbit_spin, "scaleY", 1.0), (orbit_ring, "opacity", 1.0)])
    pose("latency_high", [(orbit_spin, "scaleY", 1.35), (orbit_ring, "opacity", 0.35)])
    pose("pointerX_left", [(field, "x", -18.0)])
    pose("pointerX_right", [(field, "x", 18.0)])
    pose("pointerY_up", [(field, "y", -18.0)])
    pose("pointerY_down", [(field, "y", 18.0)])
    pose("proximity_near", [(reach, "scaleX", 1.045), (reach, "scaleY", 1.045)])
    pose("proximity_far", [(reach, "scaleX", 1.0), (reach, "scaleY", 1.0)])
    pose("force_none", [(compress, "scaleX", 1.0), (compress, "scaleY", 1.0)])
    pose("force_full", [(compress, "scaleX", 0.9), (compress, "scaleY", 0.86)])
    pose("gravityX_left", [(gravity, "x", -7.0)])
    pose("gravityX_right", [(gravity, "x", 7.0)])
    pose("gravityY_up", [(gravity, "y", -7.0)])
    pose("gravityY_down", [(gravity, "y", 7.0)])
    stress_names = ["stress_none"] + [f"stress_{s.lower()}" for s in SUBSYSTEMS]
    for k, name in enumerate(stress_names):
        pose(name, [(arc_nodes[i], "opacity", 1.0 if k - 1 == i else 0.0) for i in range(6)])

    # System state poses. Every state keys the same properties so blends are complete.
    def state_tracks(core_opacity, orbit_opacity, particle_scale, particle_opacity, contract_x, contract_y,
                     effects_opacity, arc_color, reticle_opacity, jitter=None, line=(WARM, 0.55), wobble=None):
        tracks = [
            (shell_line_paints["strokeColor"], "colorValue", [(0, Color.hex(*line))], "color"),
            (instab, "opacity", [(0, core_opacity)]),
            (orbitals, "opacity", [(0, orbit_opacity)]),
            (particles, "scaleX", [(0, particle_scale)]),
            (particles, "scaleY", [(0, particle_scale)]),
            (field, "opacity", [(0, particle_opacity)]),
            (effects, "opacity", [(0, effects_opacity)]),
            (reticle, "opacity", [(0, reticle_opacity)]),
        ]
        tracks += [(c, "colorValue", [(0, Color.hex(arc_color))], "color") for c in arc_colors]
        if wobble is None:
            tracks += [(contract, "scaleX", [(0, contract_x)]), (contract, "scaleY", [(0, contract_y)])]
        else:
            # Incoherent membrane: the two axes breathe out of phase at uneven periods.
            amp, period = wobble
            tracks += [(contract, "scaleX", sine_keys(period, contract_x, amp, 0.0, 16)),
                       (contract, "scaleY", sine_keys(period, contract_y, amp * 1.3, 2.1, 16))]
        if jitter is None:
            tracks += [(instab, "x", [(0, 0.0)]), (instab, "rotation", [(0, 0.0)])]
        else:
            amp, rot, period = jitter
            tracks += [(instab, "x", sine_keys(period, 0.0, amp, 0.0, 16)),
                       (instab, "rotation", sine_keys(period * 1.37, 0.0, rad(rot), 0.9, 16))]
        return tracks

    A("state_offline", 1.0, 0, state_tracks(0.18, 0.0, 1.0, 0.0, 0.8, 0.8, 0.0, ELEVATED, 0.3, line=("969A96", 0.4)))
    A("state_booting", 3.0, 0, [
        (instab, "opacity", [(0, 0.2), (3.0, 0.9)]), (orbitals, "opacity", [(0, 0.0), (2.2, 0.0), (3.0, 1.0)]),
        (particles, "scaleX", [(0, 3.4), (3.0, 1.0)]), (particles, "scaleY", [(0, 3.4), (3.0, 1.0)]),
        (field, "opacity", [(0, 0.0), (1.2, 1.0)]),
        (contract, "scaleX", [(0, 0.3), (1.2, 0.3), (2.4, 1.0)]), (contract, "scaleY", [(0, 0.3), (1.2, 0.3), (2.4, 1.0)]),
        (effects, "opacity", [(0, 0.0)]), (reticle, "opacity", [(0, 0.0), (1.5, 1.0)]),
        (shell_line_paints["strokeColor"], "colorValue", [(0, Color.hex(WARM, 0.2)), (3.0, Color.hex(WARM, 0.55))], "color"),
        *[(c, "colorValue", [(0, Color.hex(ELEVATED))], "color") for c in arc_colors],
        (instab, "x", [(0, 0.0)]), (instab, "rotation", [(0, 0.0)])])
    A("state_nominal", 1.0, 0, state_tracks(1.0, 1.0, 1.0, 1.0, 1.0, 1.0, 0.0, ELEVATED, 1.0))
    A("state_elevated", 2.2, 1, state_tracks(1.0, 1.0, 0.97, 1.0, 0.965, 0.965, 0.7, ELEVATED, 1.0, (0.6, 0.6, 2.2),
                                             line=("EDC99A", 0.6)))
    A("state_degraded", 1.6, 1, state_tracks(1.0, 0.85, 1.02, 0.9, 0.95, 0.93, 0.9, CRITICAL, 1.0, (2.2, 2.0, 1.6),
                                             line=("E9B98F", 0.6), wobble=(0.012, 1.6)))
    A("state_critical", 0.9, 1, state_tracks(1.0, 0.7, 1.12, 0.85, 0.9, 0.87, 1.0, CRITICAL, 1.0, (4.5, 4.0, 0.9),
                                             line=("E7A488", 0.65), wobble=(0.03, 0.9)))
    A("state_recovering", 1.8, 0, [
        (instab, "opacity", [(0, 1.0)]), (orbitals, "opacity", [(0, 0.8), (1.8, 1.0)]),
        (particles, "scaleX", [(0, 1.15), (1.8, 1.0)]), (particles, "scaleY", [(0, 1.15), (1.8, 1.0)]),
        (field, "opacity", [(0, 1.0)]),
        (contract, "scaleX", [(0, 0.95), (0.6, 0.93), (0.75, 1.06), (1.8, 1.0)]),
        (contract, "scaleY", [(0, 0.93), (0.6, 0.93), (0.75, 1.06), (1.8, 1.0)]),
        (effects, "opacity", [(0, 0.5), (1.8, 0.0)]), (reticle, "opacity", [(0, 1.0)]),
        (shell_line_paints["strokeColor"], "colorValue", [(0, Color.hex(NOMINAL, 0.6)), (1.8, Color.hex(WARM, 0.55))], "color"),
        *[(c, "colorValue", [(0, Color.hex(NOMINAL))], "color") for c in arc_colors],
        (instab, "x", [(0, 0.0)]), (instab, "rotation", [(0, rad(2)), (1.8, 0.0)])])

    # Trigger one-shots (FX layer).
    A("fx_pulse", 1.4, 0, [(pulse_ring, "opacity", [(0, 1.0), (1.4, 0.0)]),
                           (pulse_ring, "scaleX", [(0, 0.2), (1.4, 3.2)]), (pulse_ring, "scaleY", [(0, 0.2), (1.4, 3.2)]),
                           (pulse_ring2, "opacity", [(0, 0.0), (0.12, 0.8), (1.4, 0.0)]),
                           (pulse_ring2, "scaleX", [(0, 0.2), (1.4, 2.8)]), (pulse_ring2, "scaleY", [(0, 0.2), (1.4, 2.8)])])
    A("fx_wake", 0.8, 0, [(glow, "scaleX", [(0, 1.0), (0.15, 1.5), (0.8, 1.0)]), (glow, "scaleY", [(0, 1.0), (0.15, 1.5), (0.8, 1.0)])])
    A("fx_fault", 0.9, 0, [(flash, "opacity", [(0, 0.0), (0.1, 1.0), (0.9, 0.0)])])
    A("fx_recover", 1.8, 0, [(recover_ring, "opacity", [(0, 0.9), (1.8, 0.0)]),
                             (recover_ring, "scaleX", [(0, 1.7), (1.8, 1.0)]), (recover_ring, "scaleY", [(0, 1.7), (1.8, 1.0)])])
    A("fx_inspect", 0.5, 0, [(glow, "scaleX", [(0, 1.0), (0.12, 1.2), (0.5, 1.0)]), (glow, "scaleY", [(0, 1.0), (0.12, 1.2), (0.5, 1.0)])])
    A("fx_acknowledge", 0.6, 0, [(reticle, "scaleX", [(0, 1.0), (0.15, 1.03), (0.6, 1.0)]),
                                 (reticle, "scaleY", [(0, 1.0), (0.15, 1.03), (0.6, 1.0)])])

    # Press: contact 0–250 ms, charge 250–800 ms, charged hold.
    A("press_idle", 0.1, 0, [(press, "scaleX", [(0, 1.0)]), (press, "scaleY", [(0, 1.0)])])
    A("press_contact", 0.25, 0, [(press, "scaleX", [(0, 1.0), (0.25, 0.97)]), (press, "scaleY", [(0, 1.0), (0.25, 0.96)])])
    A("press_charging", 0.55, 0, [(press, "scaleX", [(0, 0.97), (0.55, 0.9)]), (press, "scaleY", [(0, 0.96), (0.55, 0.88)])])
    A("press_charged", 0.6, 1, [(press, "scaleX", sine_keys(0.6, 0.9, 0.008)), (press, "scaleY", sine_keys(0.6, 0.88, 0.008, 1.0))])

    # Mode: explode/collapse separate the six subsystems and shrink the Core.
    def mode_tracks(t0, t1, explode_from, explode_to):
        def core_scale(e):
            return 1.0 - 0.3 * e

        tracks = [(links, "opacity", [(t0, explode_from), (t1, explode_to)]),
                  (core, "scaleX", [(t0, core_scale(explode_from)), (t1, core_scale(explode_to))]),
                   (core, "scaleY", [(t0, core_scale(explode_from)), (t1, core_scale(explode_to))])]
        for i, n in enumerate(subsystem_nodes):
            a = rad(SUBSYSTEM_ANGLES[i])
            # Nodes live under the scaled Core, so compensate distance for the shrink.
            d0 = (RETICLE + (EXPLODED - RETICLE) * explode_from) / core_scale(explode_from)
            d1 = (RETICLE + (EXPLODED - RETICLE) * explode_to) / core_scale(explode_to)
            tracks += [(n, "x", [(t0, math.cos(a) * d0), (t1, math.cos(a) * d1)]),
                       (n, "y", [(t0, math.sin(a) * d0), (t1, math.sin(a) * d1)]),
                       (link_ends[i], "vx", [(t0, math.cos(a) * (d0 - 18)), (t1, math.cos(a) * (d1 - 18))]),
                       (link_ends[i], "vy", [(t0, math.sin(a) * (d0 - 18)), (t1, math.sin(a) * (d1 - 18))]),
                       (subsystem_rings[i], "scaleX", [(t0, 0.35 + 0.65 * explode_from), (t1, 0.35 + 0.65 * explode_to)]),
                       (subsystem_rings[i], "scaleY", [(t0, 0.35 + 0.65 * explode_from), (t1, 0.35 + 0.65 * explode_to)])]
        return tracks

    A("mode_collapsed", 0.1, 0, mode_tracks(0, 0, 0, 0))
    A("mode_exploding", 0.9, 0, mode_tracks(0, 0.9, 0, 1))
    A("mode_exploded", 0.1, 0, mode_tracks(0, 0, 1, 1))
    A("mode_collapsing", 0.7, 0, mode_tracks(0, 0.7, 1, 0))

    b.write_animations(ease)

    # ── state machine ──────────────────────────────────────────────────────
    f.add = orig_add  # state machine objects are not artboard components; ids below are local indices
    f.add("StateMachine", name="LumenCore")
    inputs = {}

    def number(name, value=0.0):
        inputs[name] = len(inputs)
        f.add("StateMachineNumber", name=name, value=float(value))

    def trigger(name):
        inputs[name] = len(inputs)
        f.add("StateMachineTrigger", name=name)

    def boolean(name, value=False):
        inputs[name] = len(inputs)
        f.add("StateMachineBool", name=name, value=value)

    number("systemState", 0)
    for n, v in [("health", 1), ("power", 0.8), ("temperature", 0.2), ("load", 0.5), ("latency", 0.4),
                 ("pointerX", 0), ("pointerY", 0), ("pointerDistance", 1), ("interactionForce", 0),
                 ("gravityX", 0), ("gravityY", 0)]:
        number(n, v)
    for t in ["wake", "pulse", "fault", "recover", "explode", "collapse", "inspect", "acknowledge"]:
        trigger(t)
    boolean("pressed")                 # extension: set by Rive listeners on the Core hit area
    number("stressedSubsystem", -1)    # extension: localized instability (-1 = none)

    # Listeners: pointer on the Core and on each subsystem node.
    def listener(name, target, kind, actions):
        f.add("StateMachineListenerSingle", name=name, targetId=target, listenerTypeValue=kind)
        for type_name, props in actions:
            f.add(type_name, **props)

    DOWN, UP = 2, 3
    listener("CoreDown", hit_area, DOWN, [("ListenerBoolChange", {"inputId": inputs["pressed"], "value": 1}),
                                          ("ListenerFireEvent", {"eventId": b.events["CorePressed"]})])
    listener("CoreUp", hit_area, UP, [("ListenerBoolChange", {"inputId": inputs["pressed"], "value": 0}),
                                      ("ListenerFireEvent", {"eventId": b.events["CoreReleased"]})])
    for i, hit in enumerate(subsystem_links):
        listener(f"Select{SUBSYSTEMS[i]}", hit, DOWN, [("ListenerFireEvent", {"eventId": b.events[f"SubsystemSelected{i}"]})])

    ENABLE_EXIT = 1 << 2
    EXIT_PERCENT = 1 << 3
    EQUAL, GTE, LT = 0, 3, 4

    def layer(name, states, entry, any_transitions=()):
        """states: list of dicts {name, anim|blend, transitions:[(to, conds, duration_ms, exit_pct)], fire:[(event, occurs)]}"""
        f.add("StateMachineLayer", name=name)
        index = {"__entry": 0, "__any": 1, "__exit": 2}
        for i, s in enumerate(states):
            index[s["name"]] = 3 + i

        def transitions(items):
            for to, conds, duration, exit_pct in items:
                flags = 0
                props = {"stateToId": index[to], "duration": int(duration), "interpolationType": 2, "interpolatorId": ease}
                if exit_pct is not None:
                    flags |= ENABLE_EXIT | EXIT_PERCENT
                    props["exitTime"] = int(exit_pct)
                props["flags"] = flags
                f.add("StateTransition", **props)
                for cond in conds:
                    kind = cond[0]
                    if kind == "trigger":
                        f.add("TransitionTriggerCondition", inputId=inputs[cond[1]])
                    elif kind == "bool":
                        f.add("TransitionBoolCondition", inputId=inputs[cond[1]], opValue=0 if cond[2] else 1)
                    else:
                        f.add("TransitionNumberCondition", inputId=inputs[cond[1]], opValue=cond[2], value=float(cond[3]))

        f.add("EntryState")
        transitions([(entry, [], 0, None)])
        f.add("AnyState")
        transitions(any_transitions)
        f.add("ExitState")
        for s in states:
            if "blend" in s:
                input_name, points = s["blend"]
                f.add("BlendState1DInput", inputId=inputs[input_name])
                for anim_name, value in points:
                    f.add("BlendAnimation1D", animationId=b.anim_index[anim_name], value=float(value))
            else:
                f.add("AnimationState", animationId=b.anim_index[s["anim"]])
            for event, occurs in s.get("fire", []):
                f.add("StateMachineFireEvent", eventId=b.events[event], occursValue=occurs)
            transitions(s.get("transitions", []))

    AT_START, AT_END = 0, 1

    # 1. System state: C# owns thresholds, this layer only expresses the state it is told.
    state_layer = []
    for i, name in enumerate(STATES):
        state_layer.append({"name": name, "anim": f"state_{name.lower()}",
                            "transitions": [(other, [("number", "systemState", EQUAL, j)], 800, None)
                                            for j, other in enumerate(STATES) if other != name]})
    layer("SystemState", state_layer, "Offline")

    # 2. Independent motion loops (mismatched periods).
    for name in ["breath", "drift", "orbit", "spin"]:
        layer(name.capitalize(), [{"name": name, "anim": name}], name)
    def beat_to(target, value):
        return (target, [("number", "systemState", EQUAL, value)], 300, None)

    layer("Heartbeat", [
        {"name": "calm", "anim": "heartbeat", "transitions": [beat_to("fast", 3), beat_to("fast", 4), beat_to("irregular", 5)]},
        {"name": "fast", "anim": "heartbeat_fast",
         "transitions": [beat_to("irregular", 5)] + [beat_to("calm", v) for v in (0, 1, 2, 6)]},
        {"name": "irregular", "anim": "heartbeat_irregular",
         "transitions": [beat_to("fast", 3), beat_to("fast", 4)] + [beat_to("calm", v) for v in (0, 1, 2, 6)]},
    ], "calm")

    # 3. Numeric inputs → continuous presentation.
    blends = [("Load", "load", [("load_low", 0), ("load_high", 1)]),
              ("Flow", "load", [("flow_calm", 0), ("flow_intense", 1)]),
              ("Health", "health", [("health_low", 0), ("health_high", 1)]),
              ("Power", "power", [("power_low", 0), ("power_high", 1)]),
              ("Temperature", "temperature", [("temperature_low", 0), ("temperature_high", 1)]),
              ("Latency", "latency", [("latency_low", 0), ("latency_high", 1)]),
              ("PointerX", "pointerX", [("pointerX_left", -1), ("pointerX_right", 1)]),
              ("PointerY", "pointerY", [("pointerY_up", -1), ("pointerY_down", 1)]),
              ("Proximity", "pointerDistance", [("proximity_near", 0), ("proximity_far", 1)]),
              ("Force", "interactionForce", [("force_none", 0), ("force_full", 1)]),
              ("GravityX", "gravityX", [("gravityX_left", -1), ("gravityX_right", 1)]),
              ("GravityY", "gravityY", [("gravityY_up", -1), ("gravityY_down", 1)]),
              ("Stress", "stressedSubsystem", [(n, k - 1) for k, n in enumerate(stress_names)])]
    for layer_name, input_name, points in blends:
        layer(layer_name, [{"name": "blend", "blend": (input_name, points)}], "blend")

    # 4. Triggers.
    fx_states = [{"name": "idle", "anim": "idle"}]
    fx_any = []
    for t in ["wake", "pulse", "fault", "recover", "inspect", "acknowledge"]:
        state = {"name": t, "anim": f"fx_{t}", "transitions": [("idle", [], 0, 100)]}
        if t == "pulse":
            state["fire"] = [("PulseCompleted", AT_END)]
        if t == "recover":
            state["fire"] = [("RecoveryVisualCompleted", AT_END)]
        fx_states.append(state)
        fx_any.append((t, [("trigger", t)], 0, None))
    layer("Triggers", fx_states, "idle", fx_any)

    # 5. Press (listeners set `pressed`; Uno can also drive interactionForce directly).
    released = ("idle", [("bool", "pressed", False)], 200, None)
    layer("Press", [
        {"name": "idle", "anim": "press_idle", "transitions": [("contact", [("bool", "pressed", True)], 0, None)]},
        {"name": "contact", "anim": "press_contact", "transitions": [("charging", [], 0, 100), released]},
        {"name": "charging", "anim": "press_charging", "transitions": [("charged", [], 0, 100), released]},
        {"name": "charged", "anim": "press_charged", "fire": [("CoreCharged", AT_START)], "transitions": [released]},
    ], "idle")

    # 6. Mode: explode / collapse.
    layer("Mode", [
        {"name": "collapsed", "anim": "mode_collapsed", "transitions": [("exploding", [("trigger", "explode")], 0, None)]},
        {"name": "exploding", "anim": "mode_exploding", "transitions": [("exploded", [], 0, 100)]},
        {"name": "exploded", "anim": "mode_exploded", "fire": [("ExplodeCompleted", AT_START)],
         "transitions": [("collapsing", [("trigger", "collapse")], 0, None)]},
        {"name": "collapsing", "anim": "mode_collapsing", "transitions": [("collapsedDone", [], 0, 100)]},
        {"name": "collapsedDone", "anim": "mode_collapsed", "fire": [("CollapseCompleted", AT_START)],
         "transitions": [("exploding", [("trigger", "explode")], 0, None)]},
    ], "collapsed")

    return f.to_bytes()


if __name__ == "__main__":
    data = build()
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_bytes(data)
    print(f"wrote {OUT} ({len(data)} bytes)")
