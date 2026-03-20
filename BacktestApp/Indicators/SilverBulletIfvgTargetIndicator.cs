using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BacktestApp.Indicators;

public sealed class SilverBulletIfvgTargetIndicator : IGraphIndicator
{
    private sealed record Candle(
        long Ts,
        double Open,
        double High,
        double Low,
        double Close);

    private sealed record SessionSnapshot(
        long StartTs,
        long EndTs,
        double High,
        double Low);

    private sealed record FvgZone(
        long AnchorTs,
        long EndTs,
        double Low,
        double High,
        bool IsBullish);

    private enum SweepSide
    {
        None = 0,
        High = 1,
        Low = 2
    }

    private enum SetupPhase
    {
        WaitingSession = 0,
        WaitingSweep = 1,
        WaitingIfvg = 2,
        WaitingIfvgTouch = 3,
        WaitingTarget = 4,
        Done = 5
    }

    private sealed class Setup
    {
        public SessionSnapshot Session = null!;
        public SetupPhase Phase = SetupPhase.WaitingSession;

        public SweepSide SweptSide = SweepSide.None;
        public long SweepTs;

        public FvgZone? FirstIfvg;
        public long IfvgTouchTs;

        // Stop loss = extrémité formée entre fin de session et premier IFVG
        public bool HasStopLoss;
        public double StopLossPrice;
        public long StopLossTs;
        public long StopLossStartTs;
        public long StopLossEndTs;

        // Extrême temporaire pendant l'attente du first IFVG
        public bool HasTrackedExtreme;
        public double TrackedExtremePrice;
        public long TrackedExtremeTs;

        public double TargetPrice;
        public long TargetStartTs;
        public long TargetEndTs;
        public bool TargetReached;
        public long TargetReachedTs;

        public bool StopLossHit;
        public long StopLossHitTs;
    }

    private readonly string _name;

    private readonly TimeSpan _sessionStart;
    private readonly TimeSpan _sessionEnd;

    private readonly IBrush _sessionFill;
    private readonly Pen _sessionBorder;

    private readonly IBrush _bullIfvgFill;
    private readonly Pen _bullIfvgBorder;

    private readonly IBrush _bearIfvgFill;
    private readonly Pen _bearIfvgBorder;

    private readonly IBrush _targetFill;
    private readonly Pen _targetBorder;

    private readonly IBrush _slFill;
    private readonly Pen _slBorder;

    private Candle? _c1;
    private Candle? _c2;

    private bool _sessionActive;
    private long _currentSessionStartTs;
    private long _currentSessionEndTs;
    private double _currentSessionHigh;
    private double _currentSessionLow;

    private readonly List<Setup> _setups = new();
    private Setup? _activeSetup;

    private const long OneMinuteNs = 60L * 1_000_000_000L;
    private const int FvgProjectionCandles = 20;
    private const int TargetProjectionCandles = 30;
    private const int SlProjectionCandles = 30;

    public string Name => _name;

    public SilverBulletIfvgTargetIndicator(
        string name = "Silver Bullet IFVG Target",
        TimeSpan? sessionStart = null,
        TimeSpan? sessionEnd = null,
        IBrush? sessionFill = null,
        Pen? sessionBorder = null,
        IBrush? bullIfvgFill = null,
        Pen? bullIfvgBorder = null,
        IBrush? bearIfvgFill = null,
        Pen? bearIfvgBorder = null,
        IBrush? targetFill = null,
        Pen? targetBorder = null,
        IBrush? slFill = null,
        Pen? slBorder = null)
    {
        _name = string.IsNullOrWhiteSpace(name) ? "Silver Bullet IFVG Target" : name;

        _sessionStart = sessionStart ?? new TimeSpan(21, 0, 0);
        _sessionEnd = sessionEnd ?? new TimeSpan(22, 0, 0);

        _sessionFill = sessionFill ?? new SolidColorBrush(Color.FromArgb(18, 80, 160, 255));
        _sessionBorder = sessionBorder ?? new Pen(new SolidColorBrush(Color.FromArgb(220, 80, 160, 255)), 2);

        _bullIfvgFill = bullIfvgFill ?? new SolidColorBrush(Color.FromArgb(45, 0, 200, 120));
        _bullIfvgBorder = bullIfvgBorder ?? new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 200, 120)), 1);

        _bearIfvgFill = bearIfvgFill ?? new SolidColorBrush(Color.FromArgb(45, 220, 70, 70));
        _bearIfvgBorder = bearIfvgBorder ?? new Pen(new SolidColorBrush(Color.FromArgb(220, 220, 70, 70)), 1);

        _targetFill = targetFill ?? new SolidColorBrush(Color.FromArgb(25, 255, 215, 0));
        _targetBorder = targetBorder ?? new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 215, 0)), 2);

        _slFill = slFill ?? new SolidColorBrush(Color.FromArgb(25, 255, 255, 255));
        _slBorder = slBorder ?? new Pen(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), 2);
    }

    public void Reset()
    {
        _c1 = null;
        _c2 = null;

        _sessionActive = false;
        _currentSessionStartTs = 0;
        _currentSessionEndTs = 0;
        _currentSessionHigh = 0;
        _currentSessionLow = 0;

        _activeSetup = null;
        _setups.Clear();
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

        HandleSession(ts, h, l);

        var current = new Candle(ts, o, h, l, c);

        FvgZone? newFvg = null;
        if (_c1 is not null && _c2 is not null)
            newFvg = TryCreateFvg(_c1, _c2, current);

        ProcessActiveSetup(current, newFvg);

        _c1 = _c2;
        _c2 = current;
    }

    private void HandleSession(long ts, double high, double low)
    {
        long sec = ts / 1_000_000_000L;
        var dt = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;
        bool isIn = dt.TimeOfDay >= _sessionStart && dt.TimeOfDay < _sessionEnd;

        if (isIn)
        {
            if (!_sessionActive)
            {
                _sessionActive = true;
                _currentSessionStartTs = ts;
                _currentSessionEndTs = ts;
                _currentSessionHigh = high;
                _currentSessionLow = low;
            }
            else
            {
                _currentSessionEndTs = ts;

                if (high > _currentSessionHigh)
                    _currentSessionHigh = high;

                if (low < _currentSessionLow)
                    _currentSessionLow = low;
            }

            return;
        }

        if (_sessionActive)
        {
            var closedSession = new SessionSnapshot(
                _currentSessionStartTs,
                _currentSessionEndTs,
                _currentSessionHigh,
                _currentSessionLow);

            _activeSetup = new Setup
            {
                Session = closedSession,
                Phase = SetupPhase.WaitingSweep
            };

            _setups.Add(_activeSetup);

            _sessionActive = false;
            _currentSessionStartTs = 0;
            _currentSessionEndTs = 0;
            _currentSessionHigh = 0;
            _currentSessionLow = 0;
        }
    }

    private void ProcessActiveSetup(Candle current, FvgZone? newFvg)
    {
        if (_activeSetup is null)
            return;

        if (_activeSetup.Phase == SetupPhase.Done)
            return;

        if (current.Ts <= _activeSetup.Session.EndTs)
            return;

        if (_activeSetup.Phase == SetupPhase.WaitingSweep)
        {
            bool takesHigh = current.High > _activeSetup.Session.High;
            bool takesLow = current.Low < _activeSetup.Session.Low;

            if (takesHigh && !takesLow)
            {
                _activeSetup.SweptSide = SweepSide.High;
                _activeSetup.SweepTs = current.Ts;
                _activeSetup.Phase = SetupPhase.WaitingIfvg;

                _activeSetup.HasTrackedExtreme = true;
                _activeSetup.TrackedExtremePrice = current.High;
                _activeSetup.TrackedExtremeTs = current.Ts;
                return;
            }

            if (takesLow && !takesHigh)
            {
                _activeSetup.SweptSide = SweepSide.Low;
                _activeSetup.SweepTs = current.Ts;
                _activeSetup.Phase = SetupPhase.WaitingIfvg;

                _activeSetup.HasTrackedExtreme = true;
                _activeSetup.TrackedExtremePrice = current.Low;
                _activeSetup.TrackedExtremeTs = current.Ts;
                return;
            }

            return;
        }

        if (_activeSetup.Phase == SetupPhase.WaitingIfvg)
        {
            if (_activeSetup.SweptSide == SweepSide.High)
            {
                if (!_activeSetup.HasTrackedExtreme || current.High > _activeSetup.TrackedExtremePrice)
                {
                    _activeSetup.HasTrackedExtreme = true;
                    _activeSetup.TrackedExtremePrice = current.High;
                    _activeSetup.TrackedExtremeTs = current.Ts;
                }
            }
            else if (_activeSetup.SweptSide == SweepSide.Low)
            {
                if (!_activeSetup.HasTrackedExtreme || current.Low < _activeSetup.TrackedExtremePrice)
                {
                    _activeSetup.HasTrackedExtreme = true;
                    _activeSetup.TrackedExtremePrice = current.Low;
                    _activeSetup.TrackedExtremeTs = current.Ts;
                }
            }

            if (newFvg is null)
                return;

            if (newFvg.AnchorTs <= _activeSetup.SweepTs)
                return;

            bool expectedBullish = _activeSetup.SweptSide == SweepSide.Low;
            if (newFvg.IsBullish != expectedBullish)
                return;

            _activeSetup.FirstIfvg = newFvg;

            if (_activeSetup.HasTrackedExtreme)
            {
                _activeSetup.HasStopLoss = true;
                _activeSetup.StopLossPrice = _activeSetup.TrackedExtremePrice;
                _activeSetup.StopLossTs = _activeSetup.TrackedExtremeTs;
                _activeSetup.StopLossStartTs = _activeSetup.Session.EndTs;
                _activeSetup.StopLossEndTs = newFvg.AnchorTs + SlProjectionCandles * OneMinuteNs;
            }

            _activeSetup.Phase = SetupPhase.WaitingIfvgTouch;
            return;
        }

        if (_activeSetup.Phase == SetupPhase.WaitingIfvgTouch)
        {
            if (_activeSetup.FirstIfvg is null)
                return;

            if (!TouchesZone(current, _activeSetup.FirstIfvg.Low, _activeSetup.FirstIfvg.High))
                return;

            _activeSetup.IfvgTouchTs = current.Ts;

            _activeSetup.TargetPrice =
                _activeSetup.SweptSide == SweepSide.High
                    ? _activeSetup.Session.Low
                    : _activeSetup.Session.High;

            _activeSetup.TargetStartTs = current.Ts;
            _activeSetup.TargetEndTs = current.Ts + TargetProjectionCandles * OneMinuteNs;
            _activeSetup.Phase = SetupPhase.WaitingTarget;
            return;
        }

        if (_activeSetup.Phase == SetupPhase.WaitingTarget)
        {
            // STOP LOSS = FAIL
            if (_activeSetup.HasStopLoss)
            {
                if (_activeSetup.SweptSide == SweepSide.High)
                {
                    // setup vendeur : SL au-dessus
                    if (current.High >= _activeSetup.StopLossPrice)
                    {
                        _activeSetup.StopLossHit = true;
                        _activeSetup.StopLossHitTs = current.Ts;
                        _activeSetup.Phase = SetupPhase.Done;
                        return;
                    }
                }
                else if (_activeSetup.SweptSide == SweepSide.Low)
                {
                    // setup acheteur : SL en-dessous
                    if (current.Low <= _activeSetup.StopLossPrice)
                    {
                        _activeSetup.StopLossHit = true;
                        _activeSetup.StopLossHitTs = current.Ts;
                        _activeSetup.Phase = SetupPhase.Done;
                        return;
                    }
                }
            }

            // TARGET = SUCCESS
            if (_activeSetup.SweptSide == SweepSide.High)
            {
                if (current.Low <= _activeSetup.Session.Low)
                {
                    _activeSetup.TargetReached = true;
                    _activeSetup.TargetReachedTs = current.Ts;
                    _activeSetup.Phase = SetupPhase.Done;
                }

                return;
            }

            if (_activeSetup.SweptSide == SweepSide.Low)
            {
                if (current.High >= _activeSetup.Session.High)
                {
                    _activeSetup.TargetReached = true;
                    _activeSetup.TargetReachedTs = current.Ts;
                    _activeSetup.Phase = SetupPhase.Done;
                }
            }
        }
    }

    private static bool TouchesZone(Candle candle, double zoneLow, double zoneHigh)
    {
        return candle.High >= zoneLow && candle.Low <= zoneHigh;
    }

    private static FvgZone? TryCreateFvg(Candle c1, Candle c2, Candle c3)
    {
        bool risingStructure =
            c1.High < c2.High && c2.High <= c3.High &&
            c1.Low < c2.Low && c2.Low <= c3.Low;

        bool fallingStructure =
            c1.High > c2.High && c2.High >= c3.High &&
            c1.Low > c2.Low && c2.Low >= c3.Low;

        bool bullishGap = c1.High < c3.Low;
        bool bearishGap = c1.Low > c3.High;

        long anchorTs = c2.Ts;
        long endTs = anchorTs + FvgProjectionCandles * OneMinuteNs;

        if (risingStructure && bullishGap)
        {
            double zoneLow = c1.High;
            double zoneHigh = c3.Low;

            if (zoneHigh > zoneLow)
                return new FvgZone(anchorTs, endTs, zoneLow, zoneHigh, true);
        }

        if (fallingStructure && bearishGap)
        {
            double zoneLow = c3.High;
            double zoneHigh = c1.Low;

            if (zoneHigh > zoneLow)
                return new FvgZone(anchorTs, endTs, zoneLow, zoneHigh, false);
        }

        return null;
    }

    public void Render(
        DrawingContext ctx,
        Rect plot,
        Func<long, double> tsToX,
        Func<double, double> priceToY)
    {
        for (int i = 0; i < _setups.Count; i++)
        {
            var s = _setups[i];

            DrawSession(ctx, plot, tsToX, priceToY, s.Session);

            if (s.FirstIfvg is not null)
                DrawIfvg(ctx, plot, tsToX, priceToY, s.FirstIfvg);

            if (s.HasStopLoss)
                DrawStopLoss(ctx, plot, tsToX, priceToY, s);

            if (s.Phase >= SetupPhase.WaitingTarget || s.TargetReached || s.StopLossHit)
                DrawTarget(ctx, plot, tsToX, priceToY, s);
        }
    }

    private void DrawSession(
        DrawingContext ctx,
        Rect plot,
        Func<long, double> tsToX,
        Func<double, double> priceToY,
        SessionSnapshot session)
    {
        double x1 = tsToX(session.StartTs);
        double x2 = tsToX(session.EndTs);

        if (x2 < plot.Left || x1 > plot.Right)
            return;

        double yHigh = priceToY(session.High);
        double yLow = priceToY(session.Low);

        double left = Math.Min(x1, x2);
        double right = Math.Max(x1, x2);
        double top = Math.Min(yHigh, yLow);
        double bottom = Math.Max(yHigh, yLow);

        var rect = new Rect(new Point(left, top), new Point(right, bottom));

        ctx.FillRectangle(_sessionFill, rect);
        ctx.DrawRectangle(null, _sessionBorder, rect);

        DrawText(
            ctx,
            "Silver Bullet",
            _sessionBorder.Brush,
            left + 4,
            top + 4);
    }

    private void DrawIfvg(
        DrawingContext ctx,
        Rect plot,
        Func<long, double> tsToX,
        Func<double, double> priceToY,
        FvgZone z)
    {
        double x1 = tsToX(z.AnchorTs);
        double x2 = tsToX(z.EndTs);

        if (x2 < plot.Left || x1 > plot.Right)
            return;

        double yTop = priceToY(z.High);
        double yBottom = priceToY(z.Low);

        double left = Math.Min(x1, x2);
        double right = Math.Max(x1, x2);
        double top = Math.Min(yTop, yBottom);
        double bottom = Math.Max(yTop, yBottom);

        var rect = new Rect(new Point(left, top), new Point(right, bottom));

        ctx.FillRectangle(z.IsBullish ? _bullIfvgFill : _bearIfvgFill, rect);
        ctx.DrawRectangle(null, z.IsBullish ? _bullIfvgBorder : _bearIfvgBorder, rect);

        DrawText(
            ctx,
            "First IFVG",
            z.IsBullish ? _bullIfvgBorder.Brush : _bearIfvgBorder.Brush,
            left + 4,
            top + 4);
    }

    private void DrawStopLoss(
        DrawingContext ctx,
        Rect plot,
        Func<long, double> tsToX,
        Func<double, double> priceToY,
        Setup s)
    {
        const double halfHeight = 4.0;

        double x1 = tsToX(s.StopLossStartTs);
        double x2 = tsToX(s.StopLossEndTs);

        if (x2 < plot.Left || x1 > plot.Right)
            return;

        double y = priceToY(s.StopLossPrice);

        var rect = new Rect(
            new Point(Math.Min(x1, x2), y - halfHeight),
            new Point(Math.Max(x1, x2), y + halfHeight));

        ctx.FillRectangle(_slFill, rect);
        ctx.DrawRectangle(null, _slBorder, rect);

        DrawText(
            ctx,
            "SL",
            _slBorder.Brush,
            rect.Left + 4,
            rect.Top - 16);

        if (s.StopLossHit)
        {
            double hitX = tsToX(s.StopLossHitTs);
            ctx.DrawLine(
                _slBorder,
                new Point(hitX, rect.Top - 6),
                new Point(hitX, rect.Bottom + 6));
        }
    }

    private void DrawTarget(
        DrawingContext ctx,
        Rect plot,
        Func<long, double> tsToX,
        Func<double, double> priceToY,
        Setup s)
    {
        const double halfHeight = 4.0;

        double x1 = tsToX(s.TargetStartTs);
        double x2 = tsToX(s.TargetEndTs);

        if (x2 < plot.Left || x1 > plot.Right)
            return;

        double y = priceToY(s.TargetPrice);

        var rect = new Rect(
            new Point(Math.Min(x1, x2), y - halfHeight),
            new Point(Math.Max(x1, x2), y + halfHeight));

        ctx.FillRectangle(_targetFill, rect);
        ctx.DrawRectangle(null, _targetBorder, rect);

        DrawText(
            ctx,
            "Target",
            _targetBorder.Brush,
            rect.Left + 4,
            rect.Top - 16);

        if (s.TargetReached)
        {
            double hitX = tsToX(s.TargetReachedTs);
            ctx.DrawLine(
                _targetBorder,
                new Point(hitX, rect.Top - 6),
                new Point(hitX, rect.Bottom + 6));
        }
    }

    private static void DrawText(
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
            new Typeface("Segoe UI"),
            12,
            brush);

        ctx.DrawText(ft, new Point(x, y));
    }
}