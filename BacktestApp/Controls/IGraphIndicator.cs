using Avalonia;
using Avalonia.Media;
using System;

namespace BacktestApp.Indicators;

public interface IGraphIndicator
{
    string Name { get; }

    void Reset();

    void OnCandle(
        long ts,
        long open,
        long high,
        long low,
        long close,
        uint volume,
        byte sym,
        double priceScale);

    void Render(
        DrawingContext ctx,
        Rect plot,
        Func<long, double> tsToX,
        Func<double, double> priceToY);
}