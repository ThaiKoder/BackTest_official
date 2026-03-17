using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using DatasetTool;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using BacktestApp.Indicators;
using Avalonia.Media;
using static BacktestApp.Controls.DebugMessage;



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
    // Indicators (black-box)
    // =========================
    private readonly List<IGraphIndicator> _indicators = new();

    private void InitializeIndicators()
    {
        _indicators.Clear();

        _indicators.Add(new SessionHighLowIndicator(
            "Asian",
            new TimeSpan(1, 0, 0),
            new TimeSpan(5, 0, 0),
            new SolidColorBrush(Color.FromArgb(20, 255, 0, 0)),
            new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 0, 0)), 2)));

        _indicators.Add(new SessionHighLowIndicator(
            "London",
            new TimeSpan(7, 0, 0),
            new TimeSpan(10, 0, 0),
            new SolidColorBrush(Color.FromArgb(20, 0, 0, 255)),
            new Pen(new SolidColorBrush(Color.FromArgb(255, 0, 0, 255)), 2)));

        _indicators.Add(new SessionHighLowIndicator(
            "NY AM",
            new TimeSpan(13, 30, 0),
            new TimeSpan(16, 0, 0),
            new SolidColorBrush(Color.FromArgb(20, 0, 0, 255)),
            new Pen(new SolidColorBrush(Color.FromArgb(255, 0, 0, 255)), 2)));

        _indicators.Add(new SessionHighLowIndicator(
            "Between London - NY AM",
            new TimeSpan(10, 0, 0),
            new TimeSpan(13, 30, 0),
            new SolidColorBrush(Color.FromArgb(20, 0, 0, 255)),
            new Pen(new SolidColorBrush(Color.FromArgb(255, 0, 0, 255)), 2)));

        _indicators.Add(new FvgIndicator("FVG"));

        _indicators.Add(new LiquidityLevelsIndicator("ICT Liquidity"));
    }

    private void ResetIndicators()
    {
        for (int i = 0; i < _indicators.Count; i++)
            _indicators[i].Reset();
    }

    private void FeedIndicators(
        long ts,
        long open,
        long high,
        long low,
        long close,
        uint volume,
        byte sym)
    {
        for (int i = 0; i < _indicators.Count; i++)
        {
            _indicators[i].OnCandle(
                ts,
                open,
                high,
                low,
                close,
                volume,
                sym,
                PriceScale);
        }
    }

    private void DrawIndicators(DrawingContext ctx, Rect plot)
    {
        for (int i = 0; i < _indicators.Count; i++)
        {
            _indicators[i].Render(
                ctx,
                plot,
                ts => WorldTimeToScreenX(TsNsToEpochSeconds(ts), plot),
                price => PriceToY(price, plot));
        }
    }

    // =========================
    // Ring buffer
    // =========================
    private int _ringHead;          // index physique du 1er élément logique
    private int _ringCount;         // nombre d’éléments valides dans le ring
    private long _ringFirstGlobalIdx; // index global fichier du 1er élément logique

    // =========================
    // Axis buffers (recyclage fixe)
    // =========================
    private const int MaxAxisTicks = 32;

    private readonly double[] _xTickTimes = new double[MaxAxisTicks];
    private readonly double[] _xTickPixels = new double[MaxAxisTicks];
    private int _xTickCount;
    private int _xTickStepSec;

    private readonly double[] _yTickPrices = new double[MaxAxisTicks];
    private readonly double[] _yTickPixels = new double[MaxAxisTicks];
    private int _yTickCount;
    private double _yTickStepPrice;


    // Pas dynamiques de l'axe X selon le zoom
    private static readonly int[] XAxisStepsSec =
    {
        60,          // 1m
        5 * 60,      // 5m
        15 * 60,     // 15m
        30 * 60,     // 30m
        60 * 60,     // 1h
        4 * 60 * 60, // 4h
        24 * 60 * 60 // 1d
    };


    // =========================
    // Params rendu
    // =========================
    private const double PriceScale = 1.0;

    private const double BodyMin = 3.0;
    private const double BodyMax = 250;

    private const double GapMinPx = 2.0;
    private const double GapMaxPx = 4.0;

    private const int VisibleCount = 500;
    private const int WindowCount = VisibleCount * 4; // divided by 4 to get candle in view

    private const int UiFileRange = 3;
    private const int UiCandleRange = (WindowCount - 1) / 2;
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
    private byte[] _sym = new byte[WindowCount * MmapCandleFile.SymbolSize];

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


    private int _currentIdx = -1;
    private const int LoadStartTail = 5000; // "read un peu avant la fin"
    private MmapCandleFile? _fM2, _fM1, _f0, _fP1, _fP2;
    private int _iM2 = -1, _iM1 = -1, _i0 = -1, _iP1 = -1, _iP2 = -1;


    public CandleChartControl()
    {
        Focusable = true;
        InitializeIndicators();
    }

    private uint[] _starts = Array.Empty<uint>();
    private uint[] _ends = Array.Empty<uint>();


    // =========================
    // Attach / detach
    // =========================
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (_loadedOnce) return;
        _loadedOnce = true;

        InitializeFilesAndCandlesMode();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        _edgeTimer?.Stop();
        _edgeTimer = null;

        DisposeSlot(ref _fM2, ref _iM2);
        DisposeSlot(ref _fM1, ref _iM1);
        DisposeSlot(ref _f0, ref _i0);
        DisposeSlot(ref _fP1, ref _iP1);
        DisposeSlot(ref _fP2, ref _iP2);

        _file?.Dispose();
        _file = null;

        _uiCandleIndex?.Dispose();
        _uiCandleIndex = null;
        _uiFileIndex?.Dispose();
        _uiFileIndex = null;
        _uiCandleStep = null;
        _uiFileStep = null;
    }

}