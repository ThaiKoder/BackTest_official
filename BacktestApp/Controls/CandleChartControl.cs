using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace BacktestApp.Controls;

/// <summary>
/// LEVEL 3 (corrigé):
/// - MMAP
/// - Fenêtre glissante (WindowCount)
/// - Pan/Zoom
/// - Reload automatique aux bords (basé sur index visible)
/// - Clamp du centre après reload pour éviter "centre dans le futur" => plus de bougies visibles
///
/// </summary>
public sealed class CandleChartControl : Control
{
    // =========================
    // MMAP file reader
    // =========================
    public sealed class MmapCandleFile : IDisposable
    {
        // >>> FORMAT 45 bytes (ts,o,h,l,c,v + 1 byte symbol)
        public const int SymbolSize = 1;
        public const int CandleSize = 45;

        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _acc;

        public long Count { get; }

        public MmapCandleFile(string path)
        {
            var fi = new FileInfo(path);
            long byteLen = fi.Length;

            if (byteLen <= 0 || (byteLen % CandleSize) != 0)
                Debug.WriteLine($"[MmapCandleFile] WARNING: file size {byteLen} not multiple of {CandleSize}");

            Count = byteLen / CandleSize;

            _mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
            _acc = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        }

        public bool ReadAt(
            long index,
            out long ts, out long o, out long h, out long l, out long c, out uint v,
            Span<byte> sym)
        {
            ts = o = h = l = c = 0;
            v = 0;

            if ((ulong)index >= (ulong)Count) return false;
            if (sym.Length < SymbolSize) throw new ArgumentException("sym must be length >= SymbolSize");

            long off = index * CandleSize;

            ts = _acc.ReadInt64(off + 0);
            o = _acc.ReadInt64(off + 8);
            h = _acc.ReadInt64(off + 16);
            l = _acc.ReadInt64(off + 24);
            c = _acc.ReadInt64(off + 32);
            v = _acc.ReadUInt32(off + 40);

            // 1 byte symbol (offset 44)
            sym[0] = _acc.ReadByte(off + 44);

            return true;
        }

        public void Dispose()
        {
            _acc.Dispose();
            _mmf.Dispose();
        }
    }

    // =========================
    // Params rendu
    // =========================
    private const double PriceScale = 1.0;

    private const double BodyMin = 3.0;
    private const double BodyMax = 250;

    private const double GapMinPx = 2.0;
    private const double GapMaxPx = 4.0;

    private const int VisibleCount = 10;
    private const int WindowCount = 40;

    // =========================
    // Interaction state
    // =========================
    private bool _isPanning;
    private bool _isZoomingY;
    private Point _lastPoint;
    private double _yZoomAnchorPrice;
    private double _yZoomAnchorT;

    // =========================
    // View state
    // =========================
    private double _centerTimeSec;         // epoch seconds
    private double _secondsPerPixel = 0.5; // zoom X
    private double _centerPrice;
    private double _pricePerPixel;
    private double _visibleMinPrice;
    private double _visibleMaxPrice;

    // =========================
    // Window data (zéro alloc)
    // =========================
    private readonly long[] _ts = new long[WindowCount];
    private readonly long[] _o = new long[WindowCount];
    private readonly long[] _h = new long[WindowCount];
    private readonly long[] _l = new long[WindowCount];
    private readonly long[] _c = new long[WindowCount];
    private readonly uint[] _v = new uint[WindowCount];
    private readonly byte[] _sym = new byte[WindowCount * MmapCandleFile.SymbolSize];

    private MmapCandleFile? _file;
    private long _fileCount;

    private long _windowStart; // index global (dans le fichier) du début de lecture brute
    private int _windowLoaded;

    private bool _loadedOnce;
    private bool _xInited;
    private bool _yInited;

    private bool _reloadInProgress;
    private long _lastReloadStart = -1;

    private DispatcherTimer? _edgeTimer;

    public CandleChartControl()
    {
        Focusable = true;
    }

    // =========================
    // Attach / detach
    // =========================
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_loadedOnce) return;
        _loadedOnce = true;

        // ⚠️ adapte ton chemin si besoin
        string inputDir = Path.Combine(AppContext.BaseDirectory, "data", "json");
        string binDir = Path.Combine(inputDir, "..", "bin");
        var bins = Directory.GetFiles(binDir, "*.bin");
        Array.Sort(bins, StringComparer.OrdinalIgnoreCase);

        if (bins.Length == 0)
        {
            Debug.WriteLine("[CandleChartControl] Aucun .bin trouvé");
            return;
        }

        _file?.Dispose();
        _file = new MmapCandleFile(bins[^1]);
        _fileCount = _file.Count;

        // charge vers la fin (on lit un peu avant car on filtre)
        long start = Math.Max(0, _fileCount - 5000);
        LoadWindow(start);

        _xInited = false;
        _yInited = false;

        // Timer "edge check" (reload même si on s'arrête au bord)
        _edgeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };

        _edgeTimer.Tick += (_, __) =>
        {
            if (!IsVisible) return;
            if (_windowLoaded <= 0) return;

            var plot = GetPlotRect(new Rect(0, 0, Bounds.Width, Bounds.Height));
            if (plot.Width <= 0 || plot.Height <= 0) return;

            EnsureWindowAroundView(plot);
        };

        _edgeTimer.Start();
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _edgeTimer?.Stop();
        _edgeTimer = null;

        _file?.Dispose();
        _file = null;
    }

    // =========================
    // Validation (filtre anti garbage)
    // =========================
    private static bool IsValidRecord(long ts, long o, long h, long l, long c)
    {
        // ts en ns depuis epoch (1970) : 2000..2100 large
        const long MinTs = 946684800L * 1_000_000_000L;
        const long MaxTs = 4102444800L * 1_000_000_000L;

        if (ts < MinTs || ts > MaxTs) return false;

        if (o <= 0 || h <= 0 || l <= 0 || c <= 0) return false;
        if (h < l) return false;

        const long MaxReasonable = 10_000_000_000_000L; // 1e13
        if (o > MaxReasonable || h > MaxReasonable || l > MaxReasonable || c > MaxReasonable) return false;

        return true;
    }

    // =========================
    // Window loading (filtre invalides)
    // =========================
    private void LoadWindow(long startIndex)
    {
        if (_file is null) return;

        long idx = startIndex;
        int filled = 0;

        while (filled < WindowCount && idx < _fileCount)
        {
            Span<byte> sym = _sym.AsSpan(filled * MmapCandleFile.SymbolSize, MmapCandleFile.SymbolSize);

            if (!_file.ReadAt(idx, out var ts, out var o, out var h, out var l, out var c, out var v, sym))
                break;

            if (IsValidRecord(ts, o, h, l, c))
            {
                _ts[filled] = ts;
                _o[filled] = o;
                _h[filled] = h;
                _l[filled] = l;
                _c[filled] = c;
                _v[filled] = v;
                filled++;
            }

            idx++;
        }

        _windowLoaded = filled;
        _windowStart = startIndex;

        Debug.WriteLine($"windowLoaded={_windowLoaded} windowStart={_windowStart}");
        for (int i = 0; i < Math.Min(_windowLoaded, 10); i++)
            Debug.WriteLine($"i={i} ts={_ts[i]} o={_o[i]}");
    }

    // =========================
    // Clamp centre X pour éviter "centre dans le futur"
    // =========================
    private void ClampCenterTimeToWindow(Rect plot)
    {
        if (_windowLoaded <= 0) return;

        double firstT = TsNsToEpochSeconds(_ts[0]);
        double lastT = TsNsToEpochSeconds(_ts[_windowLoaded - 1]);

        double halfSpan = (plot.Width * 0.5) * _secondsPerPixel;

        double minCenter = firstT + halfSpan;
        double maxCenter = lastT - halfSpan;

        if (maxCenter < minCenter)
        {
            _centerTimeSec = 0.5 * (firstT + lastT);
            return;
        }

        if (_centerTimeSec < minCenter) _centerTimeSec = minCenter;
        if (_centerTimeSec > maxCenter) _centerTimeSec = maxCenter;
    }

    // =========================
    // Reload window (sans sauts) + clamp centre
    // =========================
    private void ReloadWindow(long newStart)
    {
        if (_file is null) return;
        if (_reloadInProgress) return;

        newStart = ClampLong(newStart, 0, Math.Max(0, _fileCount - 1));
        if (newStart == _windowStart) return;
        if (newStart == _lastReloadStart) return;
        _lastReloadStart = newStart;

        _reloadInProgress = true;

        double keepCenterTime = _centerTimeSec;
        double keepSecondsPerPixel = _secondsPerPixel;
        double keepCenterPrice = _centerPrice;
        double keepPricePerPixel = _pricePerPixel;

        try
        {
            LoadWindow(newStart);

            _centerTimeSec = keepCenterTime;
            _secondsPerPixel = keepSecondsPerPixel;
            _centerPrice = keepCenterPrice;
            _pricePerPixel = keepPricePerPixel;

            var plot = GetPlotRect(new Rect(0, 0, Bounds.Width, Bounds.Height));
            if (plot.Width > 0) ClampCenterTimeToWindow(plot);

            Debug.WriteLine($"ReloadWindow: newStart={newStart} windowStart={_windowStart} loaded={_windowLoaded}");

            InvalidateVisual();

        }
        finally
        {
            _reloadInProgress = false;
        }
    }

    // =========================
    // Prefetch margin dynamique
    // =========================
    private int GetPrefetchMargin()
    {
        // 1/4 des données réellement chargées, clampé
        return ClampInt(_windowLoaded / 4, 10, 60);
    }

    // =========================
    // Reload automatique basé sur index visible (stable)
    // =========================
    private void EnsureWindowAroundView(Rect plot)
    {
        if (_file is null) return;
        if (_windowLoaded <= 0) return;
        if (_reloadInProgress) return;

        // Clamp centre avant de calculer, sinon "centre dans le futur" => index bizarre
        ClampCenterTimeToWindow(plot);

        int centerLocal = FindClosestIndexInWindow(_centerTimeSec);
        if (centerLocal < 0) return;

        int margin = GetPrefetchMargin();

        if (centerLocal < margin)
        {
            long centerGlobal = _windowStart + centerLocal;
            long newStart = centerGlobal - (WindowCount / 2);
            ReloadWindow(newStart);
            return;
        }

        if (centerLocal > (_windowLoaded - 1 - margin))
        {
            long centerGlobal = _windowStart + centerLocal;
            long newStart = centerGlobal - (WindowCount / 2);
            ReloadWindow(newStart);
        }
    }

    private int FindClosestIndexInWindow(double targetTimeSec)
    {
        if (_windowLoaded <= 0) return -1;

        // binaire sur temps (suppose trié)
        int lo = 0, hi = _windowLoaded - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            double t = TsNsToEpochSeconds(_ts[mid]);

            if (t < targetTimeSec) lo = mid + 1;
            else if (t > targetTimeSec) hi = mid - 1;
            else return mid;
        }

        int i0 = ClampInt(lo, 0, _windowLoaded - 1);
        int i1 = ClampInt(lo - 1, 0, _windowLoaded - 1);

        double d0 = Math.Abs(TsNsToEpochSeconds(_ts[i0]) - targetTimeSec);
        double d1 = Math.Abs(TsNsToEpochSeconds(_ts[i1]) - targetTimeSec);

        return d0 < d1 ? i0 : i1;
    }

    // =========================
    // Init X/Y (une seule fois)
    // =========================
    private void InitViewXFromWindow(Rect plot)
    {
        if (_windowLoaded <= 0) return;

        double dt = EstimateDtSecondsWindow();
        if (dt <= 0) dt = 60;

        _secondsPerPixel = (VisibleCount * dt) / Math.Max(1.0, plot.Width);

        // centre sur la dernière bougie chargée
        double lastT = TsNsToEpochSeconds(_ts[_windowLoaded - 1]);
        _centerTimeSec = lastT - (plot.Width * 0.5) * _secondsPerPixel;

        ClampZoomToGapWindow();
        ClampCenterTimeToWindow(plot);
    }

    private void AutoScaleYFromWindow(Rect plot)
    {
        if (_windowLoaded <= 0) return;
        if (plot.Height <= 0) return;

        double minP = double.PositiveInfinity;
        double maxP = double.NegativeInfinity;

        for (int i = 0; i < _windowLoaded; i++)
        {
            double low = _l[i] / PriceScale;
            double high = _h[i] / PriceScale;
            if (low < minP) minP = low;
            if (high > maxP) maxP = high;
        }

        if (!double.IsFinite(minP) || !double.IsFinite(maxP) || maxP <= minP)
        {
            minP = 0; maxP = 1;
        }

        _centerPrice = (minP + maxP) * 0.5;
        _pricePerPixel = ((maxP - minP) * 1.20) / plot.Height;
        if (_pricePerPixel <= 0) _pricePerPixel = 1e-6;
    }

    // =========================
    // Pointer interaction
    // =========================
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var plot = GetPlotRect(new Rect(0, 0, Bounds.Width, Bounds.Height));
        var p = e.GetPosition(this);

        bool inYAxis = p.X < plot.Left && p.Y >= plot.Top && p.Y <= plot.Bottom;

        _lastPoint = p;

        if (inYAxis)
        {
            _isZoomingY = true;
            _yZoomAnchorT = (plot.Bottom - p.Y) / plot.Height;
            _yZoomAnchorPrice = YToPrice(p.Y, plot);

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        _isPanning = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        _isPanning = false;
        _isZoomingY = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (!_isPanning && !_isZoomingY) return;
        if (_windowLoaded <= 0) return;

        var plot = GetPlotRect(new Rect(0, 0, Bounds.Width, Bounds.Height));

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

            _centerPrice = _yZoomAnchorPrice - (_yZoomAnchorT - 0.5) * newSpan;

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _centerTimeSec -= dx * _secondsPerPixel;
        _centerPrice += dy * _pricePerPixel;

        EnsureWindowAroundView(plot);

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (_windowLoaded <= 0) return;

        var plot = GetPlotRect(new Rect(0, 0, Bounds.Width, Bounds.Height));
        if (plot.Width <= 0 || plot.Height <= 0) return;

        var mouse = e.GetPosition(this);
        var anchor = plot.Contains(mouse)
            ? mouse
            : new Point(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2);

        double t0 = ScreenXToWorldTime(anchor.X, plot);

        double factor = e.Delta.Y > 0 ? 1.10 : 1.0 / 1.10;
        _secondsPerPixel = Clamp(_secondsPerPixel / factor, 1e-6, 1e6);

        ClampZoomToGapWindow();

        double t1 = ScreenXToWorldTime(anchor.X, plot);
        _centerTimeSec += (t0 - t1);

        EnsureWindowAroundView(plot);

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

        // background
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

        // IMPORTANT: clamp à chaque frame (sécurité anti “centre dans le futur”)
        ClampCenterTimeToWindow(plot);

        // range Y courant
        double visiblePriceRange = plot.Height * _pricePerPixel;
        _visibleMinPrice = _centerPrice - visiblePriceRange / 2.0;
        _visibleMaxPrice = _centerPrice + visiblePriceRange / 2.0;
        if (_visibleMaxPrice <= _visibleMinPrice) _visibleMaxPrice = _visibleMinPrice + 1e-9;

        double bodyW = ComputeBodyWidthWindow();

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

        //Debug.WriteLine($"dt={EstimateDtSecondsWindow()} spp={_secondsPerPixel} center={_centerTimeSec} loaded={_windowLoaded} plotW={plot.Width}");
        //Debug.WriteLine($"firstT={TsNsToEpochSeconds(_ts[0])} lastT={TsNsToEpochSeconds(_ts[_windowLoaded - 1])}");
    }

    // =========================
    // Helpers: X/Y mapping
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
    // Zoom clamp & widths
    // =========================
    private double EstimateDtSecondsWindow()
    {
        if (_windowLoaded < 2) return 60.0;

        long t0 = _ts[0];
        for (int i = 1; i < _windowLoaded; i++)
        {
            if (_ts[i] != t0)
            {
                double a = TsNsToEpochSeconds(t0);
                double b = TsNsToEpochSeconds(_ts[i]);
                return Math.Max(1e-6, Math.Abs(b - a));
            }
        }
        return 60.0;
    }

    private void ClampZoomToGapWindow()
    {
        double dtSec = EstimateDtSecondsWindow();

        double minPitch = BodyMin + GapMinPx;
        double maxPitch = BodyMax + GapMaxPx;

        double minSecondsPerPixel = dtSec / maxPitch;
        double maxSecondsPerPixel = dtSec / minPitch;

        _secondsPerPixel = Clamp(_secondsPerPixel, minSecondsPerPixel, maxSecondsPerPixel);
    }

    private double ComputeBodyWidthWindow()
    {
        double dtSec = EstimateDtSecondsWindow();
        double pxPerCandle = dtSec / _secondsPerPixel;

        double pxClamped = Clamp(pxPerCandle, GapMinPx + 1.0, GapMaxPx + BodyMax);
        double desired = pxClamped * 0.70;
        double maxAllowedByGap = Math.Max(1.0, pxClamped - GapMinPx);

        return Clamp(desired, BodyMin, Math.Min(BodyMax, maxAllowedByGap));
    }

    // =========================
    // Axes (simple)
    // =========================
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

    private static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
    private static int ClampInt(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    private static long ClampLong(long v, long min, long max) => v < min ? min : (v > max ? max : v);

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