//UI input

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Diagnostics;
using BacktestApp.Controls;

namespace BacktestApp.Controls;

public sealed partial class CandleChartControl
{
    // Hover state (évite spam Debug)
    private int _hoverLocalIndex = -1;

    // =========================
    // Crosshair / souris
    // =========================
    private bool _hasMouseInPlot;
    private Point _mousePlotPosition;

    // =========================
    // Pointer interaction
    // =========================
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        var plot = GetPlotRect(new Rect(0, 0, Bounds.Width, Bounds.Height));
        var p = e.GetPosition(this);

        // met à jour le crosshair immédiatement
        _hasMouseInPlot = plot.Contains(p);
        _mousePlotPosition = p;

        bool inYAxis = p.X < plot.Left && p.Y >= plot.Top && p.Y <= plot.Bottom;

        _lastPoint = p;

        if (inYAxis)
        {
            _isZoomingY = true;
            _yZoomAnchorT = (plot.Bottom - p.Y) / plot.Height;
            _yZoomAnchorPrice = YToPrice(p.Y, plot);

            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        _isPanning = true;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        _isPanning = false;
        _isZoomingY = false;
        e.Pointer.Capture(null);

        var plot = GetPlotRect(new Rect(0, 0, Bounds.Width, Bounds.Height));
        var p = e.GetPosition(this);

        _hasMouseInPlot = plot.Contains(p);
        _mousePlotPosition = p;

        InvalidateVisual();

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_windowLoaded <= 0) return;

        var plot = GetPlotRect(new Rect(0, 0, Bounds.Width, Bounds.Height));
        var p = e.GetPosition(this);

        // Mise à jour crosshair
        bool wasInPlot = _hasMouseInPlot;
        _hasMouseInPlot = plot.Contains(p);
        _mousePlotPosition = p;

        // --- Hover quand on ne pan/zoom pas ---
        if (!_isPanning && !_isZoomingY)
        {
            int hit = HitTestCandleLocalIndex(p, plot);
            DebugHoverCandleIfChanged(hit);

            // redraw si entrée/sortie ou déplacement dans le plot
            if (_hasMouseInPlot || wasInPlot != _hasMouseInPlot)
                InvalidateVisual();

            return;
        }

        // --- Pan/Zoom existant ---
        var dx = p.X - _lastPoint.X;
        var dy = p.Y - _lastPoint.Y;
        _lastPoint = p;

        if (_isZoomingY)
        {
            double factor = Math.Exp(dy * 0.01);
            double newPPP = Clamp(_pricePerPixel * factor, 1e-9, 1e9);

            double newSpan = plot.Height * newPPP;
            _pricePerPixel = newPPP;

            _centerPrice = _yZoomAnchorPrice - (_yZoomAnchorT - 0.5) * newSpan;

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        _centerTimeSec -= dx * _secondsPerPixel;
        _centerPrice += dy * _pricePerPixel;

        EnsureWindowAroundView(plot);

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        _hasMouseInPlot = false;
        _hoverLocalIndex = -1;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (_windowLoaded <= 0) return;

        var plot = GetPlotRect(new Rect(0, 0, Bounds.Width, Bounds.Height));
        if (plot.Width <= 0 || plot.Height <= 0) return;

        var mouse = e.GetPosition(this);

        _hasMouseInPlot = plot.Contains(mouse);
        _mousePlotPosition = mouse;

        var anchor = plot.Contains(mouse)
            ? mouse
            : new Point(plot.Left + plot.Width / 2, plot.Top + plot.Height / 2);

        double t0 = ScreenXToWorldTime(anchor.X, plot);

        double factor = e.Delta.Y > 0 ? 1.10 : 1.0 / 1.10;
        _secondsPerPixel = Clamp(_secondsPerPixel / factor, 1e-6, 1e6);

        ClampZoomToGapWindow();

        double t1 = ScreenXToWorldTime(anchor.X, plot);
        _centerTimeSec += (t0 - t1);

        EnsureWindowAroundView(plot);

        InvalidateVisual();
        e.Handled = true;
    }

    private int HitTestCandleLocalIndex(Point mouse, Rect plot)
    {
        if (_windowLoaded <= 0) return -1;
        if (!plot.Contains(mouse)) return -1;

        // 1) temps sous la souris
        double timeSec = ScreenXToWorldTime(mouse.X, plot);

        // 2) bougie la plus proche en temps
        int i = FindClosestIndexInWindow(timeSec);
        if (i < 0) return -1;

        // 3) check "sur la bougie" en X (hit-test simple)
        double tSec = TsNsToEpochSeconds(GetTs(i));
        double xCenter = WorldTimeToScreenX(tSec, plot);

        double bodyW = ComputeBodyWidthWindow();
        double halfW = bodyW * 0.5;

        // tolérance pour être moins “strict”
        const double Tol = 2.0;

        if (Math.Abs(mouse.X - xCenter) <= (halfW + Tol))
            return i;

        return -1;
    }

    private void DebugHoverCandleIfChanged(int newHoverIndex)
    {
        if (newHoverIndex == _hoverLocalIndex) return; // pas de spam

        _hoverLocalIndex = newHoverIndex;

        if (newHoverIndex < 0) return;

        long ts = GetTs(newHoverIndex);
        double o = GetO(newHoverIndex) / PriceScale;
        double h = GetH(newHoverIndex) / PriceScale;
        double l = GetL(newHoverIndex) / PriceScale;
        double c = GetC(newHoverIndex) / PriceScale;
        uint v = GetV(newHoverIndex);

        // Symbol: 1 byte (ton format actuel)
        byte sym = GetSym(newHoverIndex);

        DebugMessage.Write($"[HOVER] i={newHoverIndex} " +
            $"date(UTC)={FormatTsUtc(ts)} " +
            $"O={o} H={h} L={l} C={c} V={v} Sym={sym}");
    }

    private static string FormatTsUtc(long tsNs)
    {
        long sec = tsNs / 1_000_000_000L;
        long nsRemainder = tsNs - sec * 1_000_000_000L;
        var dto = DateTimeOffset.FromUnixTimeSeconds(sec).ToUniversalTime();
        return $"{dto:yyyy-MM-dd HH:mm:ss} (+{nsRemainder}ns)";
    }
}