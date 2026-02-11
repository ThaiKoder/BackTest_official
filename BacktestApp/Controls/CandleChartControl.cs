using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BacktestApp.Controls;

public sealed class CandleChartControl : Control
{
    public record struct Candle(double O, double H, double L, double C);

    // ✅ 3 candles de test
    private readonly Candle[] _candles =
    [
        new Candle(100, 115, 95, 110),
        new Candle(110, 112, 98, 102),
        new Candle(102, 125, 101, 123),
    ];

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var r = new Rect(0, 0, Bounds.Width, Bounds.Height);

        // Background
        ctx.FillRectangle(new SolidColorBrush(Color.FromRgb(0x19, 0x19, 0x19)), r);

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        // --- mapping prix -> y ---
        double min = double.MaxValue, max = double.MinValue;
        foreach (var c in _candles)
        {
            min = Math.Min(min, c.L);
            max = Math.Max(max, c.H);
        }

        // marge pour que ça respire
        var pad = 12.0;
        var plot = new Rect(pad, pad, Math.Max(0, Bounds.Width - 2 * pad), Math.Max(0, Bounds.Height - 2 * pad));
        if (plot.Width <= 0 || plot.Height <= 0) return;

        double span = Math.Max(1e-9, max - min);
        double Y(double price)
        {
            var t = (price - min) / span;             // 0..1
            return plot.Bottom - t * plot.Height;     // inversé
        }

        // --- layout candles ---
        double gap = 18.0;
        double bodyW = 18.0;
        double x0 = plot.Left + 20.0;

        var upBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xC2, 0x7E));
        var dnBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A));
        var wickPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)), 1);

        for (int i = 0; i < _candles.Length; i++)
        {
            var c = _candles[i];

            double xCenter = x0 + i * (bodyW + gap);

            double yH = Y(c.H);
            double yL = Y(c.L);
            double yO = Y(c.O);
            double yC = Y(c.C);

            bool up = c.C >= c.O;
            var brush = up ? upBrush : dnBrush;

            // wick
            ctx.DrawLine(wickPen, new Point(xCenter, yH), new Point(xCenter, yL));

            // body
            double top = Math.Min(yO, yC);
            double bot = Math.Max(yO, yC);
            double h = Math.Max(2, bot - top); // body min 2px
            var body = new Rect(xCenter - bodyW / 2, top, bodyW, h);

            ctx.FillRectangle(brush, body);
        }
    }
}