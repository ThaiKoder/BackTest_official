using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;

namespace BacktestApp.Controls;

/// <summary>
/// LEVEL 3 (split):
/// - MMAP (fichier 2)
/// - Fenêtre glissante (WindowCount)
/// - Pan/Zoom (fichier 4)
/// - Render + axes (fichier 5)
/// - View math (fichier 3)
/// - Utils (fichier 6)
/// </summary>
public sealed partial class CandleChartControl : Control
{
    // =========================
    // Params rendu
    // =========================
    private const double PriceScale = 1.0;

    private const double BodyMin = 3.0;
    private const double BodyMax = 250;

    private const double GapMinPx = 2.0;
    private const double GapMaxPx = 4.0;

    private const int VisibleCount = 10;
    private const int WindowCount = 40; // divided by 4 to get candle in view

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

    // NOTE: SymbolSize vient de MmapCandleFile (déclaré dans fichier 2)
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
            DebugMessage.Write("[CandleChartControl] Aucun .bin trouvé");
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
        _edgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
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
}