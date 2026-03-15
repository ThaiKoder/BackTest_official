using System;
using System.Collections.Generic;

namespace BacktestApp.Indicators;

public sealed class SessionHighLowIndicator
{
    private const int StartHour = 10;
    private const int EndHour = 12;

    private bool _active;
    private long _startTs;
    private long _endTs;

    private double _high;
    private double _low;

    private readonly List<SessionBox> _boxes = new();

    public IReadOnlyList<SessionBox> Boxes => _boxes;

    public void Reset()
    {
        _boxes.Clear();
        _active = false;
    }

    public void OnCandle(long ts, long high, long low, double priceScale)
    {
        long sec = ts / 1_000_000_000L;
        var dt = DateTimeOffset.FromUnixTimeSeconds(sec).UtcDateTime;

        int hour = dt.Hour;

        double h = high / priceScale;
        double l = low / priceScale;

        if (!_active)
        {
            if (hour == StartHour && dt.Minute == 0)
            {
                _active = true;
                _startTs = ts;
                _endTs = ts;

                _high = h;
                _low = l;
            }

            return;
        }

        _endTs = ts;

        if (h > _high) _high = h;
        if (l < _low) _low = l;

        if (hour >= EndHour)
        {
            _boxes.Add(new SessionBox(
                _startTs,
                _endTs,
                _high,
                _low));

            _active = false;
        }
    }

    public sealed record SessionBox(
        long StartTs,
        long EndTs,
        double High,
        double Low
    );
}