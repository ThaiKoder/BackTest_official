using Avalonia;
using System;

namespace BacktestApp.Controls;

public sealed partial class CandleChartControl
{
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
    // Clamp centre X (anti “centre dans le futur”)
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
    // Prefetch margin dynamique
    // =========================
    private int GetPrefetchMargin()
    {
        // 1/4 des données réellement chargées, clampé
        return ClampInt(_windowLoaded / 4, 10, 60);
    }
}