using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace BacktestApp.Controls;

public sealed class CandleChartControl : Control
{
    public readonly record struct Candle(
        long TsNs,
        long O,
        long H,
        long L,
        long C,
        uint V,
        string Symbol
    );

    // ===>  Prix: scale automatique => on ne fait aucune conversion forcée.
    // Si tes prix sont en "prix * 1e9", mets PriceScale = 1e9.
    // Sinon laisse à 1.0. Tu peux aussi le détecter à l'import.
    private const double PriceScale = 1.0;

    // Bougies: largeur variable mais clampée
    private const double BodyMin = 3.0;
    private const double BodyMax = 250;

    // ===> ne doivent jamais se toucher => gap min entre deux centres
    private const double GapMinPx = 2.0;
    private const double GapMaxPx = 4.0;

    // Interaction state
    private bool _isPanning;
    private Point _lastPoint;

    // View state
    private double _centerTimeSec;      // epoch seconds
    private double _secondsPerPixel = 0.5; // zoom X (petit => zoom in)

    // Y autoscale visible
    private double _visibleMinPrice;
    private double _visibleMaxPrice;

    // Données test
    private readonly Candle[] _candles =
    [
        new Candle(TsNsFromUtc(2026,02,11,10,00,00), 100, 115,  95, 110, 10, "GOOG"),
        new Candle(TsNsFromUtc(2026,02,11,10,01,00), 110, 112,  98, 102, 12, "GOOG"),
        new Candle(TsNsFromUtc(2026,02,11,10,02,00), 102, 125, 101, 123, 20, "GOOG"),
    ];

    private void ClampZoomToGap()
    {
        double dtSec = EstimateDtSeconds();

        // pitch = distance en pixels entre deux bougies (centres)
        double minPitch = BodyMin + GapMinPx;      // évite qu'elles se touchent
        double maxPitch = BodyMax + GapMaxPx;      // évite trop d'espace

        // pitch = dtSec / secondsPerPixel
        // => secondsPerPixel = dtSec / pitch
        double minSecondsPerPixel = dtSec / maxPitch; // zoom-in limite (pitch trop grand)
        double maxSecondsPerPixel = dtSec / minPitch; // zoom-out limite (pitch trop petit)

        _secondsPerPixel = Clamp(_secondsPerPixel, minSecondsPerPixel, maxSecondsPerPixel);
    }

    public CandleChartControl()
    {
        Focusable = true;

        if (_candles.Length > 0)
        {
            var mid = _candles[_candles.Length / 2];
            _centerTimeSec = TsNsToEpochSeconds(mid.TsNs);
            _visibleMinPrice = _candles[0].L / PriceScale;
            _visibleMaxPrice = _candles[0].H / PriceScale;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isPanning = true;
            _lastPoint = e.GetPosition(this);
            e.Pointer.Capture(this);
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isPanning = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isPanning) return;

        var p = e.GetPosition(this);
        var dx = p.X - _lastPoint.X;
        _lastPoint = p;

        // Pan X seulement (Y est autoscale)
        _centerTimeSec -= dx * _secondsPerPixel;

        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (_candles.Length == 0) return;

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var plot = GetPlotRect(bounds);
        if (plot.Width <= 0 || plot.Height <= 0) return;

        var mouse = e.GetPosition(this);
        var anchor = plot.Contains(mouse)
            ? mouse
            : new Point(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2);

        // Monde sous curseur AVANT (temps seulement)
        double t0 = ScreenXToWorldTime(anchor.X, plot);

        double factor = e.Delta.Y > 0 ? 1.10 : 1.0 / 1.10;

        _secondsPerPixel = Clamp(_secondsPerPixel / factor, 1e-6, 1e6);

        // impose GapMax / GapMin
        ClampZoomToGap();

        // Monde sous curseur APRÈS
        double t1 = ScreenXToWorldTime(anchor.X, plot);

        // Re-anchor
        _centerTimeSec += (t0 - t1);

        InvalidateVisual();
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var plot = GetPlotRect(bounds);
        if (plot.Width <= 0 || plot.Height <= 0) return;
        if (_candles.Length == 0) return;

        // Brushes / pens
        var bg = new SolidColorBrush(Color.FromRgb(0x19, 0x19, 0x19));
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), 1);
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), 1);
        var labelBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
        var wickPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)), 1);
        var upBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xC2, 0x7E));
        var dnBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A));

        ctx.FillRectangle(bg, bounds);

        // 1) largeur de bougie dépend du zoom X, clamp + gap min
        double bodyW = ComputeBodyWidth(plot);

        // 2) autoscale Y sur bougies visibles (avec marge)
        // ✅ si aucune bougie visible, on garde une plage "fallback" pour continuer à dessiner axes + grille
        if (!ComputeVisiblePriceRange(plot, out _visibleMinPrice, out _visibleMaxPrice))
        {
            // fallback: utilise la dernière plage connue si valide, sinon une plage par défaut
            if (!double.IsFinite(_visibleMinPrice) || !double.IsFinite(_visibleMaxPrice) || _visibleMaxPrice <= _visibleMinPrice)
            {
                _visibleMinPrice = 0;
                _visibleMaxPrice = 1;
            }
            // et surtout: on NE return pas
        }

        // Axes
        ctx.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        ctx.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        // 3) Axes adaptatifs en fonction de bodyW
        var axisProfile = AxisProfile.FromBodyWidth(bodyW);

        DrawYAxis(ctx, plot, gridPen, axisPen, labelBrush, axisProfile);
        DrawXAxis(ctx, plot, gridPen, axisPen, labelBrush, axisProfile);




        // 4) candles
        for (int i = 0; i < _candles.Length; i++)
        {
            var c = _candles[i];
            double tSec = TsNsToEpochSeconds(c.TsNs);
            double xCenter = WorldTimeToScreenX(tSec, plot);

            if (xCenter < plot.Left - 100 || xCenter > plot.Right + 100)
                continue;

            double o = c.O / PriceScale;
            double h = c.H / PriceScale;
            double l = c.L / PriceScale;
            double cl = c.C / PriceScale;

            double yH = PriceToY(h, plot);
            double yL = PriceToY(l, plot);
            double yO = PriceToY(o, plot);
            double yC = PriceToY(cl, plot);

            // ===== CULLING STRICT =====
            double half = bodyW / 2.0;

            double bodyLeft = xCenter - half;
            double bodyRight = xCenter + half;

            double top = Math.Min(yO, yC);
            double bot = Math.Max(yO, yC);

            // Si le corps dépasse du plot → on ne dessine PAS la bougie
            if (bodyRight >= plot.Right || bodyLeft <= plot.Left)
                continue;

            if (bot <= plot.Top || top >= plot.Bottom)
                continue;

            bool up = cl >= o;
            var brush = up ? upBrush : dnBrush;

            ctx.DrawLine(wickPen, new Point(xCenter, yH), new Point(xCenter, yL));

            double height = Math.Max(2, bot - top);
            var body = new Rect(xCenter - bodyW / 2, top, bodyW, height);
            ctx.FillRectangle(brush, body);
        }

    }

    // =========================
    // Auto Y on visible candles
    // =========================

    private bool ComputeVisiblePriceRange(Rect plot, out double minP, out double maxP)
    {
        double leftTime = ScreenXToWorldTime(plot.Left, plot);
        double rightTime = ScreenXToWorldTime(plot.Right, plot);
        if (rightTime < leftTime) (leftTime, rightTime) = (rightTime, leftTime);

        minP = double.PositiveInfinity;
        maxP = double.NegativeInfinity;

        // scan simple; plus tard tu feras un index pour limiter à visible
        for (int i = 0; i < _candles.Length; i++)
        {
            var c = _candles[i];
            double t = TsNsToEpochSeconds(c.TsNs);
            if (t < leftTime || t > rightTime) continue;

            double low = c.L / PriceScale;
            double high = c.H / PriceScale;

            if (low < minP) minP = low;
            if (high > maxP) maxP = high;
        }

        if (!double.IsFinite(minP) || !double.IsFinite(maxP) || maxP <= minP)
            return false;

        // marge 5%
        double span = maxP - minP;
        minP -= span * 0.05;
        maxP += span * 0.05;

        return true;
    }

    private double PriceToY(double price, Rect plot)
    {
        double span = Math.Max(1e-12, _visibleMaxPrice - _visibleMinPrice);
        double t = (price - _visibleMinPrice) / span; // 0..1
        return plot.Bottom - t * plot.Height;
    }

    private double YToPrice(double y, Rect plot)
    {
        double span = Math.Max(1e-12, _visibleMaxPrice - _visibleMinPrice);
        double t = (plot.Bottom - y) / plot.Height;
        return _visibleMinPrice + t * span;
    }

    // =========================
    // X mapping (zoom / pan)
    // =========================

    private double WorldTimeToScreenX(double timeSec, Rect plot)
    {
        double dxSec = timeSec - _centerTimeSec;
        double dxPx = dxSec / _secondsPerPixel;
        return (plot.Left + plot.Width / 2) + dxPx;
    }

    private double ScreenXToWorldTime(double x, Rect plot)
    {
        double dxPx = x - (plot.Left + plot.Width / 2);
        return _centerTimeSec + dxPx * _secondsPerPixel;
    }

    // =========================
    // Candle width (no-touch)
    // =========================

    private double ComputeBodyWidth(Rect plot)
    {
        double dtSec = EstimateDtSeconds();

        // pixels entre deux timestamps consécutifs
        double pxPerCandle = dtSec / _secondsPerPixel;

        // clamp du gap
        double pxClamped = Clamp(pxPerCandle, GapMinPx + 1.0, GapMaxPx + BodyMax);

        // largeur désirée = 70% de l’espace
        double desired = pxClamped * 0.70;

        // largeur max autorisée en respectant gap min
        double maxAllowedByGap = Math.Max(1.0, pxClamped - GapMinPx);

        return Clamp(desired, BodyMin, Math.Min(BodyMax, maxAllowedByGap));
    }

    private double EstimateDtSeconds()
    {
        if (_candles.Length < 2) return 1.0;
        // dt médian simple sur petit sample
        long a = _candles[0].TsNs;
        long b = _candles[1].TsNs;
        double dt = Math.Abs(TsNsToEpochSeconds(b) - TsNsToEpochSeconds(a));
        return Math.Max(1e-6, dt);
    }

    // =========================
    // Axes adaptatifs
    // =========================

    private readonly record struct AxisProfile(int YTicks, int XTicks, string TimeFormat, string PriceFormat)
    {
        public static AxisProfile FromBodyWidth(double bodyW)
        {
            // bodyW ~ min => zoom out (bcp de candles) => on veut + précis, + ticks
            // bodyW ~ max => zoom in (peu de candles) => axes plus "larges", moins chargés
            double t = (bodyW - BodyMin) / Math.Max(1e-9, (BodyMax - BodyMin)); // 0..1

            int yTicks = LerpInt(7, 4, t); // min width => 7 ticks, max width => 4
            int xTicks = LerpInt(7, 4, t);

            // format temps: plus précis quand bodyW est petit
            string timeFmt = t < 0.33 ? "HH:mm:ss" : (t < 0.66 ? "HH:mm" : "HH:mm");

            // prix: plus précis quand bodyW est petit
            string priceFmt = t < 0.33 ? "0.#####"
                           : t < 0.66 ? "0.###"
                                      : "0.##";

            return new AxisProfile(yTicks, xTicks, timeFmt, priceFmt);
        }

        private static int LerpInt(int a, int b, double t)
            => (int)Math.Round(a + (b - a) * Clamp01(t));
    }

    private void DrawYAxis(DrawingContext ctx, Rect plot, Pen gridPen, Pen axisPen, IBrush labelBrush, AxisProfile p)
    {
        for (int i = 0; i <= p.YTicks; i++)
        {
            double tt = i / (double)p.YTicks;
            double y = plot.Bottom - tt * plot.Height;
            double price = YToPrice(y, plot);

            ctx.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            ctx.DrawLine(axisPen, new Point(plot.Left - 4, y), new Point(plot.Left, y));

            DrawText(ctx, price.ToString(p.PriceFormat, CultureInfo.InvariantCulture), 6, y - 8, labelBrush);
        }
    }

    private void DrawXAxis(DrawingContext ctx, Rect plot, Pen gridPen, Pen axisPen, IBrush labelBrush, AxisProfile p)
    {
        for (int i = 0; i <= p.XTicks; i++)
        {
            double tt = i / (double)p.XTicks;
            double x = plot.Left + tt * plot.Width;

            double timeSec = ScreenXToWorldTime(x, plot);
            var dt = DateTimeOffset.FromUnixTimeSeconds((long)timeSec).UtcDateTime;

            ctx.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
            ctx.DrawLine(axisPen, new Point(x, plot.Bottom), new Point(x, plot.Bottom + 4));

            DrawText(ctx, dt.ToString(p.TimeFormat, CultureInfo.InvariantCulture), x - 28, plot.Bottom + 6, labelBrush);
        }
    }

    // =========================
    // Layout + misc
    // =========================

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

    private static long TsNsFromUtc(int y, int mo, int d, int h, int mi, int s)
    {
        var dto = new DateTimeOffset(y, mo, d, h, mi, s, TimeSpan.Zero);
        long ms = dto.ToUnixTimeMilliseconds();
        return ms * 1_000_000L; // ms -> ns
    }

    private static double TsNsToEpochSeconds(long tsNs) => tsNs / 1_000_000_000.0;

    private static double Clamp(double v, double min, double max)
        => v < min ? min : (v > max ? max : v);

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

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
}