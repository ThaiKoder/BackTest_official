// rendu uniquement

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BacktestApp.Indicators;
using System;
using System.Diagnostics;
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

    private static readonly double[] NicePriceSteps =
    {
        0.0001, 0.0002, 0.0005,
        0.001,  0.002,  0.005,
        0.01,   0.02,   0.05,
        0.1,    0.2,    0.5,
        1.0,    2.0,    5.0,
        10.0,   20.0,   50.0,
        100.0,  200.0,  500.0,
        1000.0, 2000.0, 5000.0
    };

    private static readonly Pen SessionPen =
        new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), 2);

    private static readonly IBrush SessionBrush =
        new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));

    private void DrawSessionZone(
        DrawingContext ctx,
        Rect plot,
        SessionHighLowIndicator.Output? output,
        SessionZoneDefinition definition)
    {
        if (output is null || !output.HasLast)
            return;

        double x1 = WorldTimeToScreenX(
            TsNsToEpochSeconds(output.LastStartTs),
            plot);

        double x2 = WorldTimeToScreenX(
            TsNsToEpochSeconds(output.LastEndTs),
            plot);

        double yHigh = PriceToY(output.LastHigh, plot);
        double yLow = PriceToY(output.LastLow, plot);

        double left = Math.Min(x1, x2);
        double width = Math.Abs(x2 - x1);

        double top = Math.Min(yHigh, yLow);
        double height = Math.Abs(yLow - yHigh);

        if (width <= 0 || height <= 0)
            return;


        var rect = new Rect(left, top, width, height);
        ctx.DrawRectangle(definition.Fill, definition.Border, rect);

        // =========================
        // Texte sous le LOW
        // =========================

        string label = output.Name;
        var textBrush = definition.Border.Brush;

        var ft = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            textBrush);

        double textX = left + width * 0.5 - ft.Width / 2;
        double textY = yLow + 4; // sous le low

        ctx.DrawText(ft, new Point(textX, textY));
    }


    private void DrawAllSessionZones(DrawingContext ctx, Rect plot)
    {
        int count = Math.Min(_sessionZoneDefinitions.Count, _sessionOutputs.Count);

        for (int i = 0; i < count; i++)
        {
            DrawSessionZone(
                ctx,
                plot,
                _sessionOutputs[i],
                _sessionZoneDefinitions[i]);
        }
    }

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

        DrawAllSessionZones(ctx, plot);
        DebugKillZones();

        // Mise à jour des axes dans des buffers fixes
        UpdateYAxisTicks(plot);
        UpdateXAxisTicks(plot);

        var leftAxisRect = new Rect(0, 0, plot.Left, bounds.Height);
        ctx.FillRectangle(AxisBgBrush, leftAxisRect);

        var bottomAxisRect = new Rect(0, plot.Bottom, bounds.Width, bounds.Height - plot.Bottom);
        ctx.FillRectangle(AxisBgBrush, bottomAxisRect);

        ctx.DrawLine(AxisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        ctx.DrawLine(AxisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        using (ctx.PushClip(leftAxisRect))
        {
            DrawYAxisSimple(ctx, plot, AxisPen, LabelBrush);
        }

        using (ctx.PushClip(bottomAxisRect))
        {
            DrawXAxisSimple(ctx, plot, AxisPen, LabelBrush);
        }
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

            string label = FormatYAxisLabel(price, _yTickStepPrice);

            var ft = new FormattedText(
                label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                12,
                labelBrush);

            // aligné dans la marge de gauche
            double textX = 4;
            double textY = y - ft.Height / 2.0;

            ctx.DrawText(ft, new Point(textX, textY));
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

    private double GetYAxisStepPrice(Rect plot)
    {
        const int TargetTickCount = 8;

        double visibleRange = plot.Height * _pricePerPixel;
        if (visibleRange <= 0 || !double.IsFinite(visibleRange))
            return 1.0;

        double rawStep = visibleRange / TargetTickCount;
        return GetNiceStep(rawStep);
    }
    private static double AlignPriceDown(double price, double step)
    {
        return Math.Floor(price / step) * step;
    }

    private void UpdateYAxisTicks(Rect plot)
    {
        _yTickCount = 0;
        _yTickStepPrice = GetYAxisStepPrice(plot);

        double minPrice = _centerPrice - (plot.Height * _pricePerPixel) / 2.0;
        double maxPrice = _centerPrice + (plot.Height * _pricePerPixel) / 2.0;

        double p = AlignPriceDown(minPrice, _yTickStepPrice);

        while (p <= maxPrice + _yTickStepPrice && _yTickCount < MaxAxisTicks)
        {
            double y = PriceToY(p, plot);

            if (y >= plot.Top && y <= plot.Bottom)
            {
                _yTickPrices[_yTickCount] = p;
                _yTickPixels[_yTickCount] = y;
                _yTickCount++;
            }

            p += _yTickStepPrice;
        }
    }
    private static string FormatYAxisLabel(double price, double step)
    {
        int stepUnit = 50000000;
        long raw = (long)Math.Round(price * PriceScale);
        string s = raw.ToString(CultureInfo.InvariantCulture);

        if (s.Length <= 4)
            return s;

        string leftL = s.Substring(0, s.Length - (s.Length - 6));
        string leftM = s.Substring(0, s.Length - (s.Length - 4));
        string leftS = s.Substring(0, s.Length - (s.Length - 2));
        string leftXS = s.Substring(0, s.Length - (s.Length - 1));

        string rightL = s.Substring(s.Length - 2);
        string rightM = s.Substring(s.Length - 4);
        string rightS = s.Substring(s.Length - 6);
        string rightXS = s.Substring(s.Length - 8);

        //DebugMessage.Write($"{step}");

        // zoom très large
        if (step >= 2000000000) return $"{leftL}";


        // zoom très medium
        if (step >= 1000000000) return $"{leftM} -- {rightM.Substring(0, 2)}";

        // zoom très small
        if (step >= 200000000) return $"{leftS} -- {rightS.Substring(0, 2)}";

        //// zoom moyen → 2 chiffres de précision
        //if (step >= 1)
        //    return $"{leftL} -- {right.Substring(0, 2)}";

        //// zoom proche → précision complète
        return $"{leftXS} -- {rightXS}";
    }


    private static double GetNiceStep(double rawStep)
    {
        if (rawStep <= 0 || !double.IsFinite(rawStep))
            return 1.0;

        double exponent = Math.Floor(Math.Log10(rawStep));
        double magnitude = Math.Pow(10.0, exponent);
        double normalized = rawStep / magnitude;

        double niceNormalized;
        if (normalized <= 1.0) niceNormalized = 1.0;
        else if (normalized <= 2.0) niceNormalized = 2.0;
        else if (normalized <= 5.0) niceNormalized = 5.0;
        else niceNormalized = 10.0;

        return niceNormalized * magnitude;
    }
}