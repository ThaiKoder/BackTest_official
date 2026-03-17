using Avalonia;
using Avalonia.Media;
using System;
using System.Globalization;

namespace BacktestApp.Indicators;

public sealed class LiquidityLevelsIndicator : IGraphIndicator
{
    private sealed record Candle(
        long Ts,
        double Open,
        double High,
        double Low,
        double Close);

    private enum LegDirection
    {
        Unknown,
        Up,
        Down
    }

    public enum LiquidityLevelKind
    {
        Pdh,
        Pdl,
        SwingHigh,
        SwingLow
    }

    public sealed record LiquidityLevel(
        string Name,
        LiquidityLevelKind Kind,
        long StartTs,
        long EndTs,
        double Price);

    public sealed record Output(
        LiquidityLevel? Pdh,
        LiquidityLevel? Pdl,
        LiquidityLevel? LastSwingHigh,
        LiquidityLevel? LastSwingLow);

    private readonly Pen _pdhPen;
    private readonly Pen _pdlPen;
    private readonly Pen _swingHighPen;
    private readonly Pen _swingLowPen;

    private Candle? _prev;

    private DateTime? _currentDayUtc;
    private long _currentDayLastTs;
    private double _currentDayHigh;
    private double _currentDayLow;
    private bool _dayInitialized;

    private LiquidityLevel? _pdh;
    private LiquidityLevel? _pdl;
    private LiquidityLevel? _lastSwingHigh;
    private LiquidityLevel? _lastSwingLow;

    private LegDirection _legDirection = LegDirection.Unknown;

    private double _legHighPrice;
    private long _legHighTs;

    private double _legLowPrice;
    private long _legLowTs;

    private const long TwelveHoursNs = 12L * 60L * 60L * 1_000_000_000L;

    public string Name { get; }

    public Output CurrentOutput =>
        new Output(_pdh, _pdl, _lastSwingHigh, _lastSwingLow);

    public LiquidityLevelsIndicator(
        string name = "ICT Liquidity",
        Pen? pdhPen = null,
        Pen? pdlPen = null,
        Pen? swingHighPen = null,
        Pen? swingLowPen = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "ICT Liquidity" : name;

        _pdhPen = pdhPen ?? new Pen(
            new SolidColorBrush(Color.FromArgb(255, 255, 215, 0)), 1);

        _pdlPen = pdlPen ?? new Pen(
            new SolidColorBrush(Color.FromArgb(255, 255, 140, 0)), 1);

        _swingHighPen = swingHighPen ?? new Pen(
            new SolidColorBrush(Color.FromArgb(255, 255, 80, 80)), 1);

        _swingLowPen = swingLowPen ?? new Pen(
            new SolidColorBrush(Color.FromArgb(255, 80, 170, 255)), 1);
    }

    public void Reset()
    {
        _prev = null;

        _currentDayUtc = null;
        _currentDayLastTs = 0;
        _currentDayHigh = 0;
        _currentDayLow = 0;
        _dayInitialized = false;

        _pdh = null;
        _pdl = null;
        _lastSwingHigh = null;
        _lastSwingLow = null;

        _legDirection = LegDirection.Unknown;
        _legHighPrice = 0;
        _legHighTs = 0;
        _legLowPrice = 0;
        _legLowTs = 0;
    }

    public void OnCandle(
        long ts,
        long open,
        long high,
        long low,
        long close,
        uint volume,
        byte sym,
        double priceScale)
    {
        double o = open / priceScale;
        double h = high / priceScale;
        double l = low / priceScale;
        double c = close / priceScale;

        var current = new Candle(ts, o, h, l, c);

        HandleDailyLevels(current);
        HandleSwingLevelsByLeg(current);

        _prev = current;
    }

    private void HandleDailyLevels(Candle current)
    {
        DateTime candleDayUtc = UtcDateFromNs(current.Ts);

        if (!_dayInitialized)
        {
            _currentDayUtc = candleDayUtc;
            _currentDayLastTs = current.Ts;
            _currentDayHigh = current.High;
            _currentDayLow = current.Low;
            _dayInitialized = true;
            return;
        }

        if (_currentDayUtc == candleDayUtc)
        {
            if (current.High > _currentDayHigh)
                _currentDayHigh = current.High;

            if (current.Low < _currentDayLow)
                _currentDayLow = current.Low;

            _currentDayLastTs = current.Ts;
            return;
        }

        _pdh = new LiquidityLevel(
            "PDH",
            LiquidityLevelKind.Pdh,
            _currentDayLastTs,
            _currentDayLastTs + TwelveHoursNs,
            _currentDayHigh);

        _pdl = new LiquidityLevel(
            "PDL",
            LiquidityLevelKind.Pdl,
            _currentDayLastTs,
            _currentDayLastTs + TwelveHoursNs,
            _currentDayLow);

        _currentDayUtc = candleDayUtc;
        _currentDayLastTs = current.Ts;
        _currentDayHigh = current.High;
        _currentDayLow = current.Low;
    }

    private void HandleSwingLevelsByLeg(Candle current)
    {
        if (_prev is null)
        {
            _prev = current;
            _legHighPrice = current.High;
            _legHighTs = current.Ts;
            _legLowPrice = current.Low;
            _legLowTs = current.Ts;
            return;
        }

        Candle prev = _prev;

        bool bullishRotation = current.High > prev.High && current.Low > prev.Low;
        bool bearishRotation = current.High < prev.High && current.Low < prev.Low;

        // Initialisation de la première jambe
        if (_legDirection == LegDirection.Unknown)
        {
            if (bullishRotation)
            {
                _legDirection = LegDirection.Up;

                _legHighPrice = Math.Max(prev.High, current.High);
                _legHighTs = current.High >= prev.High ? current.Ts : prev.Ts;

                _legLowPrice = Math.Min(prev.Low, current.Low);
                _legLowTs = prev.Low <= current.Low ? prev.Ts : current.Ts;
            }
            else if (bearishRotation)
            {
                _legDirection = LegDirection.Down;

                _legHighPrice = Math.Max(prev.High, current.High);
                _legHighTs = prev.High >= current.High ? prev.Ts : current.Ts;

                _legLowPrice = Math.Min(prev.Low, current.Low);
                _legLowTs = current.Low <= prev.Low ? current.Ts : prev.Ts;
            }
            else
            {
                if (current.High > _legHighPrice || _legHighTs == 0)
                {
                    _legHighPrice = current.High;
                    _legHighTs = current.Ts;
                }

                if (current.Low < _legLowPrice || _legLowTs == 0)
                {
                    _legLowPrice = current.Low;
                    _legLowTs = current.Ts;
                }
            }

            return;
        }

        if (_legDirection == LegDirection.Up)
        {
            if (current.High >= _legHighPrice)
            {
                _legHighPrice = current.High;
                _legHighTs = current.Ts;
            }

            if (bearishRotation)
            {
                _lastSwingHigh = new LiquidityLevel(
                    "Last Swing High",
                    LiquidityLevelKind.SwingHigh,
                    _legHighTs,
                    _legHighTs + TwelveHoursNs,
                    _legHighPrice);

                _legDirection = LegDirection.Down;

                _legLowPrice = current.Low;
                _legLowTs = current.Ts;

                _legHighPrice = current.High;
                _legHighTs = current.Ts;
                return;
            }

            // outside bar ou extension par le bas : on garde la jambe
            if (current.Low < _legLowPrice)
            {
                _legLowPrice = current.Low;
                _legLowTs = current.Ts;
            }

            return;
        }

        if (_legDirection == LegDirection.Down)
        {
            if (current.Low <= _legLowPrice)
            {
                _legLowPrice = current.Low;
                _legLowTs = current.Ts;
            }

            if (bullishRotation)
            {
                _lastSwingLow = new LiquidityLevel(
                    "Last Swing Low",
                    LiquidityLevelKind.SwingLow,
                    _legLowTs,
                    _legLowTs + TwelveHoursNs,
                    _legLowPrice);

                _legDirection = LegDirection.Up;

                _legHighPrice = current.High;
                _legHighTs = current.Ts;

                _legLowPrice = current.Low;
                _legLowTs = current.Ts;
                return;
            }

            // outside bar ou extension par le haut : on garde la jambe
            if (current.High > _legHighPrice)
            {
                _legHighPrice = current.High;
                _legHighTs = current.Ts;
            }
        }
    }

    public void Render(
        DrawingContext ctx,
        Rect plot,
        Func<long, double> tsToX,
        Func<double, double> priceToY)
    {
        DrawLevel(ctx, plot, tsToX, priceToY, _pdh);
        DrawLevel(ctx, plot, tsToX, priceToY, _pdl);
        DrawLevel(ctx, plot, tsToX, priceToY, _lastSwingHigh);
        DrawLevel(ctx, plot, tsToX, priceToY, _lastSwingLow);
    }

    private void DrawLevel(
        DrawingContext ctx,
        Rect plot,
        Func<long, double> tsToX,
        Func<double, double> priceToY,
        LiquidityLevel? level)
    {
        if (level is null)
            return;

        double x1 = tsToX(level.StartTs);
        double x2 = tsToX(level.EndTs);
        double y = priceToY(level.Price);

        if (x2 < plot.Left || x1 > plot.Right)
            return;

        var pen = GetPen(level.Kind);

        ctx.DrawLine(
            pen,
            new Point(x1, y),
            new Point(x2, y));

        DrawLabel(ctx, level.Name, pen.Brush, x2 + 4, y - 10);
    }

    private Pen GetPen(LiquidityLevelKind kind)
    {
        return kind switch
        {
            LiquidityLevelKind.Pdh => _pdhPen,
            LiquidityLevelKind.Pdl => _pdlPen,
            LiquidityLevelKind.SwingHigh => _swingHighPen,
            LiquidityLevelKind.SwingLow => _swingLowPen,
            _ => _pdhPen
        };
    }

    private static DateTime UtcDateFromNs(long tsNs)
    {
        long sec = tsNs / 1_000_000_000L;
        return DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime.Date;
    }

    private static void DrawLabel(
        DrawingContext ctx,
        string text,
        IBrush? brush,
        double x,
        double y)
    {
        if (brush is null)
            return;

        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            11,
            brush);

        ctx.DrawText(ft, new Point(x, y));
    }
}