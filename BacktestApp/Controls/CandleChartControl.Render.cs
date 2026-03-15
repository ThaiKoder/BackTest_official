// rendu uniquement

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

        var bgBrush = (IBrush?)Application.Current?.FindResource("Color.Background") ?? Brushes.Black;
        var axisBgBrush = (IBrush?)Application.Current?.FindResource("Color.Background") ?? Brushes.Black;
        ctx.FillRectangle(bgBrush, bounds);

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
        if (_visibleMaxPrice <= _visibleMinPrice)
            _visibleMaxPrice = _visibleMinPrice + 1e-9;

        double bodyW = ComputeBodyWidthWindow();

        using (ctx.PushClip(plot))
        {
            for (int i = 0; i < _windowLoaded; i++)
            {
                double tSec = TsNsToEpochSeconds(GetTs(i));
                double xCenter = WorldTimeToScreenX(tSec, plot);

                if (xCenter < plot.Left - 100 || xCenter > plot.Right + 100)
                    continue;

                double o = GetO(i) / PriceScale;
                double h = GetH(i) / PriceScale;
                double l = GetL(i) / PriceScale;
                double cl = GetC(i) / PriceScale;

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

        // Mise à jour des axes dans des buffers fixes
        UpdateYAxisTicks(plot);
        UpdateXAxisTicks(plot);

        var leftAxisRect = new Rect(0, 0, plot.Left, bounds.Height);
        ctx.FillRectangle(axisBgBrush, leftAxisRect);

        var bottomAxisRect = new Rect(0, plot.Bottom, bounds.Width, bounds.Height - plot.Bottom);
        ctx.FillRectangle(axisBgBrush, bottomAxisRect);

        ctx.DrawLine(AxisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        ctx.DrawLine(AxisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        DrawYAxisSimple(ctx, plot, AxisPen, LabelBrush);
        DrawXAxisSimple(ctx, plot, AxisPen, LabelBrush);
    }

    private int GetXAxisStepSeconds()
    {
        const double MinTickSpacingPx = 90.0;

        for (int i = 0; i < XAxisStepsSec.Length; i++)
        {
            double px = XAxisStepsSec[i] / _secondsPerPixel;
            if (px >= MinTickSpacingPx)
                return XAxisStepsSec[i];
        }

        return XAxisStepsSec[XAxisStepsSec.Length - 1];
    }

    private static double AlignTimeDown(double timeSec, int stepSec)
    {
        return Math.Floor(timeSec / stepSec) * stepSec;
    }

    private void UpdateXAxisTicks(Rect plot)
    {
        _xTickCount = 0;
        _xTickStepSec = GetXAxisStepSeconds();

        double leftTime = ScreenXToWorldTime(plot.Left, plot);
        double rightTime = ScreenXToWorldTime(plot.Right, plot);

        double t = AlignTimeDown(leftTime, _xTickStepSec);

        while (t <= rightTime + _xTickStepSec && _xTickCount < MaxAxisTicks)
        {
            double x = WorldTimeToScreenX(t, plot);

            if (x >= plot.Left && x <= plot.Right)
            {
                _xTickTimes[_xTickCount] = t;
                _xTickPixels[_xTickCount] = x;
                _xTickCount++;
            }

            t += _xTickStepSec;
        }
    }

    private void UpdateYAxisTicks(Rect plot)
    {
        _yTickCount = 0;

        const int ticks = 6;
        for (int i = 0; i <= ticks && _yTickCount < MaxAxisTicks; i++)
        {
            double tt = i / (double)ticks;
            double y = plot.Bottom - tt * plot.Height;
            double price = YToPrice(y, plot);

            _yTickPixels[_yTickCount] = y;
            _yTickPrices[_yTickCount] = price;
            _yTickCount++;
        }
    }

    private static string FormatXAxisLabel(double timeSec, int stepSec)
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds((long)timeSec).UtcDateTime;

        if (stepSec < 3600)
            return dt.ToString("HH:mm", CultureInfo.InvariantCulture);

        if (stepSec < 24 * 3600)
            return dt.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);

        return dt.ToString("dd/MM", CultureInfo.InvariantCulture);
    }

    private void DrawYAxisSimple(DrawingContext ctx, Rect plot, Pen axisPen, IBrush labelBrush)
    {
        for (int i = 0; i < _yTickCount; i++)
        {
            double y = _yTickPixels[i];
            double price = _yTickPrices[i];

            ctx.DrawLine(axisPen, new Point(plot.Left - 4, y), new Point(plot.Left, y));
            DrawText(ctx, price.ToString("0.###", CultureInfo.InvariantCulture), 6, y - 8, labelBrush);
        }
    }

    private void DrawXAxisSimple(DrawingContext ctx, Rect plot, Pen axisPen, IBrush labelBrush)
    {
        for (int i = 0; i < _xTickCount; i++)
        {
            double x = _xTickPixels[i];
            double timeSec = _xTickTimes[i];

            ctx.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));

            string label = FormatXAxisLabel(timeSec, _xTickStepSec);
            DrawText(ctx, label, x - 22, plot.Bottom + 6, labelBrush);
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