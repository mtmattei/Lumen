using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace Lumen.Controls;

/// <summary>Thin health arc. The state word sits on top in XAML so it stays accessible text.</summary>
public sealed class HealthRing : SKCanvasElement
{
    private readonly SKPaint _track = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3, Color = new SKColor(0x2D, 0x30, 0x2E) };
    private readonly SKPaint _arc = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3, StrokeCap = SKStrokeCap.Round };
    private double _value;
    private double _shown;
    private SKColor _color = new(0x7F, 0xB8, 0x9A);

    public void Update(double value, SKColor color)
    {
        _value = Math.Clamp(value, 0, 1);
        _color = color;
        if (Math.Abs(_shown - _value) > 0.0005) Invalidate();
    }

    /// <summary>Eases the arc toward the latest value; called from the frame loop.</summary>
    public void Step(double dt)
    {
        var next = _shown + (_value - _shown) * (1 - Math.Exp(-dt / 0.35));
        if (Math.Abs(next - _shown) > 0.0005)
        {
            _shown = next;
            Invalidate();
        }
    }

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var size = (float)Math.Min(area.Width, area.Height) - 6;
        var rect = SKRect.Create(((float)area.Width - size) / 2, ((float)area.Height - size) / 2, size, size);
        canvas.DrawOval(rect, _track);
        _arc.Color = _color;
        canvas.DrawArc(rect, 90, (float)(360 * _shown), false, _arc);
    }
}
