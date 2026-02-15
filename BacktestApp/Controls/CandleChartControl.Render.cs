//rendu uniquement

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Globalization;


namespace BacktestApp.Controls;


public sealed partial class CandleChartControl
{
    // (Recommandé perf) : mettre ces champs ailleurs (fichier 1 ou ici)
    // private static readonly Pen AxisPen = ...
    // etc.

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var plot = GetPlotRect(bounds);
        if (plot.Width <= 0 || plot.Height <= 0) return;

        var bg = (IBrush?)Application.Current?.FindResource("Color.Background") ?? Brushes.Black;
        var axisBg = (IBrush?)Application.Current?.FindResource("Color.Background") ?? Brushes.Black;
        ctx.FillRectangle(bg, bounds);

        if (_windowLoaded <= 0) return;

        if (!_xInited)
        {
            InitViewXFromWindow(plot);
            _xInited = true;
        }
        if (!_yInited)
        {
            AutoScaleYFromWindow(plot);
            _yInited = true;
        }

        ClampCenterTimeToWindow(plot);

        double visiblePriceRange = plot.Height * _pricePerPixel;
        _visibleMinPrice = _centerPrice - visiblePriceRange / 2.0;
        _visibleMaxPrice = _centerPrice + visiblePriceRange / 2.0;
        if (_visibleMaxPrice <= _visibleMinPrice) _visibleMaxPrice = _visibleMinPrice + 1e-9;

        double bodyW = ComputeBodyWidthWindow();

        // ⚠️ Perf: à remplacer par des champs cached (voir plus bas)
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), 1);
        var labelBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
        var wickPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)), 1);
        var upBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xC2, 0x7E));
        var dnBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A));

        using (ctx.PushClip(plot))
        {
            for (int i = 0; i < _windowLoaded; i++)
            {
                double tSec = TsNsToEpochSeconds(_ts[i]);
                double xCenter = WorldTimeToScreenX(tSec, plot);

                if (xCenter < plot.Left - 100 || xCenter > plot.Right + 100)
                    continue;

                double o = _o[i] / PriceScale;
                double h = _h[i] / PriceScale;
                double l = _l[i] / PriceScale;
                double cl = _c[i] / PriceScale;

                double yH = PriceToY(h, plot);
                double yL = PriceToY(l, plot);
                double yO = PriceToY(o, plot);
                double yC = PriceToY(cl, plot);

                bool up = cl >= o;
                var brush = up ? upBrush : dnBrush;

                ctx.DrawLine(wickPen, new Point(xCenter, yH), new Point(xCenter, yL));

                double top = Math.Min(yO, yC);
                double bot = Math.Max(yO, yC);

                double height = Math.Max(2, bot - top);
                var body = new Rect(xCenter - bodyW / 2, top, bodyW, height);
                ctx.FillRectangle(brush, body);
            }
        }

        var leftAxisRect = new Rect(0, 0, plot.Left, bounds.Height);
        ctx.FillRectangle(axisBg, leftAxisRect);

        var bottomAxisRect = new Rect(0, plot.Bottom, bounds.Width, bounds.Height - plot.Bottom);
        ctx.FillRectangle(axisBg, bottomAxisRect);

        ctx.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        ctx.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        DrawYAxisSimple(ctx, plot, axisPen, labelBrush);
        DrawXAxisSimple(ctx, plot, axisPen, labelBrush);
    }


    private void DrawYAxisSimple(DrawingContext ctx, Rect plot, Pen axisPen, IBrush labelBrush)
    {
        int ticks = 6;
        for (int i = 0; i <= ticks; i++)
        {
            double tt = i / (double)ticks;
            double y = plot.Bottom - tt * plot.Height;
            double price = YToPrice(y, plot);

            ctx.DrawLine(axisPen, new Point(plot.Left - 4, y), new Point(plot.Left, y));
            DrawText(ctx, price.ToString("0.###", CultureInfo.InvariantCulture), 6, y - 8, labelBrush);
        }
    }


    private void DrawXAxisSimple(DrawingContext ctx, Rect plot, Pen axisPen, IBrush labelBrush)
    {
        int ticks = 6;
        for (int i = 0; i <= ticks; i++)
        {
            double tt = i / (double)ticks;
            double x = plot.Left + tt * plot.Width;

            double timeSec = ScreenXToWorldTime(x, plot);
            var dt = DateTimeOffset.FromUnixTimeSeconds((long)timeSec).UtcDateTime;

            ctx.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            DrawText(ctx, dt.ToString("HH:mm", CultureInfo.InvariantCulture), x - 22, plot.Bottom + 6, labelBrush);
        }
    }


    private static void DrawText(DrawingContext ctx, string text, double x, double y, IBrush brush)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            brush);

        ctx.DrawText(ft, new Point(x, y));
    }


    private static Rect GetPlotRect(Rect bounds)
    {
        double leftAxisW = 70;
        double bottomAxisH = 28;
        double pad = 10;

        return new Rect(
            x: leftAxisW + pad,
            y: pad,
            width: Math.Max(0, bounds.Width - (leftAxisW + pad) - pad),
            height: Math.Max(0, bounds.Height - bottomAxisH - pad - pad)
        );
    }
}