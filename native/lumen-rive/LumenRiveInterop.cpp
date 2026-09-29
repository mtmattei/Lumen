// LUMEN ↔ Rive runtime bridge.
//
// Adapted from rive-app/rive-sharp native/RiveSharpInterop.cpp (MIT, Copyright 2022 Rive).
// Rendering is delegated back to managed code through one table of C function pointers, so the
// Core draws with SkiaSharp onto the same canvas Uno composes (SKCanvasElement).

#include "rive/animation/state_machine_instance.hpp"
#include "rive/animation/state_machine_input_instance.hpp"
#include "rive/artboard.hpp"
#include "rive/custom_property_number.hpp"
#include "rive/event.hpp"
#include "rive/event_report.hpp"
#include "rive/factory.hpp"
#include "rive/file.hpp"
#include "rive/math/raw_path.hpp"
#include "rive/renderer.hpp"
#include "utils/factory_utils.hpp"

#include <cstring>
#include <memory>
#include <vector>

using namespace rive;

#if defined(_WIN32)
#define LUMEN_EXPORT extern "C" __declspec(dllexport)
#define LUMEN_CALL __cdecl
#else
#define LUMEN_EXPORT extern "C" __attribute__((visibility("default")))
#define LUMEN_CALL
#endif

// Managed objects are GCHandles passed as intptr_t. All callbacks are cdecl and blittable.
struct LumenCallbacks
{
    // Factory
    intptr_t(LUMEN_CALL* makePath)();
    intptr_t(LUMEN_CALL* makePaint)();
    void(LUMEN_CALL* release)(intptr_t ref);
    // Path
    void(LUMEN_CALL* pathRewind)(intptr_t path);
    void(LUMEN_CALL* pathFillRule)(intptr_t path, int32_t rule);
    void(LUMEN_CALL* pathMoveTo)(intptr_t path, float x, float y);
    void(LUMEN_CALL* pathLineTo)(intptr_t path, float x, float y);
    void(LUMEN_CALL* pathCubicTo)(intptr_t path, float ox, float oy, float ix, float iy, float x, float y);
    void(LUMEN_CALL* pathClose)(intptr_t path);
    void(LUMEN_CALL* pathAddPath)(intptr_t path, intptr_t source, float xx, float xy, float yx, float yy, float tx, float ty);
    // Paint
    void(LUMEN_CALL* paintStyle)(intptr_t paint, int32_t style);
    void(LUMEN_CALL* paintColor)(intptr_t paint, uint32_t argb);
    void(LUMEN_CALL* paintThickness)(intptr_t paint, float thickness);
    void(LUMEN_CALL* paintJoin)(intptr_t paint, int32_t join);
    void(LUMEN_CALL* paintCap)(intptr_t paint, int32_t cap);
    void(LUMEN_CALL* paintBlendMode)(intptr_t paint, int32_t mode);
    void(LUMEN_CALL* paintLinearGradient)(intptr_t paint, float sx, float sy, float ex, float ey,
                                          const uint32_t* colors, const float* stops, int32_t count);
    void(LUMEN_CALL* paintRadialGradient)(intptr_t paint, float cx, float cy, float radius,
                                          const uint32_t* colors, const float* stops, int32_t count);
    void(LUMEN_CALL* paintClearShader)(intptr_t paint);
    // Renderer
    void(LUMEN_CALL* save)(intptr_t renderer);
    void(LUMEN_CALL* restore)(intptr_t renderer);
    void(LUMEN_CALL* transform)(intptr_t renderer, float xx, float xy, float yx, float yy, float tx, float ty);
    void(LUMEN_CALL* drawPath)(intptr_t renderer, intptr_t path, intptr_t paint);
    void(LUMEN_CALL* clipPath)(intptr_t renderer, intptr_t path);
    void(LUMEN_CALL* modulateOpacity)(intptr_t renderer, float opacity);
};

static LumenCallbacks g_cb{};

LUMEN_EXPORT void LUMEN_CALL lumen_rive_register(const LumenCallbacks* callbacks) { g_cb = *callbacks; }

LUMEN_EXPORT int32_t LUMEN_CALL lumen_rive_abi_version() { return 1; }

// ── Render objects ──────────────────────────────────────────────────────────

class LumenPath : public RenderPath
{
public:
    LumenPath() : m_ref(g_cb.makePath()) {}
    ~LumenPath() override { g_cb.release(m_ref); }

    void rewind() override { g_cb.pathRewind(m_ref); }
    void fillRule(FillRule value) override { g_cb.pathFillRule(m_ref, (int32_t)value); }
    void moveTo(float x, float y) override { g_cb.pathMoveTo(m_ref, x, y); }
    void lineTo(float x, float y) override { g_cb.pathLineTo(m_ref, x, y); }
    void cubicTo(float ox, float oy, float ix, float iy, float x, float y) override
    {
        g_cb.pathCubicTo(m_ref, ox, oy, ix, iy, x, y);
    }
    void close() override { g_cb.pathClose(m_ref); }

    void addRenderPath(const RenderPath* path, const Mat2D& m) override
    {
        g_cb.pathAddPath(m_ref, static_cast<const LumenPath*>(path)->m_ref, m.xx(), m.xy(), m.yx(), m.yy(), m.tx(), m.ty());
    }

    void addRawPath(const RawPath& path) override
    {
        for (auto [verb, pts] : path)
        {
            switch (verb)
            {
                case PathVerb::move:
                    moveTo(pts[0].x, pts[0].y);
                    break;
                case PathVerb::line:
                    lineTo(pts[1].x, pts[1].y);
                    break;
                case PathVerb::quad:
                {
                    // Elevate to cubic: control points at 2/3 toward the quad control.
                    Vec2D p0 = pts[0], q = pts[1], p1 = pts[2];
                    Vec2D c0 = p0 + (q - p0) * (2.0f / 3.0f);
                    Vec2D c1 = p1 + (q - p1) * (2.0f / 3.0f);
                    cubicTo(c0.x, c0.y, c1.x, c1.y, p1.x, p1.y);
                    break;
                }
                case PathVerb::cubic:
                    cubicTo(pts[1].x, pts[1].y, pts[2].x, pts[2].y, pts[3].x, pts[3].y);
                    break;
                case PathVerb::close:
                    close();
                    break;
            }
        }
    }

    const intptr_t m_ref;
};

struct LumenShader : public RenderShader
{
    virtual void apply(intptr_t paint) const = 0;
};

struct LinearShader : LumenShader
{
    LinearShader(float sx, float sy, float ex, float ey, const ColorInt* c, const float* s, size_t n) :
        sx(sx), sy(sy), ex(ex), ey(ey), colors(c, c + n), stops(s, s + n)
    {}
    void apply(intptr_t paint) const override
    {
        g_cb.paintLinearGradient(paint, sx, sy, ex, ey, colors.data(), stops.data(), (int32_t)colors.size());
    }
    float sx, sy, ex, ey;
    std::vector<uint32_t> colors;
    std::vector<float> stops;
};

struct RadialShader : LumenShader
{
    RadialShader(float cx, float cy, float r, const ColorInt* c, const float* s, size_t n) :
        cx(cx), cy(cy), radius(r), colors(c, c + n), stops(s, s + n)
    {}
    void apply(intptr_t paint) const override
    {
        g_cb.paintRadialGradient(paint, cx, cy, radius, colors.data(), stops.data(), (int32_t)colors.size());
    }
    float cx, cy, radius;
    std::vector<uint32_t> colors;
    std::vector<float> stops;
};

class LumenPaint : public RenderPaint
{
public:
    LumenPaint() : m_ref(g_cb.makePaint()) {}
    ~LumenPaint() override { g_cb.release(m_ref); }

    void style(RenderPaintStyle value) override { g_cb.paintStyle(m_ref, (int32_t)value); }
    void color(ColorInt value) override { g_cb.paintColor(m_ref, value); }
    void thickness(float value) override { g_cb.paintThickness(m_ref, value); }
    void join(StrokeJoin value) override { g_cb.paintJoin(m_ref, (int32_t)value); }
    void cap(StrokeCap value) override { g_cb.paintCap(m_ref, (int32_t)value); }
    void blendMode(BlendMode value) override { g_cb.paintBlendMode(m_ref, (int32_t)value); }
    void shader(rcp<RenderShader> shader) override
    {
        if (shader)
            static_cast<LumenShader*>(shader.get())->apply(m_ref);
        else
            g_cb.paintClearShader(m_ref);
    }
    void invalidateStroke() override {}

    const intptr_t m_ref;
};

class LumenRenderer : public Renderer
{
public:
    explicit LumenRenderer(intptr_t ref) : m_ref(ref) {}

    void save() override { g_cb.save(m_ref); }
    void restore() override { g_cb.restore(m_ref); }
    void transform(const Mat2D& m) override { g_cb.transform(m_ref, m.xx(), m.xy(), m.yx(), m.yy(), m.tx(), m.ty()); }
    void drawPath(RenderPath* path, RenderPaint* paint) override
    {
        g_cb.drawPath(m_ref, static_cast<LumenPath*>(path)->m_ref, static_cast<LumenPaint*>(paint)->m_ref);
    }
    void clipPath(RenderPath* path) override { g_cb.clipPath(m_ref, static_cast<LumenPath*>(path)->m_ref); }
    void modulateOpacity(float opacity) override { g_cb.modulateOpacity(m_ref, opacity); }

    // lumen-core.riv has no images; images are ignored rather than half-supported.
    void drawImage(const RenderImage*, ImageSampler, BlendMode, float) override {}
    void drawImageMesh(const RenderImage*, ImageSampler, rcp<RenderBuffer>, rcp<RenderBuffer>, rcp<RenderBuffer>,
                       uint32_t, uint32_t, BlendMode, float) override
    {}

private:
    intptr_t m_ref;
};

class LumenFactory : public Factory
{
public:
    rcp<RenderBuffer> makeRenderBuffer(RenderBufferType type, RenderBufferFlags flags, size_t size) override
    {
        return make_rcp<DataRenderBuffer>(type, flags, size);
    }
    rcp<RenderShader> makeLinearGradient(float sx, float sy, float ex, float ey, const ColorInt colors[],
                                         const float stops[], size_t count) override
    {
        return make_rcp<LinearShader>(sx, sy, ex, ey, colors, stops, count);
    }
    rcp<RenderShader> makeRadialGradient(float cx, float cy, float radius, const ColorInt colors[],
                                         const float stops[], size_t count) override
    {
        return make_rcp<RadialShader>(cx, cy, radius, colors, stops, count);
    }
    rcp<RenderPath> makeRenderPath(RawPath& rawPath, FillRule fillRule) override
    {
        auto path = make_rcp<LumenPath>();
        path->fillRule(fillRule);
        path->addRawPath(rawPath);
        return path;
    }
    rcp<RenderPath> makeEmptyRenderPath() override { return make_rcp<LumenPath>(); }
    rcp<RenderPaint> makeRenderPaint() override { return make_rcp<LumenPaint>(); }
    rcp<RenderImage> decodeImage(Span<const uint8_t>) override { return nullptr; }
};

// ── Scene ───────────────────────────────────────────────────────────────────

struct LumenScene
{
    LumenFactory factory;
    rcp<File> file;
    std::unique_ptr<ArtboardInstance> artboard;
    std::unique_ptr<StateMachineInstance> machine;

    Mat2D alignment(float width, float height) const
    {
        return computeAlignment(Fit::contain, Alignment::center, AABB(0, 0, width, height), artboard->bounds());
    }
};

LUMEN_EXPORT LumenScene* LUMEN_CALL lumen_rive_scene_new() { return new LumenScene(); }

LUMEN_EXPORT void LUMEN_CALL lumen_rive_scene_delete(LumenScene* scene) { delete scene; }

// Returns 1 on success; 0 = bad file, -1 = artboard missing, -2 = state machine missing.
LUMEN_EXPORT int32_t LUMEN_CALL lumen_rive_scene_load(LumenScene* scene, const uint8_t* bytes, int32_t length,
                                                       const char* artboard, const char* stateMachine)
{
    scene->machine.reset();
    scene->artboard.reset();
    scene->file = File::import(Span<const uint8_t>(bytes, (size_t)length), &scene->factory);
    if (!scene->file)
        return 0;
    scene->artboard = artboard && artboard[0] ? scene->file->artboardNamed(artboard) : scene->file->artboardDefault();
    if (!scene->artboard)
        return -1;
    scene->machine = stateMachine && stateMachine[0] ? scene->artboard->stateMachineNamed(stateMachine)
                                                     : scene->artboard->stateMachineAt(0);
    if (!scene->machine)
        return -2;
    scene->machine->advanceAndApply(0);
    return 1;
}

LUMEN_EXPORT float LUMEN_CALL lumen_rive_scene_width(LumenScene* scene) { return scene->artboard ? scene->artboard->width() : 0; }
LUMEN_EXPORT float LUMEN_CALL lumen_rive_scene_height(LumenScene* scene) { return scene->artboard ? scene->artboard->height() : 0; }

LUMEN_EXPORT int32_t LUMEN_CALL lumen_rive_set_number(LumenScene* scene, const char* name, float value)
{
    if (auto* input = scene->machine ? scene->machine->getNumber(name) : nullptr)
    {
        input->value(value);
        return 1;
    }
    return 0;
}

LUMEN_EXPORT int32_t LUMEN_CALL lumen_rive_set_bool(LumenScene* scene, const char* name, int32_t value)
{
    if (auto* input = scene->machine ? scene->machine->getBool(name) : nullptr)
    {
        input->value(value != 0);
        return 1;
    }
    return 0;
}

LUMEN_EXPORT int32_t LUMEN_CALL lumen_rive_fire(LumenScene* scene, const char* name)
{
    if (auto* input = scene->machine ? scene->machine->getTrigger(name) : nullptr)
    {
        input->fire();
        return 1;
    }
    return 0;
}

LUMEN_EXPORT int32_t LUMEN_CALL lumen_rive_advance(LumenScene* scene, float seconds)
{
    return scene->machine && scene->machine->advanceAndApply(seconds) ? 1 : 0;
}

// Draws contain-fitted and centred inside width × height.
LUMEN_EXPORT void LUMEN_CALL lumen_rive_draw(LumenScene* scene, intptr_t renderer, float width, float height)
{
    if (!scene->machine)
        return;
    LumenRenderer r(renderer);
    r.save();
    r.transform(scene->alignment(width, height));
    scene->machine->draw(&r);
    r.restore();
}

// kind: 0 down, 1 move, 2 up. Coordinates are in the drawn surface (same width/height as draw).
LUMEN_EXPORT void LUMEN_CALL lumen_rive_pointer(LumenScene* scene, int32_t kind, float x, float y, float width, float height)
{
    if (!scene->machine)
        return;
    Mat2D inverse;
    if (!scene->alignment(width, height).invert(&inverse))
        return;
    Vec2D p = inverse * Vec2D(x, y);
    switch (kind)
    {
        case 0:
            scene->machine->pointerDown(p);
            break;
        case 1:
            scene->machine->pointerMove(p);
            break;
        default:
            scene->machine->pointerUp(p);
            break;
    }
}

// Events reported during the last advance.
LUMEN_EXPORT int32_t LUMEN_CALL lumen_rive_event_count(LumenScene* scene)
{
    return scene->machine ? (int32_t)scene->machine->reportedEventCount() : 0;
}

// Copies the UTF-8 name into buffer (if large enough); returns its byte length, or -1.
LUMEN_EXPORT int32_t LUMEN_CALL lumen_rive_event_name(LumenScene* scene, int32_t index, char* buffer, int32_t capacity)
{
    if (!scene->machine || index < 0 || (size_t)index >= scene->machine->reportedEventCount())
        return -1;
    const std::string& name = scene->machine->reportedEventAt((size_t)index).event()->name();
    if (buffer && capacity >= (int32_t)name.size())
        std::memcpy(buffer, name.data(), name.size());
    return (int32_t)name.size();
}

// Reads a custom number property of a reported event (e.g. SubsystemSelected.id). Returns 1 if found.
LUMEN_EXPORT int32_t LUMEN_CALL lumen_rive_event_number(LumenScene* scene, int32_t index, const char* property, float* value)
{
    if (!scene->machine || index < 0 || (size_t)index >= scene->machine->reportedEventCount())
        return 0;
    const Event* event = scene->machine->reportedEventAt((size_t)index).event();
    for (auto* child : event->children())
    {
        if (child->is<CustomPropertyNumber>() && child->name() == property)
        {
            *value = child->as<CustomPropertyNumber>()->propertyValue();
            return 1;
        }
    }
    return 0;
}
