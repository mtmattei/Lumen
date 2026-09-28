using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace Lumen.Rive;

/// <summary>Draw host for the Core. In a Rive build this is the Rive view.</summary>
public sealed class LumenCoreCanvas : SKCanvasElement
{
    public LumenCoreRenderer? Renderer { get; set; }

    protected override void RenderOverride(SKCanvas canvas, Size area)
        => Renderer?.Render(canvas, (float)area.Width, (float)area.Height);

    public void RequestRedraw() => Invalidate();
}
