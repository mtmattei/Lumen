using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace Lumen.Controls;

/// <summary>Skia sparkline fed at telemetry rate. Avoids re-laying out XAML shapes every tick.</summary>
public sealed class Sparkline : SKCanvasElement
{
    private const int Capacity = 90;
    private readonly double[] _values = new double[Capacity];
    private readonly SKPaint _line = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.4f };
    private readonly SKPaint _fill = new() { IsAntialias = true, Style = SKPaintStyle.Fill };
    private readonly SKPath _path = new();
    private int _count;
    private int _head;

    public Sparkline()
    {
        Height = 28;
    }

    public SKColor Color { get; set; } = new(0xF1, 0xF2, 0xEF);

    public double? FixedMinimum { get; set; }

    public double? FixedMaximum { get; set; }

    public void Push(double value)
    {
        _values[_head] = value;
        _head = (_head + 1) % Capacity;
        _count = Math.Min(Capacity, _count + 1);
        Invalidate();
    }

    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        if (_count < 2) return;
        var w = (float)area.Width;
        var h = (float)area.Height;

        var min = FixedMinimum ?? double.MaxValue;
        var max = FixedMaximum ?? double.MinValue;
        if (FixedMinimum is null || FixedMaximum is null)
        {
            for (var i = 0; i < _count; i++)
            {
                var v = _values[(_head - _count + i + Capacity) % Capacity];
                if (FixedMinimum is null) min = Math.Min(min, v);
                if (FixedMaximum is null) max = Math.Max(max, v);
            }
        }

        var range = Math.Max(1e-6, max - min);
        min -= range * 0.2;
        range *= 1.4;

        _path.Reset();
        for (var i = 0; i < _count; i++)
        {
            var v = _values[(_head - _count + i + Capacity) % Capacity];
            var x = w * i / (Capacity - 1);
            var y = h - (float)((v - min) / range * h);
            if (i == 0) _path.MoveTo(x, y); else _path.LineTo(x, y);
        }

        _line.Color = Color.WithAlpha(200);
        canvas.DrawPath(_path, _line);

        var lastX = w * (_count - 1) / (Capacity - 1);
        _path.LineTo(lastX, h);
        _path.LineTo(0, h);
        _path.Close();
        using var shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, h), [Color.WithAlpha(40), Color.WithAlpha(0)], SKShaderTileMode.Clamp);
        _fill.Shader = shader;
        canvas.DrawPath(_path, _fill);
        _fill.Shader = null;
    }
}
