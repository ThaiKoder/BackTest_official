using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using DatasetTool;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection.Metadata.Ecma335;
using System.Threading;
using System.Threading.Tasks;

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

    // Prix: scale automatique (si tes prix sont en prix*1e9 => mets 1e9)
    private const double PriceScale = 1.0;

    // Bougies: largeur variable mais clampée
    private const double BodyMin = 3.0;
    private const double BodyMax = 250;

    // Ne doivent jamais se toucher => gap min entre deux centres
    private const double GapMinPx = 2.0;
    private const double GapMaxPx = 4.0;

    // Interaction state
    private bool _isPanning;
    private Point _lastPoint;

    // View state
    private double _centerTimeSec;         // epoch seconds
    private double _secondsPerPixel = 0.5; // zoom X (petit => zoom in)

    // Y autoscale visible
    private double _visibleMinPrice;
    private double _visibleMaxPrice;

    private double _centerPrice;
    private double _pricePerPixel;

    private bool _isZoomingY;
    private double _yZoomAnchorPrice;
    private double _yZoomAnchorT;

    // Données
    private readonly List<Candle> _candles = new();

    // Chargement async (évite relire dans Render)
    private CancellationTokenSource? _loadCts;
    private bool _loadedOnce;

    public CandleChartControl()
    {
        Focusable = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Charge une seule fois à l'attache
        if (_loadedOnce) return;
        _loadedOnce = true;

        _loadCts = new CancellationTokenSource();
        _ = LoadCandlesFromBinAsync(_loadCts.Token);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = null;
    }

    // =========================
    // Lecture BIN -> _candles
    // =========================

    private async Task LoadCandlesFromBinAsync(CancellationToken ct)
    {
        try
        {
            string inputDir = Path.Combine(AppContext.BaseDirectory, "data", "json");
            if (!Directory.Exists(inputDir))
            {
                Debug.WriteLine($"Dossier introuvable: {inputDir}");
                return;
            }

            string binDir = Path.Combine(inputDir, "..", "bin");
            if (!Directory.Exists(binDir))
            {
                Debug.WriteLine($"Dossier bin introuvable: {binDir}");
                return;
            }

            var binFiles = Directory.GetFiles(binDir, "*.bin");
            if (binFiles.Length == 0)
            {
                Debug.WriteLine("Aucun .bin trouvé.");
                return;
            }

            // Lecture sur thread pool (ne bloque pas l'UI)
            var sw = Stopwatch.StartNew();

            var list = await Task.Run(() =>
            {
                var tmp = new List<Candle>(capacity: 1024);
                long lastTs = 0;
                int nbCdl = 0;
                foreach (var binPath in binFiles)
                {
                    ct.ThrowIfCancellationRequested();

                    using var bin = new Binary(binPath);

                    // IMPORTANT: Ici symbol est un byte/int selon ton Binary.ReadAllFast
                    // Tu as un mapping quarterContracts dans ton code, à toi d'adapter.
                    // Je mets Symbol en string minimal ("SYM") si tu n'as pas le nom.
                    bin.ReadAllFast((ts, o, h, l, c, v, symbol) =>
                    {
                        // Option: si symbol est un index/byte, tu peux mapper vers string
                        // Exemple:
                        // string symStr = quarterContracts[symbol];
                        // tmp.Add(new Candle(ts, o, h, l, c, v, symStr));


                        //!!!!!!!
                        if (ts != lastTs && nbCdl < 15)
                        {
                            tmp.Add(new Candle(ts, o, h, l, c, v, symbol.ToString(CultureInfo.InvariantCulture)));
                            lastTs = ts;
                            nbCdl++;
                        }
                    });
                }

                return tmp;
            }, ct);

            sw.Stop();
            Debug.WriteLine($"Candles chargées: {list.Count} en {sw.ElapsedMilliseconds} ms");

            // Applique côté UI
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _candles.Clear();
                _candles.AddRange(list);

                InitViewFromCandles();
                InvalidateVisual();
            });
        }
        catch (OperationCanceledException)
        {
            // ok
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void InitViewFromCandles()
    {
        if (_candles.Count == 0) return;

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var plot = GetPlotRect(bounds);

        // Si on n'a pas encore de taille (premier attach), on met un fallback
        double plotW = plot.Width;
        double plotH = plot.Height;

        int n = 10;
        int start = _candles.Count - n;

        // ---- X: 60 dernières candles à l'écran ----
        // On ancre à la dernière bougie (bord droit visuel)
        var last = _candles[^1];
        double lastT = TsNsToEpochSeconds(last.TsNs);

        double dtSec = EstimateDtSeconds();              // intervalle entre candles
        double pitchPx = 10.0;                           // ~ pixels par candle (incluant gap)
        pitchPx = Clamp(pitchPx, BodyMin + GapMinPx, BodyMax + GapMaxPx);

        _secondsPerPixel = ((n - 1) * dtSec) / plotW;
        ClampZoomToGap();
        _centerTimeSec = lastT - (plotW * 0.5) * _secondsPerPixel;  
        double minP = double.PositiveInfinity;
        double maxP = double.NegativeInfinity;

        for (int i = start; i < _candles.Count; i++)
        {
            double l = _candles[i].L / PriceScale;
            double h = _candles[i].H / PriceScale;
            if (l < minP) minP = l;
            if (h > maxP) maxP = h;
        }

        if (!double.IsFinite(minP) || !double.IsFinite(maxP) || maxP <= minP)
        {
            minP = 0;
            maxP = 1;
        }

        double span = (maxP - minP);
        double padded = span * 1.10;                     // +10% de marge
        if (padded <= 0) padded = 1;

        _centerPrice = (minP + maxP) * 0.5;
        _pricePerPixel = padded / plotH;

        _visibleMinPrice = _centerPrice - (plotH * _pricePerPixel) * 0.5;
        _visibleMaxPrice = _centerPrice + (plotH * _pricePerPixel) * 0.5;
    }
    // =========================
    // Interaction
    // =========================

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var plot = GetPlotRect(bounds);

        var p = e.GetPosition(this);

        // Zone axe Y = tout ce qui est à gauche du plot
        bool inYAxis = p.X < plot.Left && p.Y >= plot.Top && p.Y <= plot.Bottom;

        _lastPoint = p;

        if (inYAxis)
        {
            _isZoomingY = true;
            _yZoomAnchorT = (plot.Bottom - p.Y) / plot.Height; // 0..1
            _yZoomAnchorPrice = YToPrice(p.Y, plot);

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        _isPanning = true;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        _isPanning = false;
        _isZoomingY = false;

        e.Pointer.Capture(null);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var plot = GetPlotRect(bounds);

        var p = e.GetPosition(this);
        var dx = p.X - _lastPoint.X;
        var dy = p.Y - _lastPoint.Y;
        _lastPoint = p;

        if (_isZoomingY)
        {
            double factor = Math.Exp(dy * 0.01);
            double newPPP = Clamp(_pricePerPixel * factor, 1e-9, 1e9);

            double newSpan = plot.Height * newPPP;
            _pricePerPixel = newPPP;

            // preserve anchor price
            _centerPrice = _yZoomAnchorPrice - (_yZoomAnchorT - 0.5) * newSpan;

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_isPanning)
        {
            _centerTimeSec -= dx * _secondsPerPixel;
            _centerPrice += dy * _pricePerPixel;

            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        if (_candles.Count == 0) return;

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var plot = GetPlotRect(bounds);
        if (plot.Width <= 0 || plot.Height <= 0) return;

        var mouse = e.GetPosition(this);
        var anchor = plot.Contains(mouse)
            ? mouse
            : new Point(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2);

        double t0 = ScreenXToWorldTime(anchor.X, plot);

        double factor = e.Delta.Y > 0 ? 1.10 : 1.0 / 1.10;
        _secondsPerPixel = Clamp(_secondsPerPixel / factor, 1e-6, 1e6);

        ClampZoomToGap();

        double t1 = ScreenXToWorldTime(anchor.X, plot);
        _centerTimeSec += (t0 - t1);

        InvalidateVisual();
        e.Handled = true;
    }

    // =========================
    // Render
    // =========================

    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var plot = GetPlotRect(bounds);
        if (plot.Width <= 0 || plot.Height <= 0) return;

        if (_candles.Count > 0 && (_secondsPerPixel <= 0 || _pricePerPixel <= 0))
        {
            InitViewFromCandles();
        }

        // background
        var bg = (IBrush?)Application.Current?.FindResource("Color.Background") ?? Brushes.Black;
        var axisBg = (IBrush?)Application.Current?.FindResource("Color.Background") ?? Brushes.Black;
        ctx.FillRectangle(bg, bounds);

        if (_candles.Count == 0)
            return;

        // largeur bougie
        double bodyW = ComputeBodyWidth(plot);

        // init Y si pas prêt
        if (_pricePerPixel <= 0)
        {
            double minP = double.PositiveInfinity, maxP = double.NegativeInfinity;
            for (int i = 0; i < _candles.Count; i++)
            {
                double l = _candles[i].L / PriceScale;
                double h = _candles[i].H / PriceScale;
                if (l < minP) minP = l;
                if (h > maxP) maxP = h;
            }
            if (!double.IsFinite(minP) || !double.IsFinite(maxP) || maxP <= minP) { minP = 0; maxP = 1; }

            _centerPrice = (minP + maxP) / 2.0;
            _pricePerPixel = ((maxP - minP) * 1.10) / plot.Height;
            _visibleMinPrice = minP;
            _visibleMaxPrice = maxP;
        }

        // range Y courant
        double visiblePriceRange = plot.Height * _pricePerPixel;
        _visibleMinPrice = _centerPrice - visiblePriceRange / 2.0;
        _visibleMaxPrice = _centerPrice + visiblePriceRange / 2.0;
        if (_visibleMaxPrice <= _visibleMinPrice) _visibleMaxPrice = _visibleMinPrice + 1e-9;

        // pens/brushes
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), 1);
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), 1);
        var labelBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD));
        var wickPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)), 1);
        var upBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0xC2, 0x7E));
        var dnBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x5A, 0x5A));

        // axes profile
        var axisProfile = AxisProfile.FromBodyWidth(bodyW);

        // BOUGIES -> CLIP AU PLOT
        using (ctx.PushClip(plot))
        {
            for (int i = 0; i < _candles.Count; i++)
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

        // FOND DES AXES (au-dessus des bougies)
        var leftAxisRect = new Rect(0, 0, plot.Left, bounds.Height);
        ctx.FillRectangle(axisBg, leftAxisRect);

        var bottomAxisRect = new Rect(0, plot.Bottom, bounds.Width, bounds.Height - plot.Bottom);
        ctx.FillRectangle(axisBg, bottomAxisRect);

        // axes lines
        ctx.DrawLine(axisPen, new Point(plot.Left, plot.Top), new Point(plot.Left, plot.Bottom));
        ctx.DrawLine(axisPen, new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        DrawYAxis(ctx, plot, gridPen, axisPen, labelBrush, axisProfile);
        DrawXAxis(ctx, plot, gridPen, axisPen, labelBrush, axisProfile);
    }

    // =========================
    // Zoom clamp
    // =========================

    private void ClampZoomToGap()
    {
        double dtSec = EstimateDtSeconds();

        double minPitch = BodyMin + GapMinPx;
        double maxPitch = BodyMax + GapMaxPx;

        double minSecondsPerPixel = dtSec / maxPitch;
        double maxSecondsPerPixel = dtSec / minPitch;

        _secondsPerPixel = Clamp(_secondsPerPixel, minSecondsPerPixel, maxSecondsPerPixel);
    }

    // =========================
    // Y helpers
    // =========================

    private double PriceToY(double price, Rect plot)
    {
        double t = (price - _visibleMinPrice) / (_visibleMaxPrice - _visibleMinPrice);
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

        double pxPerCandle = dtSec / _secondsPerPixel;

        double pxClamped = Clamp(pxPerCandle, GapMinPx + 1.0, GapMaxPx + BodyMax);

        double desired = pxClamped * 0.70;

        double maxAllowedByGap = Math.Max(1.0, pxClamped - GapMinPx);

        return Clamp(desired, BodyMin, Math.Min(BodyMax, maxAllowedByGap));
    }

    private double EstimateDtSeconds()
    {
        if (_candles.Count < 2) return 1.0;
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
            double t = (bodyW - BodyMin) / Math.Max(1e-9, (BodyMax - BodyMin));

            int yTicks = LerpInt(7, 4, t);
            int xTicks = LerpInt(7, 4, t);

            string timeFmt = t < 0.33 ? "HH:mm:ss" : "HH:mm";
            string priceFmt = t < 0.33 ? "0.#####"
                           : t < 0.66 ? "0.###"
                                      : "0.##";

            return new AxisProfile(yTicks, xTicks, timeFmt, priceFmt);
        }

        private static int LerpInt(int a, int b, double t)
            => (int)Math.Round(a + (b - a) * Clamp01(t));

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }

    private void DrawYAxis(DrawingContext ctx, Rect plot, Pen gridPen, Pen axisPen, IBrush labelBrush, AxisProfile p)
    {
        for (int i = 0; i <= p.YTicks; i++)
        {
            double tt = i / (double)p.YTicks;
            double y = plot.Bottom - tt * plot.Height;
            double price = YToPrice(y, plot);

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

    private static double TsNsToEpochSeconds(long tsNs) => tsNs / 1_000_000_000.0;

    private static double Clamp(double v, double min, double max)
        => v < min ? min : (v > max ? max : v);

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