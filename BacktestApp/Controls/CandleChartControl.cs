using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace BacktestApp.Controls;

public sealed class CandleChartControl : Control
{
    public readonly record struct Candle(DateTime T, double O, double H, double L, double C);

    private readonly Candle[] _candles =
    [
        new Candle(new DateTime(2026, 02, 11, 10, 00, 00), 100, 115,  95, 110),
        new Candle(new DateTime(2026, 02, 11, 10, 01, 00), 110, 112,  98, 102),
        new Candle(new DateTime(2026, 02, 11, 10, 02, 00), 102, 125, 101, 123),
    ];

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        // Brushes / pens
        var bg = new SolidColorBrush(Color.FromRgb(0x19, 0x19, 0x19));
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), 1);
        var labelBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
        var wickPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)), 1);
        var upBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xC2, 0x7E));
        var dnBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A));

        ctx.FillRectangle(bg, bounds);

        // --- layout: reserve space for axes labels ---
        double leftAxisW = 70;   // Y labels
        double bottomAxisH = 28; // X labels
        double pad = 10;

        var plot = new Rect(
            x: leftAxisW + pad,
            y: pad,
            width: Math.Max(0, bounds.Width - (leftAxisW + pad) - pad),
            height: Math.Max(0, bounds.Height - bottomAxisH - pad - pad)
        );

        if (plot.Width <= 0 || plot.Height <= 0) return;

        // --- range min/max (price) ---
        double min = double.MaxValue, max = double.MinValue;
        foreach (var c in _candles)
        {
            min = Math.Min(min, c.L);
            max = Math.Max(max, c.H);
        }
        if (min == double.MaxValue || max == double.MinValue) return;

        // small margin
        var span = Math.Max(1e-9, max - min);
        min -= span * 0.05;
        max += span * 0.05;
        span = Math.Max(1e-9, max - min);

        double Y(double price)
        {
            var t = (price - min) / span;        // 0..1
            return plot.Bottom - t * plot.Height;
        }

        // --- axes ---
        // Y axis line
        ctx.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        // X axis line
        ctx.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        // --- Y ticks (price) ---
        int yTicks = 5;
        for (int i = 0; i <= yTicks; i++)
        {
            double t = i / (double)yTicks;
            double price = min + t * (max - min);
            double y = Y(price);

            // tick mark
            ctx.DrawLine(axisPen, new Point(plot.Left - 4, y), new Point(plot.Left, y));

            // label (right-aligned in left gutter)
            DrawText(ctx,
                text: price.ToString("0.##", CultureInfo.InvariantCulture),
                x: 6,
                y: y - 8,
                brush: labelBrush);
        }

        // --- X ticks (time) ---
        // With 3 candles: show each candle time as a tick
        int n = _candles.Length;
        if (n > 0)
        {
            for (int i = 0; i < n; i++)
            {
                double x = plot.Left + (n == 1 ? plot.Width / 2 : (i / (double)(n - 1)) * plot.Width);

                // tick mark
                ctx.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));

                // label
                var label = _candles[i].T.ToString("HH:mm", CultureInfo.InvariantCulture);
                DrawText(ctx, label, x - 16, plot.Bottom + 6, labelBrush);
            }
        }

        // --- candles ---
        double gap = 18.0;
        double bodyW = 18.0;
        double x0 = plot.Left + 20.0;

        for (int i = 0; i < _candles.Length; i++)
        {
            var c = _candles[i];

            double xCenter = x0 + i * (bodyW + gap);
            if (xCenter < plot.Left || xCenter > plot.Right) continue;

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
            double h = Math.Max(2, bot - top);
            var body = new Rect(xCenter - bodyW / 2, top, bodyW, h);

            ctx.FillRectangle(brush, body);
        }
    }

    private static void DrawText(DrawingContext ctx, string text, double x, double y, IBrush brush)
    {
        // FormattedText is the simplest way to draw text in Render()
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            brush
        );

        ctx.DrawText(ft, new Point(x, y));
    }
}