using Avalonia;
using Avalonia.Media;
using System;
using System.Collections.Generic;

namespace BacktestApp.Indicators;

public sealed class FvgIndicator : IGraphIndicator
{
    private sealed record Candle(
        long Ts,
        double Open,
        double High,
        double Low,
        double Close);

    public sealed record FvgZone(
        long AnchorTs,
        long EndTs,
        double Low,
        double High,
        bool IsBullish);

    private readonly List<FvgZone> _zones = new();

    private readonly IBrush _bullFill;
    private readonly Pen _bullBorder;

    private readonly IBrush _bearFill;
    private readonly Pen _bearBorder;

    private Candle? _c1;
    private Candle? _c2;

    public string Name { get; }

    public IReadOnlyList<FvgZone> Zones => _zones;

    public FvgIndicator(
        string name,
        IBrush? bullFill = null,
        Pen? bullBorder = null,
        IBrush? bearFill = null,
        Pen? bearBorder = null)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "FVG" : name;

        _bullFill = bullFill ?? new SolidColorBrush(Color.FromArgb(40, 0, 200, 120));
        _bullBorder = bullBorder ?? new Pen(new SolidColorBrush(Color.FromArgb(220, 0, 200, 120)), 1);

        _bearFill = bearFill ?? new SolidColorBrush(Color.FromArgb(40, 220, 70, 70));
        _bearBorder = bearBorder ?? new Pen(new SolidColorBrush(Color.FromArgb(220, 220, 70, 70)), 1);
    }

    public void Reset()
    {
        _zones.Clear();
        _c1 = null;
        _c2 = null;
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

        if (_c1 is not null && _c2 is not null)
        {
            TryCreateFvg(_c1, _c2, current);
        }

        _c1 = _c2;
        _c2 = current;
    }

    private void TryCreateFvg(Candle c1, Candle c2, Candle c3)
    {
        const long oneMinuteNs = 60L * 1_000_000_000L;
        const int projectionCandles = 20;

        bool risingStructure =
            c1.High < c2.High && c2.High <= c3.High &&
            c1.Low < c2.Low && c2.Low <= c3.Low;

        bool fallingStructure =
            c1.High > c2.High && c2.High >= c3.High &&
            c1.Low > c2.Low && c2.Low >= c3.Low;

        bool bullishGap = c1.High < c3.Low;
        bool bearishGap = c1.Low > c3.High;

        long anchorTs = c2.Ts; // FVG attaché à la bougie du milieu
        long endTs = anchorTs + projectionCandles * oneMinuteNs;

        if (risingStructure && bullishGap)
        {
            double zoneLow = c1.High;
            double zoneHigh = c3.Low;

            if (zoneHigh > zoneLow)
            {
                _zones.Add(new FvgZone(
                    anchorTs,
                    endTs,
                    zoneLow,
                    zoneHigh,
                    IsBullish: true));
            }
        }

        if (fallingStructure && bearishGap)
        {
            double zoneLow = c3.High;
            double zoneHigh = c1.Low;

            if (zoneHigh > zoneLow)
            {
                _zones.Add(new FvgZone(
                    anchorTs,
                    endTs,
                    zoneLow,
                    zoneHigh,
                    IsBullish: false));
            }
        }
    }

    public void Render(
        DrawingContext ctx,
        Rect plot,
        Func<long, double> tsToX,
        Func<double, double> priceToY)
    {
        for (int i = 0; i < _zones.Count; i++)
        {
            var z = _zones[i];

            double x1 = tsToX(z.AnchorTs);
            double x2 = tsToX(z.EndTs);

            if (x2 < plot.Left || x1 > plot.Right)
                continue;

            double yTop = priceToY(z.High);
            double yBottom = priceToY(z.Low);

            double left = Math.Min(x1, x2);
            double right = Math.Max(x1, x2);
            double top = Math.Min(yTop, yBottom);
            double bottom = Math.Max(yTop, yBottom);

            var rect = new Rect(
                new Point(left, top),
                new Point(right, bottom));

            ctx.FillRectangle(z.IsBullish ? _bullFill : _bearFill, rect);
            ctx.DrawRectangle(null, z.IsBullish ? _bullBorder : _bearBorder, rect);
        }
    }
}