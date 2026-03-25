using Avalonia;
using Avalonia.Media;
using System;
using System.Globalization;

namespace BacktestApp.Indicators;

public sealed class SessionHighLowIndicator : IGraphIndicator
{
    public enum SessionState
    {
        Out = 0,
        In = 1
    }

    public sealed record Output(
        string Name,
        SessionState State,
        bool HasPrevious,
        double PreviousHigh,
        double PreviousLow,
        long PreviousStartTs,
        long PreviousEndTs,
        bool HasLast,
        double LastHigh,
        double LastLow,
        long LastStartTs,
        long LastEndTs
    );

    private readonly string _name;
    private readonly TimeSpan _startTime;
    private readonly TimeSpan _endTime;
    private readonly IBrush _fill;
    private readonly Pen _border;

    private SessionState _state = SessionState.Out;

    private bool _zoneActive;
    private double _currentZoneHigh;
    private double _currentZoneLow;
    private long _currentStartTs;
    private long _currentEndTs;

    private bool _hasLast;
    private double _lastHigh;
    private double _lastLow;
    private long _lastStartTs;
    private long _lastEndTs;

    private bool _hasPrevious;
    private double _previousHigh;
    private double _previousLow;
    private long _previousStartTs;
    private long _previousEndTs;

    public string Name => _name;

    public Output CurrentOutput =>
        new Output(
            _name,
            _state,
            _hasPrevious,
            _previousHigh,
            _previousLow,
            _previousStartTs,
            _previousEndTs,
            _hasLast,
            _lastHigh,
            _lastLow,
            _lastStartTs,
            _lastEndTs
        );

    // constructeur UI
    public SessionHighLowIndicator(
        string name,
        TimeSpan startTime,
        TimeSpan endTime,
        IBrush fill,
        Pen border)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("name vide", nameof(name));

        if (startTime == endTime)
            throw new ArgumentException("startTime et endTime ne doivent pas être égaux.");

        _name = name;
        _startTime = startTime;
        _endTime = endTime;
        _fill = fill ?? throw new ArgumentNullException(nameof(fill));
        _border = border ?? throw new ArgumentNullException(nameof(border));
    }


    // constructeur test / backtest
    public SessionHighLowIndicator(
        string name,
        TimeSpan startTime,
        TimeSpan endTime)
        : this(
            name,
            startTime,
            endTime,
            new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)),
            new Pen(new SolidColorBrush(Color.FromArgb(0, 255, 255, 255)), 1))
    {
    }

    public void Reset()
    {
        _state = SessionState.Out;

        _zoneActive = false;
        _currentZoneHigh = 0;
        _currentZoneLow = 0;
        _currentStartTs = 0;
        _currentEndTs = 0;

        _hasLast = false;
        _lastHigh = 0;
        _lastLow = 0;
        _lastStartTs = 0;
        _lastEndTs = 0;

        _hasPrevious = false;
        _previousHigh = 0;
        _previousLow = 0;
        _previousStartTs = 0;
        _previousEndTs = 0;
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
        long sec = ts / 1_000_000_000L;
        var dt = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;

        bool isIn = IsInSession(dt.TimeOfDay);

        double h = high / priceScale;
        double l = low / priceScale;

        if (isIn)
        {
            _state = SessionState.In;

            if (!_zoneActive)
            {
                _zoneActive = true;
                _currentStartTs = ts;
                _currentEndTs = ts;
                _currentZoneHigh = h;
                _currentZoneLow = l;
            }
            else
            {
                _currentEndTs = ts;

                if (h > _currentZoneHigh)
                    _currentZoneHigh = h;

                if (l < _currentZoneLow)
                    _currentZoneLow = l;
            }
        }
        else
        {
            _state = SessionState.Out;

            if (_zoneActive)
            {
                if (_hasLast)
                {
                    _hasPrevious = true;
                    _previousHigh = _lastHigh;
                    _previousLow = _lastLow;
                    _previousStartTs = _lastStartTs;
                    _previousEndTs = _lastEndTs;
                }

                _hasLast = true;
                _lastHigh = _currentZoneHigh;
                _lastLow = _currentZoneLow;
                _lastStartTs = _currentStartTs;
                _lastEndTs = _currentEndTs;

                _zoneActive = false;
                _currentStartTs = 0;
                _currentEndTs = 0;
            }
        }
    }

    public void Render(
        DrawingContext ctx,
        Rect plot,
        Func<long, double> tsToX,
        Func<double, double> priceToY)
    {
        if (!_hasLast)
            return;

        double x1 = tsToX(_lastStartTs);
        double x2 = tsToX(_lastEndTs);

        double yHigh = priceToY(_lastHigh);
        double yLow = priceToY(_lastLow);

        double left = Math.Min(x1, x2);
        double width = Math.Abs(x2 - x1);

        double top = Math.Min(yHigh, yLow);
        double height = Math.Abs(yLow - yHigh);

        if (width <= 0 || height <= 0)
            return;

        var rect = new Rect(left, top, width, height);
        ctx.DrawRectangle(_fill, _border, rect);

        var ft = new FormattedText(
            _name,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            12,
            _border.Brush);

        double textX = left + width * 0.5 - ft.Width / 2.0;
        double textY = top + 4.0;

        ctx.DrawText(ft, new Point(textX, textY));
    }

    private bool IsInSession(TimeSpan t)
    {
        // session normale dans la même journée
        if (_startTime < _endTime)
            return t >= _startTime && t < _endTime;

        // session qui traverse minuit
        return t >= _startTime || t < _endTime;
    }
}