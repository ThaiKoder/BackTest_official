using System;

namespace BacktestApp.Indicators;

public sealed class SessionHighLowIndicator
{
    public enum SessionState
    {
        Out = 0,
        In = 1
    }

    public sealed record Output(
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

    private SessionState _state = SessionState.Out;

    // zone en cours
    private bool _zoneActive;
    private double _currentZoneHigh;
    private double _currentZoneLow;
    private long _currentStartTs;
    private long _currentEndTs;

    // dernière zone validée
    private bool _hasLast;
    private double _lastHigh;
    private double _lastLow;
    private long _lastStartTs;
    private long _lastEndTs;

    // avant-dernière zone validée
    private bool _hasPrevious;
    private double _previousHigh;
    private double _previousLow;
    private long _previousStartTs;
    private long _previousEndTs;

    public SessionState State => _state;

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

    public Output OnCandle(long ts, long high, long low, double priceScale)
    {
        long sec = ts / 1_000_000_000L;
        var dt = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;

        bool isIn = IsInSession(dt);

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

            // sortie de zone : on valide une seule fois
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

        return new Output(
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
    }

    private static bool IsInSession(DateTime dtUtc)
    {
        TimeSpan t = dtUtc.TimeOfDay;
        return t >= new TimeSpan(10, 0, 0)
            && t < new TimeSpan(12, 0, 0);
    }
}