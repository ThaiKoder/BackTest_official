//rendu uniquement

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Globalization;


namespace BacktestApp.Controls;


public sealed partial class CandleChartControl
{
    private static readonly IBrush BgBrush =
        (IBrush?)Application.Current?.FindResource("Color.Background") ?? Brushes.Black;

    private static readonly IBrush AxisBgBrush =
        (IBrush?)Application.Current?.FindResource("Color.Background") ?? Brushes.Black;

    private static readonly Pen AxisPen =
        new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), 1);

    private static readonly IBrush LabelBrush =
        new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));

    private static readonly Pen WickPen =
        new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)), 1);

    private static readonly IBrush UpBrush =
        new SolidColorBrush(Color.FromRgb(0x2E, 0xC2, 0x7E));

    private static readonly IBrush DownBrush =
        new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A));

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var plot = GetPlotRect(bounds);
        if (plot.Width <= 0 || plot.Height <= 0) return;

        var BgBrush = (IBrush?)Application.Current?.FindResource("Color.Background") ?? Brushes.Black;
        var AxisBgBrush = (IBrush?)Application.Current?.FindResource("Color.Background") ?? Brushes.Black;
        ctx.FillRectangle(BgBrush, bounds);

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
                var brush = up ? UpBrush : DownBrush;

                ctx.DrawLine(WickPen, new Point(xCenter, yH), new Point(xCenter, yL));

                double top = Math.Min(yO, yC);
                double bot = Math.Max(yO, yC);

                double height = Math.Max(2, bot - top);
                var body = new Rect(xCenter - bodyW / 2, top, bodyW, height);
                ctx.FillRectangle(brush, body);
            }
        }

        var leftAxisRect = new Rect(0, 0, plot.Left, bounds.Height);
        ctx.FillRectangle(AxisBgBrush, leftAxisRect);

        var bottomAxisRect = new Rect(0, plot.Bottom, bounds.Width, bounds.Height - plot.Bottom);
        ctx.FillRectangle(AxisBgBrush, bottomAxisRect);

        ctx.DrawLine(AxisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        ctx.DrawLine(AxisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        DrawYAxisSimple(ctx, plot, AxisPen, LabelBrush);
        DrawXAxisSimple(ctx, plot, AxisPen, LabelBrush);
    }


    private void DrawYAxisSimple(DrawingContext ctx, Rect plot, Pen AxisPen, IBrush LabelBrush)
    {
        int ticks = 6;
        for (int i = 0; i <= ticks; i++)
        {
            double tt = i / (double)ticks;
            double y = plot.Bottom - tt * plot.Height;
            double price = YToPrice(y, plot);

            ctx.DrawLine(AxisPen, new Point(plot.Left - 4, y), new Point(plot.Left, y));
            DrawText(ctx, price.ToString("0.###", CultureInfo.InvariantCulture), 6, y - 8, LabelBrush);
        }
    }


    private void DrawXAxisSimple(DrawingContext ctx, Rect plot, Pen AxisPen, IBrush LabelBrush)
    {
        int ticks = 6;
        for (int i = 0; i <= ticks; i++)
        {
            double tt = i / (double)ticks;
            double x = plot.Left + tt * plot.Width;

            double timeSec = ScreenXToWorldTime(x, plot);
            var dt = DateTimeOffset.FromUnixTimeSeconds((long)timeSec).UtcDateTime;

            ctx.DrawLine(AxisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));
            DrawText(ctx, dt.ToString("HH:mm", CultureInfo.InvariantCulture), x - 22, plot.Bottom + 6, LabelBrush);
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