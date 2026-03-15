using System;
using System.IO;
using DatasetTool;

namespace BacktestApp.Controls;

public sealed partial class CandleChartControl
{
    private FileIndex? _uiFileIndex;
    private CandleIndex? _uiCandleIndex;
    private FileIndex.FileCursorStep? _uiFileStep;
    private CandleIndex.CandleCursorStep? _uiCandleStep;

    private const int UiFileRange = 3;
    private const int UiCandleRange = 3;

    private void InitializeFilesAndCandlesMode()
    {
        if (_uiFileIndex != null)
            return;

        _uiFileIndex = new FileIndex();
        _uiFileIndex.Load(Path.Combine("data", "bin", "_index.bin"));

        _uiFileStep = _uiFileIndex.FilesNext(0, UiFileRange);
        if (_uiFileStep.CurrentIdx < 0)
        {
            DebugMessage.Write("[CandleChartControl] _index.bin vide ou premier curseur invalide");
            return;
        }

        LoadCandlesForCurrentFileStep();
    }

    private void LoadCandlesForCurrentFileStep()
    {
        if (_uiFileIndex == null || _uiFileStep == null)
            return;

        var (startYmd, endYmd) = _uiFileIndex.Read(_uiFileStep.CurrentIdx);
        string filePath = Path.Combine(
            "data",
            "bin",
            $"glbx-mdp3-{startYmd}-{endYmd}.ohlcv-1m.bin");

        _uiCandleIndex?.Dispose();
        _uiCandleIndex = new CandleIndex();
        _uiCandleIndex.Load(filePath, UiFileRange);

        _uiCandleStep = _uiCandleIndex.CandlesNext(0, UiCandleRange);
        ApplyCandleStepToWindow(_uiCandleStep, resetView: true);
    }

    private void ApplyCandleStepToWindow(CandleIndex.CandleCursorStep step, bool resetView)
    {
        // 1) Cas initial / fallback : on reconstruit depuis step.Window
        if (resetView || _windowLoaded <= 0)
        {
            RebuildWindowFromStep(step);

            if (resetView)
            {
                _xInited = false;
                _yInited = false;
            }

            DebugMessage.Write($"[CandleChartControl] FULL step current={step.CurrentIdx} next={step.NextCursorIdx} loaded={_windowLoaded}");
            InvalidateVisual();
            return;
        }

        // 2) Cas incrémental : on exploite Removed + Added
        int removeCount = step.Removed.Count;
        int addCount = step.Added.Count;

        // sécurité
        if (removeCount < 0) removeCount = 0;
        if (addCount < 0) addCount = 0;
        if (removeCount > _windowLoaded) removeCount = _windowLoaded;

        int remain = _windowLoaded - removeCount;

        // Shift gauche sur les buffers existants
        if (removeCount > 0 && remain > 0)
        {
            Array.Copy(_ts, removeCount, _ts, 0, remain);
            Array.Copy(_o, removeCount, _o, 0, remain);
            Array.Copy(_h, removeCount, _h, 0, remain);
            Array.Copy(_l, removeCount, _l, 0, remain);
            Array.Copy(_c, removeCount, _c, 0, remain);
            Array.Copy(_v, removeCount, _v, 0, remain);
            Array.Copy(_sym, removeCount, _sym, 0, remain);
        }

        // Append des nouvelles candles à droite
        int write = remain;
        for (int i = 0; i < addCount && write < WindowCount; i++, write++)
        {
            var candle = step.Added[i];

            _ts[write] = candle.Ts;
            _o[write] = candle.O;
            _h[write] = candle.H;
            _l[write] = candle.L;
            _c[write] = candle.C;
            _v[write] = candle.V;
            _sym[write] = candle.Sym;
        }

        _windowLoaded = write;
        _windowStart += removeCount;
        _fileCount = _uiCandleIndex?.Count ?? 0;

        DebugMessage.Write(
            $"[CandleChartControl] INCR step current={step.CurrentIdx} next={step.NextCursorIdx} " +
            $"removed={removeCount} added={addCount} loaded={_windowLoaded} windowStart={_windowStart}");

        InvalidateVisual();
    }


    private void RebuildWindowFromStep(CandleIndex.CandleCursorStep step)
    {
        int filled = 0;
        int firstValidIdx = -1;

        foreach (var candle in step.Window)
        {
            if (candle.Idx == -1)
                continue;

            if (firstValidIdx == -1)
                firstValidIdx = candle.Idx;

            if (filled >= WindowCount)
                break;

            _ts[filled] = candle.Ts;
            _o[filled] = candle.O;
            _h[filled] = candle.H;
            _l[filled] = candle.L;
            _c[filled] = candle.C;
            _v[filled] = candle.V;
            _sym[filled] = candle.Sym;
            filled++;
        }

        _windowLoaded = filled;
        _windowStart = Math.Max(0, firstValidIdx);
        _fileCount = _uiCandleIndex?.Count ?? 0;
    }


    private bool AdvanceCandlesNext()
    {
        if (_uiCandleIndex == null || _uiCandleStep == null)
            return false;

        if (_uiCandleStep.NextCursorIdx == -1)
        {
            DebugMessage.Write("[CandleChartControl] fin du fichier courant pour CandlesNext");
            return false;
        }

        _uiCandleStep = _uiCandleIndex.CandlesNext(_uiCandleStep.NextCursorIdx, UiCandleRange);
        ApplyCandleStepToWindow(_uiCandleStep, resetView: false);
        return true;
    }
}
